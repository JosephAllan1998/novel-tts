using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NovelTTS.Commands;
using NovelTTS.Data;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Models;
using NovelTTS.Services.Merge;
using NovelTTS.Services.TTS;
using NovelTTS.ViewModels.Base;

namespace NovelTTS.ViewModels
{
    public class MergeTtsViewModel : ViewModelBase
    {
        private readonly Dispatcher _uiDispatcher;

        // ─── Project context (set from main window) ────────────────────────────
        private NovelProject _project;
        private DatabaseManager _dbManager;
        private AppLogger _logger;
        private ChapterRepository _chapterRepo;
        private MergeJobRepository _mergeRepo;
        private AudioJobRepository _audioRepo;

        // ─── Merge state ───────────────────────────────────────────────────────
        private int _chaptersPerMerge = 10;
        private int _mergeProgress = 0;
        private int _mergeTotal = 100;
        private string _mergeStatus = "Sẵn sàng";
        private bool _isMergeRunning = false;

        // ─── TTS state ─────────────────────────────────────────────────────────
        private List<string> _installedVoices = new List<string>();
        private string _selectedVoice = "";
        private int _speechRate = 0;
        private int _ttsThreads = 2;
        private AudioFormat _audioFormat = AudioFormat.MP3;
        private int _ttsProgress = 0;
        private int _ttsTotal = 100;
        private string _ttsStatus = "Sẵn sàng";
        private bool _isTtsRunning = false;
        private string _logText = "";

        private TtsService _ttsService;
        private CancellationTokenSource _mergeCts;
        private CancellationTokenSource _ttsCts;

        // ─── Properties ────────────────────────────────────────────────────────
        public int ChaptersPerMerge { get => _chaptersPerMerge; set => SetField(ref _chaptersPerMerge, value); }
        public int MergeProgress { get => _mergeProgress; set => SetField(ref _mergeProgress, value); }
        public int MergeTotal { get => _mergeTotal; set => SetField(ref _mergeTotal, value); }
        public string MergeStatus { get => _mergeStatus; set => SetField(ref _mergeStatus, value); }
        public bool IsMergeRunning { get => _isMergeRunning; set { SetField(ref _isMergeRunning, value); RaiseMergeCommands(); } }

        public List<string> InstalledVoices { get => _installedVoices; set => SetField(ref _installedVoices, value); }
        public string SelectedVoice { get => _selectedVoice; set => SetField(ref _selectedVoice, value); }
        public int SpeechRate { get => _speechRate; set => SetField(ref _speechRate, value); }
        public int TtsThreads { get => _ttsThreads; set => SetField(ref _ttsThreads, Math.Max(1, Math.Min(5, value))); }
        public AudioFormat AudioFormat { get => _audioFormat; set => SetField(ref _audioFormat, value); }
        public int TtsProgress { get => _ttsProgress; set => SetField(ref _ttsProgress, value); }
        public int TtsTotal { get => _ttsTotal; set => SetField(ref _ttsTotal, value); }
        public string TtsStatus { get => _ttsStatus; set => SetField(ref _ttsStatus, value); }
        public bool IsTtsRunning { get => _isTtsRunning; set { SetField(ref _isTtsRunning, value); RaiseTtsCommands(); } }
        public string LogText { get => _logText; set => SetField(ref _logText, value); }

        // ─── Commands ──────────────────────────────────────────────────────────
        public RelayCommand RunMergeCommand { get; }
        public RelayCommand CancelMergeCommand { get; }
        public RelayCommand RunTtsCommand { get; }
        public RelayCommand PauseTtsCommand { get; }
        public RelayCommand ResumeTtsCommand { get; }
        public RelayCommand CancelTtsCommand { get; }
        public RelayCommand ClearLogCommand { get; }

        public MergeTtsViewModel(Dispatcher dispatcher)
        {
            _uiDispatcher = dispatcher;

            //RunMergeCommand = new RelayCommand(RunMerge, () => !IsMergeRunning && _project != null);
            //CancelMergeCommand = new RelayCommand(CancelMerge, () => IsMergeRunning);
            //RunTtsCommand = new RelayCommand(RunTts, () => !IsTtsRunning && _project != null);
            //PauseTtsCommand = new RelayCommand(_ => _ttsService?.Pause(), () => IsTtsRunning);
            //ResumeTtsCommand = new RelayCommand(_ => _ttsService?.Resume(), () => IsTtsRunning);
            //CancelTtsCommand = new RelayCommand(CancelTts, () => IsTtsRunning);
            RunMergeCommand = new RelayCommand(RunMerge);
            CancelMergeCommand = new RelayCommand(CancelMerge);
            RunTtsCommand = new RelayCommand(RunTts);
            PauseTtsCommand = new RelayCommand(_ => _ttsService?.Pause());
            ResumeTtsCommand = new RelayCommand(_ => _ttsService?.Resume());
            CancelTtsCommand = new RelayCommand(CancelTts);
            ClearLogCommand = new RelayCommand(_ => LogText = "");

            // Load installed voices in background
            LoadVoicesAsync();
        }

        public void SetProject(NovelProject project, DatabaseManager dbManager, AppLogger logger)
        {
            try
            {
                _project = project;
                _dbManager = dbManager;
                _logger = logger;
                _chapterRepo = new ChapterRepository(dbManager);
                _mergeRepo = new MergeJobRepository(dbManager);
                _audioRepo = new AudioJobRepository(dbManager);
                ChaptersPerMerge = project.ChaptersPerMerge;
                RaiseMergeCommands();
                RaiseTtsCommands();
                AppendLog($"Project loaded: {project.NovelSlug}");
            }
            catch (Exception ex) { AppendLog($"[ERROR] SetProject: {ex.Message}"); }
        }

        // ─── Merge ─────────────────────────────────────────────────────────────

        private void RunMerge(object _)
        {
            try
            {
                if (_project == null) { MessageBox.Show("Chưa có project!"); return; }
                IsMergeRunning = true;
                MergeProgress = 0;
                MergeStatus = "Đang merge...";
                _mergeCts = new CancellationTokenSource();

                var svc = new MergeService(_project, _chapterRepo, _mergeRepo, _logger);
                svc.OnProgress += (d, t) => UiInvoke(() => { MergeProgress = d; if (t > 0) MergeTotal = t; });
                svc.OnStatusMessage += msg => UiInvoke(() => { MergeStatus = msg; AppendLog(msg); });

                var ct = _mergeCts.Token;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        svc.PlanMergeJobs(ChaptersPerMerge, ct);
                        svc.ExecuteMergeJobs(ct);
                        UiInvoke(() => { MergeStatus = "Merge hoàn tất!"; IsMergeRunning = false; AppendLog("[MERGE] Hoàn tất"); });
                    }
                    catch (OperationCanceledException)
                    {
                        UiInvoke(() => { MergeStatus = "Đã huỷ"; IsMergeRunning = false; });
                    }
                    catch (Exception ex)
                    {
                        UiInvoke(() => { MergeStatus = $"Lỗi: {ex.Message}"; IsMergeRunning = false; AppendLog($"[ERROR] {ex.Message}"); });
                    }
                }, ct);
            }
            catch (Exception ex) { AppendLog($"[ERROR] RunMerge: {ex.Message}"); IsMergeRunning = false; }
        }

        private void CancelMerge(object _)
        {
            try { _mergeCts?.Cancel(); } catch { }
        }

        // ─── TTS ───────────────────────────────────────────────────────────────

        private void RunTts(object _)
        {
            try
            {
                if (_project == null) { MessageBox.Show("Chưa có project!"); return; }
                IsTtsRunning = true;
                TtsProgress = 0;
                TtsStatus = "Đang chuyển đổi TTS...";
                _ttsCts = new CancellationTokenSource();

                _ttsService = new TtsService(
                    _project, _audioRepo, _mergeRepo, _logger,
                    SelectedVoice, TtsThreads, SpeechRate, AudioFormat);

                _ttsService.OnProgress += (d, t) => UiInvoke(() => { TtsProgress = d; if (t > 0) TtsTotal = t; });
                _ttsService.OnStatusMessage += msg => UiInvoke(() => { TtsStatus = msg; AppendLog(msg); });

                _ttsService.PlanAudioJobs();
                _ttsService.Start();

                // Monitor completion on background thread
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        while (_ttsService.IsRunning && !_ttsCts.Token.IsCancellationRequested)
                            Thread.Sleep(500);

                        UiInvoke(() =>
                        {
                            IsTtsRunning = false;
                            TtsStatus = _ttsCts.Token.IsCancellationRequested ? "Đã huỷ" : "TTS hoàn tất!";
                            AppendLog($"[TTS] {TtsStatus}");
                        });
                    }
                    catch (Exception ex) { UiInvoke(() => { IsTtsRunning = false; AppendLog($"[ERROR] {ex.Message}"); }); }
                }, _ttsCts.Token);
            }
            catch (Exception ex) { AppendLog($"[ERROR] RunTts: {ex.Message}"); IsTtsRunning = false; }
        }

        private void CancelTts(object _)
        {
            try
            {
                _ttsService?.Stop();
                _ttsCts?.Cancel();
            }
            catch { }
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private void LoadVoicesAsync()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var voices = TtsService.GetInstalledVoices();
                    UiInvoke(() =>
                    {
                        InstalledVoices = voices;
                        if (voices.Count > 0) SelectedVoice = voices[0];
                    });
                }
                catch { }
            });
        }

        private readonly StringBuilder _logBuffer = new StringBuilder();
        private void AppendLog(string msg)
        {
            _logBuffer.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (_logBuffer.Length > 60000) { _logBuffer.Remove(0, 20000); }
            LogText = _logBuffer.ToString();
        }

        private void UiInvoke(Action a) { try { _uiDispatcher.Invoke(a); } catch { } }
        private void RaiseMergeCommands() { RunMergeCommand.RaiseCanExecuteChanged(); CancelMergeCommand.RaiseCanExecuteChanged(); }
        private void RaiseTtsCommands() { RunTtsCommand.RaiseCanExecuteChanged(); CancelTtsCommand.RaiseCanExecuteChanged(); PauseTtsCommand.RaiseCanExecuteChanged(); ResumeTtsCommand.RaiseCanExecuteChanged(); }
    }
}
