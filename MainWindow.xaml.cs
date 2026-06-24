using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Ookii.Dialogs.Wpf;
using NovelTTS.Data;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Http;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Infrastructure.Pipeline;
using NovelTTS.Models;
using NovelTTS.Services.Merge;
using NovelTTS.Services.Project;
using NovelTTS.Services.TTS;

namespace NovelTTS
{
    public partial class MainWindow : Window
    {
        // ─── Pipeline ──────────────────────────────────────────────────────────
        private ProjectService _projectService;
        private NovelProject _currentProject;
        private DatabaseManager _dbManager;
        private AppLogger _logger;
        private PipelineCoordinator _pipeline;
        private MergeService _mergeService;
        private TtsService _ttsService;
        private ChapterRepository _chapterRepo;
        private MergeJobRepository _mergeRepo;
        private AudioJobRepository _audioRepo;

        // ─── Pipeline running flags ────────────────────────────────────────────
        private bool _pipelineRunning = false;
        private bool _mergeRunning = false;
        private bool _ttsRunning = false;
        private System.Threading.CancellationTokenSource _mergeCts;
        private System.Threading.CancellationTokenSource _ttsCts;

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                _projectService = new ProjectService(new AppLogger(
                    Path.Combine(Path.GetTempPath(), "NovelTTS", "logs")));

                LoadInstalledVoices();
                WireSliders();
                SetStatus("✅ Sẵn sàng");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Khởi động lỗi: {ex.Message}", "NovelTTS");
            }
        }

        // ═══ CRAWLER TAB ══════════════════════════════════════════════════════

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new VistaFolderBrowserDialog
                {
                    Description = "Chọn thư mục lưu dự án",
                    UseDescriptionForTitle = true
                };
                if (dlg.ShowDialog() == true)
                    TxtWorkDir.Text = dlg.SelectedPath;
            }
            catch (Exception ex) { AppendCrawlLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = TxtUrl.Text.Trim();
                string workDir = TxtWorkDir.Text.Trim();

                if (string.IsNullOrWhiteSpace(url)) { MessageBox.Show("Vui lòng nhập URL truyện."); return; }
                if (string.IsNullOrWhiteSpace(workDir)) { MessageBox.Show("Vui lòng chọn thư mục lưu."); return; }

                if (!int.TryParse(TxtChaptersPerMerge.Text, out int chaptersPerMerge) || chaptersPerMerge < 1)
                    chaptersPerMerge = 10;
                if (!int.TryParse(TxtRetryCount.Text, out int retryCount) || retryCount < 1)
                    retryCount = 4;
                if (!int.TryParse(TxtMinDelay.Text, out int minDelay) || minDelay < 0)
                    minDelay = 800;
                if (!int.TryParse(TxtMaxDelay.Text, out int maxDelay) || maxDelay < minDelay)
                    maxDelay = 2500;

                // Reset UI
                PbCrawl.Value = PbDownload.Value = PbParse.Value = 0;
                TxtCrawlPct.Text = TxtDownloadPct.Text = TxtParsePct.Text = "0";
                TxtCrawlLog.Text = "";
                AppendCrawlLog("═══════════════════════════════════════");
                AppendCrawlLog($"[START] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                AppendCrawlLog($"URL: {url}  WorkDir: {workDir}");

                // Create or open project
                _currentProject = _projectService.CreateProject(url, workDir, chaptersPerMerge);
                _dbManager = _projectService.GetDatabaseManager(_currentProject);
                _logger = new AppLogger(_currentProject.LogsDir);
                _chapterRepo = new ChapterRepository(_dbManager);

                TxtProjectName.Text = _currentProject.NovelSlug;

                var ask = MessageBox.Show($"Project '{_currentProject.NovelSlug}' đã được tạo/khởi động.\nBạn có muốn tiếp tục crawl truyện này không?", "Tiếp tục crawl", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ask == MessageBoxResult.No) return;

                // Build and start pipeline
                var httpProvider = new HttpClientProvider(30);
                var retryFactory = new RetryPolicyFactory(retryCount, minDelay);

                _pipeline = new PipelineCoordinator(
                    _currentProject, _dbManager, _chapterRepo, _logger, retryFactory, httpProvider);

                _pipeline.OnStatusMessage += msg =>
                    Dispatcher.Invoke(() => AppendCrawlLog(msg));

                _pipeline.OnCrawlProgress += (done, total) =>
                    Dispatcher.Invoke(() =>
                    {
                        int max = total > 0 ? total : Math.Max(100, done + 10);
                        PbCrawl.Maximum = max;
                        PbCrawl.Value = Math.Min(done, max);
                        TxtCrawlPct.Text = done.ToString();
                    });

                _pipeline.OnDownloadProgress += (done, total) =>
                    Dispatcher.Invoke(() =>
                    {
                        int max = total > 0 ? total : (int)PbCrawl.Value;
                        if (max < 1) max = 100;
                        PbDownload.Maximum = max;
                        PbDownload.Value = Math.Min(done, max);
                        TxtDownloadPct.Text = done.ToString();
                    });

                _pipeline.OnParseProgress += (done, total) =>
                    Dispatcher.Invoke(() =>
                    {
                        int max = total > 0 ? total : (int)PbDownload.Maximum;
                        if (max < 1) max = 100;
                        PbParse.Maximum = max;
                        PbParse.Value = Math.Min(done, max);
                        TxtParsePct.Text = done.ToString();
                    });

                _pipeline.Start();
                _pipelineRunning = true;
                SetCrawlerButtons(running: true);
                SetStatus("⚙ Đang crawl...");
                AppendCrawlLog("Pipeline đã khởi động (3 threads).");
                TxtMergeCount.Text = chaptersPerMerge.ToString();
            }
            catch (Exception ex)
            {
                AppendCrawlLog($"[ERROR] {ex.Message}\n{ex.StackTrace}");
                SetStatus($"❌ Lỗi: {ex.Message}");
                _pipelineRunning = false;
                SetCrawlerButtons(running: false);
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            try { _pipeline?.Pause(); SetStatus("⏸ Đã tạm dừng"); AppendCrawlLog("[PAUSE]"); }
            catch (Exception ex) { AppendCrawlLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnResume_Click(object sender, RoutedEventArgs e)
        {
            try { _pipeline?.Resume(); SetStatus("⚙ Tiếp tục..."); AppendCrawlLog("[RESUME]"); }
            catch (Exception ex) { AppendCrawlLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _pipeline?.Stop();
                _pipelineRunning = false;
                SetCrawlerButtons(running: false);
                SetStatus("⏹ Đã dừng");
                AppendCrawlLog("[STOP]");
            }
            catch (Exception ex) { AppendCrawlLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnClearCrawlLog_Click(object sender, RoutedEventArgs e) => TxtCrawlLog.Text = "";

        // ═══ MERGE & TTS TAB ══════════════════════════════════════════════════

        private void BtnRunMerge_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureProjectLoaded();
                if (_currentProject == null) return;

                if (!int.TryParse(TxtMergeCount.Text, out int cpm) || cpm < 1) cpm = 10;

                _mergeRunning = true;
                BtnRunMerge.IsEnabled = false;
                BtnCancelMerge.IsEnabled = true;
                PbMerge.Value = 0;
                TxtMergeStatus.Text = "Đang merge...";
                AppendTtsLog($"[MERGE] Bắt đầu – {cpm} chương/file");

                _mergeCts = new System.Threading.CancellationTokenSource();
                _mergeRepo = new MergeJobRepository(_dbManager);

                var svc = new MergeService(_currentProject, _chapterRepo, _mergeRepo, _logger);
                svc.OnProgress += (d, t) =>
                    Dispatcher.Invoke(() => { PbMerge.Maximum = t > 0 ? t : 100; PbMerge.Value = Math.Min(d, PbMerge.Maximum); TxtMergeStatus.Text = $"{d}/{t}"; });
                svc.OnStatusMessage += msg =>
                    Dispatcher.Invoke(() => AppendTtsLog(msg));

                var ct = _mergeCts.Token;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        svc.PlanMergeJobs(cpm, ct);
                        svc.ExecuteMergeJobs(ct);
                        Dispatcher.Invoke(() =>
                        {
                            TxtMergeStatus.Text = "Hoàn tất!";
                            AppendTtsLog("[MERGE] Hoàn tất.");
                            _mergeRunning = false;
                            BtnRunMerge.IsEnabled = true;
                            BtnCancelMerge.IsEnabled = false;
                            SetStatus("✅ Merge hoàn tất");
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        Dispatcher.Invoke(() => { TxtMergeStatus.Text = "Đã huỷ"; _mergeRunning = false; BtnRunMerge.IsEnabled = true; BtnCancelMerge.IsEnabled = false; });
                    }
                    catch (Exception ex2)
                    {
                        Dispatcher.Invoke(() => { TxtMergeStatus.Text = $"Lỗi: {ex2.Message}"; AppendTtsLog($"[ERROR] {ex2.Message}"); _mergeRunning = false; BtnRunMerge.IsEnabled = true; BtnCancelMerge.IsEnabled = false; });
                    }
                }, ct);
            }
            catch (Exception ex) { AppendTtsLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnCancelMerge_Click(object sender, RoutedEventArgs e)
        {
            try { _mergeCts?.Cancel(); } catch { }
        }

        private void BtnRunTts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureProjectLoaded();
                if (_currentProject == null) return;

                _audioRepo = new AudioJobRepository(_dbManager);
                _mergeRepo = _mergeRepo ?? new MergeJobRepository(_dbManager);

                string voice = CboVoice.SelectedItem?.ToString() ?? "";
                int rate = (int)SliderRate.Value;
                int threads = (int)SliderTtsThreads.Value;
                var fmt = RbMp3.IsChecked == true ? AudioFormat.MP3 : AudioFormat.WAV;

                _ttsRunning = true;
                BtnRunTts.IsEnabled = false;
                BtnPauseTts.IsEnabled = true;
                BtnResumeTts.IsEnabled = true;
                BtnStopTts.IsEnabled = true;
                PbTts.Value = 0;
                TxtTtsStatus.Text = "Đang chạy TTS...";
                AppendTtsLog($"[TTS] Bắt đầu – voice={voice} threads={threads} format={fmt}");
                _ttsCts = new System.Threading.CancellationTokenSource();

                _ttsService = new TtsService(_currentProject, _audioRepo, _mergeRepo, _logger,
                    voice, threads, rate, fmt);

                _ttsService.OnProgress += (d, t) =>
                    Dispatcher.Invoke(() =>
                    {
                        PbTts.Maximum = t > 0 ? t : 100;
                        PbTts.Value = Math.Min(d, PbTts.Maximum);
                        TxtTtsStatus.Text = $"{d}/{t}";
                    });
                _ttsService.OnStatusMessage += msg =>
                    Dispatcher.Invoke(() => AppendTtsLog(msg));

                _ttsService.PlanAudioJobs();
                _ttsService.Start();

                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        while (_ttsService.IsRunning && !_ttsCts.IsCancellationRequested)
                            System.Threading.Thread.Sleep(500);
                        Dispatcher.Invoke(() =>
                        {
                            _ttsRunning = false;
                            TxtTtsStatus.Text = _ttsCts.IsCancellationRequested ? "Đã huỷ" : "Hoàn tất!";
                            AppendTtsLog($"[TTS] {TxtTtsStatus.Text}");
                            SetTtsButtons(false);
                            SetStatus("✅ TTS hoàn tất");
                        });
                    }
                    catch (Exception ex2) { Dispatcher.Invoke(() => { AppendTtsLog($"[ERROR] {ex2.Message}"); SetTtsButtons(false); }); }
                }, _ttsCts.Token);
            }
            catch (Exception ex) { AppendTtsLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnPauseTts_Click(object sender, RoutedEventArgs e)
        {
            try { _ttsService?.Pause(); SetStatus("⏸ TTS tạm dừng"); AppendTtsLog("[TTS PAUSE]"); }
            catch (Exception ex) { AppendTtsLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnResumeTts_Click(object sender, RoutedEventArgs e)
        {
            try { _ttsService?.Resume(); SetStatus("⚙ TTS tiếp tục"); AppendTtsLog("[TTS RESUME]"); }
            catch (Exception ex) { AppendTtsLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnStopTts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _ttsService?.Stop();
                _ttsCts?.Cancel();
                _ttsRunning = false;
                SetTtsButtons(false);
                SetStatus("⏹ TTS đã dừng");
                AppendTtsLog("[TTS STOP]");
            }
            catch (Exception ex) { AppendTtsLog($"[ERROR] {ex.Message}"); }
        }

        private void BtnClearTtsLog_Click(object sender, RoutedEventArgs e) => TxtTtsLog.Text = "";

        // ═══ LOG FILES TAB ════════════════════════════════════════════════════

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentProject == null)
                { TxtLogViewer.Text = "Chưa mở project nào."; return; }

                string tag = (sender as Button)?.Tag?.ToString() ?? "";
                string path = Path.Combine(_currentProject.LogsDir, tag);
                if (!File.Exists(path))
                { TxtLogViewer.Text = $"Log file không tồn tại:\n{path}"; return; }

                TxtLogViewer.Text = File.ReadAllText(path, Encoding.UTF8);
                TxtLogViewer.ScrollToEnd();
            }
            catch (Exception ex) { TxtLogViewer.Text = $"Lỗi đọc log: {ex.Message}"; }
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private void LoadInstalledVoices()
        {
            try
            {
                CboVoice.Items.Clear();
                var voices = TtsService.GetInstalledVoices();
                foreach (var v in voices) CboVoice.Items.Add(v);
                if (CboVoice.Items.Count > 0) CboVoice.SelectedIndex = 0;
            }
            catch { }
        }

        private void WireSliders()
        {
            try
            {
                SliderRate.ValueChanged += (_, __) =>
                    TxtRateVal.Text = ((int)SliderRate.Value).ToString();
                SliderTtsThreads.ValueChanged += (_, __) =>
                    TxtTtsThreadsVal.Text = ((int)SliderTtsThreads.Value).ToString();
            }
            catch { }
        }

        private void EnsureProjectLoaded()
        {
            if (_currentProject != null) return;
            MessageBox.Show("Vui lòng tạo/chạy project từ tab Crawler trước.", "Chưa có project");
        }

        private void SetStatus(string msg) { TxtStatus.Text = msg; }

        private void SetCrawlerButtons(bool running)
        {
            BtnStart.IsEnabled = !running;
            BtnPause.IsEnabled = running;
            BtnResume.IsEnabled = running;
            BtnStop.IsEnabled = running;
        }

        private void SetTtsButtons(bool running)
        {
            BtnRunTts.IsEnabled = !running;
            BtnPauseTts.IsEnabled = running;
            BtnResumeTts.IsEnabled = running;
            BtnStopTts.IsEnabled = running;
        }

        private readonly StringBuilder _crawlLogBuf = new StringBuilder();
        private readonly StringBuilder _ttsLogBuf = new StringBuilder();

        private void AppendCrawlLog(string msg)
        {
            _crawlLogBuf.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (_crawlLogBuf.Length > 100000)
            {
                string s = _crawlLogBuf.ToString();
                _crawlLogBuf.Clear();
                _crawlLogBuf.Append(s.Substring(s.Length / 2));
            }
            TxtCrawlLog.Text = _crawlLogBuf.ToString();
            TxtCrawlLog.ScrollToEnd();
        }

        private void AppendTtsLog(string msg)
        {
            _ttsLogBuf.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (_ttsLogBuf.Length > 80000)
            {
                string s = _ttsLogBuf.ToString();
                _ttsLogBuf.Clear();
                _ttsLogBuf.Append(s.Substring(s.Length / 2));
            }
            TxtTtsLog.Text = _ttsLogBuf.ToString();
            TxtTtsLog.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _pipeline?.Stop();
                _ttsService?.Stop();
            }
            catch { }
            base.OnClosed(e);
        }
    }
}
