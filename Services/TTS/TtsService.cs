using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Speech.Synthesis;
using System.Threading;
using NAudio.Lame;
using NAudio.Wave;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Models;

namespace NovelTTS.Services.TTS
{
    /// <summary>
    /// Converts merged TXT files to audio (WAV or MP3) using System.Speech.Synthesis.
    /// Supports up to 5 parallel threads, each with its own SpeechSynthesizer instance.
    /// Resume: skips jobs where output file already exists and status is Completed.
    /// </summary>
    public class TtsService : IDisposable
    {
        private readonly NovelProject       _project;
        private readonly AudioJobRepository _audioRepo;
        private readonly MergeJobRepository _mergeRepo;
        private readonly AppLogger          _logger;

        private readonly int       _parallelThreads;
        private readonly string    _voiceName;
        private readonly int       _speechRate;   // -10 to 10
        private readonly AudioFormat _format;

        private BlockingCollection<AudioJob> _ttsQueue;
        private List<Thread>                 _workerThreads;
        private CancellationTokenSource      _cts;
        private ManualResetEventSlim         _pauseEvent;

        private int _doneCount = 0;
        private int _totalCount = 0;

        public event Action<int, int>   OnProgress;
        public event Action<string>     OnStatusMessage;

        private bool _disposed;

        public TtsService(
            NovelProject project,
            AudioJobRepository audioRepo,
            MergeJobRepository mergeRepo,
            AppLogger logger,
            string voiceName    = "",
            int    parallelThreads = 2,
            int    speechRate   = 0,
            AudioFormat format  = AudioFormat.MP3)
        {
            _project         = project;
            _audioRepo       = audioRepo;
            _mergeRepo       = mergeRepo;
            _logger          = logger;
            _voiceName       = voiceName;
            _parallelThreads = Math.Max(1, Math.Min(parallelThreads, 5));
            _speechRate      = Math.Max(-10, Math.Min(10, speechRate));
            _format          = format;
        }

        // ─── Public API ────────────────────────────────────────────────────────

        public void PlanAudioJobs()
        {
            const string method = "TtsService.PlanAudioJobs";
            try
            {
                _logger.Tts(method, "Planning audio jobs");
                var completedMerges = _mergeRepo.GetCompleted(_project.ProjectId);
                var jobs = new List<AudioJob>();

                foreach (var merge in completedMerges)
                {
                    string ext    = _format == AudioFormat.MP3 ? "mp3" : "wav";
                    string fname  = Path.GetFileNameWithoutExtension(merge.OutputFilePath) + $".{ext}";
                    string outPath = Path.Combine(_project.AudioDir, fname);

                    jobs.Add(new AudioJob
                    {
                        ProjectId      = _project.ProjectId,
                        MergeJobId     = merge.JobId,
                        SourceFilePath = merge.OutputFilePath,
                        OutputFilePath = outPath,
                        AudioStatus    = AudioStatus.Pending,
                        Format         = _format
                    });
                }

                _audioRepo.DeleteByProject(_project.ProjectId);
                _audioRepo.BulkInsert(jobs);
                _totalCount = jobs.Count;

                _logger.Tts(method, $"Planned {jobs.Count} audio jobs");
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
                throw;
            }
        }

        public void Start()
        {
            const string method = "TtsService.Start";
            try
            {
                _logger.Tts(method, "Starting TTS pipeline");

                _cts        = new CancellationTokenSource();
                _pauseEvent = new ManualResetEventSlim(true);

                var pendingJobs = _audioRepo.GetPending(_project.ProjectId);
                _ttsQueue = new BlockingCollection<AudioJob>(boundedCapacity: 50);

                foreach (var job in pendingJobs)
                    _ttsQueue.Add(job);
                _ttsQueue.CompleteAdding();

                _workerThreads = new List<Thread>();
                for (int i = 0; i < _parallelThreads; i++)
                {
                    int threadIdx = i;
                    var t = new Thread(() => WorkerLoop(threadIdx, _cts.Token))
                    {
                        Name         = $"Thread-TTS-{threadIdx + 1}",
                        IsBackground = true
                    };
                    _workerThreads.Add(t);
                    t.Start();
                }

                _logger.Tts(method, $"{_parallelThreads} TTS worker threads started");
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
                throw;
            }
        }

        public void Pause()  { _pauseEvent?.Reset(); }
        public void Resume() { _pauseEvent?.Set(); }

        public void Stop()
        {
            try
            {
                _pauseEvent?.Set();
                _cts?.Cancel();
                _workerThreads?.ForEach(t => t.Join(TimeSpan.FromSeconds(15)));
                OnStatusMessage?.Invoke("[TTS] Đã dừng");
            }
            catch (Exception ex)
            {
                _logger.Error("TtsService.Stop", ex);
            }
        }

        public bool IsRunning => _workerThreads?.Exists(t => t.IsAlive) == true;

        // ─── Worker ────────────────────────────────────────────────────────────

        private void WorkerLoop(int threadIdx, CancellationToken ct)
        {
            const string method = "TtsService.WorkerLoop";

            SpeechSynthesizer synth = null;
            try
            {
                synth = CreateSynthesizer();
                _logger.Tts(method, $"Worker {threadIdx} started, voice={synth.Voice?.Name}");

                foreach (var job in _ttsQueue.GetConsumingEnumerable(ct))
                {
                    _pauseEvent.Wait(ct);
                    if (ct.IsCancellationRequested) break;

                    ProcessJob(job, synth, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Tts(method, $"Worker {threadIdx} cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
            }
            finally
            {
                synth?.Dispose();
                _logger.Tts(method, $"Worker {threadIdx} finished");
            }
        }

        private void ProcessJob(AudioJob job, SpeechSynthesizer synth, CancellationToken ct)
        {
            const string method = "TtsService.ProcessJob";
            try
            {
                _logger.Tts(method, $"Processing audio job {job.JobId}",
                    input: job.SourceFilePath, output: job.OutputFilePath);

                // Resume: skip if completed
                if (File.Exists(job.OutputFilePath) && new FileInfo(job.OutputFilePath).Length > 1000)
                {
                    _audioRepo.UpdateStatus(job.JobId, AudioStatus.Completed, job.OutputFilePath);
                    int c = Interlocked.Increment(ref _doneCount);
                    OnProgress?.Invoke(c, _totalCount);
                    return;
                }

                if (!File.Exists(job.SourceFilePath))
                {
                    _logger.Tts(method, $"Source not found: {job.SourceFilePath}");
                    _audioRepo.UpdateStatus(job.JobId, AudioStatus.Failed,
                        error: "Source file not found");
                    return;
                }

                _audioRepo.UpdateStatus(job.JobId, AudioStatus.InProgress);

                string text = File.ReadAllText(job.SourceFilePath, System.Text.Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _audioRepo.UpdateStatus(job.JobId, AudioStatus.Failed, error: "Empty text");
                    return;
                }

                string dir = Path.GetDirectoryName(job.OutputFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (job.Format == AudioFormat.WAV)
                {
                    synth.SetOutputToWaveFile(job.OutputFilePath);
                    synth.Speak(text);
                    synth.SetOutputToNull();
                }
                else // MP3
                {
                    string wavTmp = Path.ChangeExtension(job.OutputFilePath, ".tmp.wav");
                    synth.SetOutputToWaveFile(wavTmp);
                    synth.Speak(text);
                    synth.SetOutputToNull();

                    ConvertWavToMp3(wavTmp, job.OutputFilePath);

                    try { File.Delete(wavTmp); } catch { }
                }

                _audioRepo.UpdateStatus(job.JobId, AudioStatus.Completed, job.OutputFilePath);

                int done = Interlocked.Increment(ref _doneCount);
                OnProgress?.Invoke(done, _totalCount);
                OnStatusMessage?.Invoke($"[TTS] {done}/{_totalCount}: {Path.GetFileName(job.OutputFilePath)}");
                _logger.Tts(method, $"Audio job {job.JobId} complete", output: job.OutputFilePath);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Error(method, ex, input: job.SourceFilePath);
                try { _audioRepo.UpdateStatus(job.JobId, AudioStatus.Failed, error: ex.Message); } catch { }
            }
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private SpeechSynthesizer CreateSynthesizer()
        {
            try
            {
                var synth = new SpeechSynthesizer();
                synth.Rate   = _speechRate;
                synth.Volume = 100;

                if (!string.IsNullOrWhiteSpace(_voiceName))
                {
                    try { synth.SelectVoice(_voiceName); }
                    catch
                    {
                        _logger.Tts("TtsService.CreateSynthesizer",
                            $"Voice '{_voiceName}' not found, using default");
                    }
                }

                return synth;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[TtsService.CreateSynthesizer] {ex.Message}", ex);
            }
        }

        private void ConvertWavToMp3(string wavPath, string mp3Path)
        {
            const string method = "TtsService.ConvertWavToMp3";
            try
            {
                using (var reader = new AudioFileReader(wavPath))
                using (var writer = new LameMP3FileWriter(mp3Path, reader.WaveFormat, LAMEPreset.STANDARD))
                {
                    reader.CopyTo(writer);
                }
                _logger.Tts(method, $"WAV → MP3 OK", input: wavPath, output: mp3Path);
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex, input: wavPath);
                throw;
            }
        }

        /// <summary>Returns all installed voice names on this machine.</summary>
        public static List<string> GetInstalledVoices()
        {
            var result = new List<string>();
            try
            {
                using (var synth = new SpeechSynthesizer())
                {
                    foreach (var voice in synth.GetInstalledVoices())
                    {
                        if (voice.Enabled)
                            result.Add(voice.VoiceInfo.Name);
                    }
                }
            }
            catch { }
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _cts?.Dispose();
            _ttsQueue?.Dispose();
            _disposed = true;
        }
    }
}
