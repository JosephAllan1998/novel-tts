using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Ookii.Dialogs.Wpf;
using NovelTTS.Commands;
using NovelTTS.Data;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Http;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Infrastructure.Pipeline;
using NovelTTS.Models;
using NovelTTS.Services.Project;
using NovelTTS.ViewModels.Base;

namespace NovelTTS.ViewModels
{
    public class CrawlerViewModel : ViewModelBase
    {
        // ─── Fields ────────────────────────────────────────────────────────────
        private readonly Dispatcher _uiDispatcher;
        private readonly ProjectService _projectService;

        private NovelProject _currentProject;
        private PipelineCoordinator _pipeline;
        private AppLogger _logger;
        private ChapterRepository _chapterRepo;
        private DatabaseManager _dbManager;

        // ─── Bound properties ──────────────────────────────────────────────────
        private string _novelUrl = "https://truyenfull.today/de-ba/";
        private string _workDir = "";
        private int _chaptersPerMerge = 10;
        private int _retryCount = 4;
        private int _minDelay = 800;
        private int _maxDelay = 2500;
        private string _logText = "";
        private string _statusText = "Sẵn sàng";
        private int _crawlProgress = 0;
        private int _downloadProgress = 0;
        private int _parseProgress = 0;
        private int _crawlTotal = 100;
        private bool _isPipelinesRunning = false;

        public string NovelUrl { get => _novelUrl; set => SetField(ref _novelUrl, value); }
        public string WorkDir { get => _workDir; set => SetField(ref _workDir, value); }
        public int ChaptersPerMerge { get => _chaptersPerMerge; set => SetField(ref _chaptersPerMerge, value); }
        public int RetryCount { get => _retryCount; set => SetField(ref _retryCount, value); }
        public int MinDelay { get => _minDelay; set => SetField(ref _minDelay, value); }
        public int MaxDelay { get => _maxDelay; set => SetField(ref _maxDelay, value); }
        public string LogText { get => _logText; set => SetField(ref _logText, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
        public int CrawlProgress { get => _crawlProgress; set => SetField(ref _crawlProgress, value); }
        public int DownloadProgress { get => _downloadProgress; set => SetField(ref _downloadProgress, value); }
        public int ParseProgress { get => _parseProgress; set => SetField(ref _parseProgress, value); }
        public int CrawlTotal { get => _crawlTotal; set => SetField(ref _crawlTotal, value); }
        public bool IsPipelineRunning { get => _isPipelinesRunning; set { SetField(ref _isPipelinesRunning, value); RaiseAllCommands(); } }

        public NovelProject CurrentProject { get => _currentProject; set => SetField(ref _currentProject, value); }

        // ─── Commands ──────────────────────────────────────────────────────────
        public RelayCommand BrowseWorkDirCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand PauseCommand { get; }
        public RelayCommand ResumeCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand ClearLogCommand { get; }

        public CrawlerViewModel(Dispatcher dispatcher)
        {
            _uiDispatcher = dispatcher;
            _projectService = new ProjectService(new AppLogger(GetTempLogDir()));

            BrowseWorkDirCommand = new RelayCommand(BrowseWorkDir);
            //StartCommand = new RelayCommand(StartPipeline, () => !IsPipelineRunning && !string.IsNullOrWhiteSpace(NovelUrl) && !string.IsNullOrWhiteSpace(WorkDir));
            //PauseCommand = new RelayCommand(PausePipeline, () => IsPipelineRunning);
            //ResumeCommand = new RelayCommand(ResumePipeline, () => IsPipelineRunning);
            //StopCommand = new RelayCommand(StopPipeline, () => IsPipelineRunning);
            StartCommand = new RelayCommand(StartPipeline);
            PauseCommand = new RelayCommand(PausePipeline);
            ResumeCommand = new RelayCommand(ResumePipeline);
            StopCommand = new RelayCommand(StopPipeline);
            ClearLogCommand = new RelayCommand(_ => LogText = "");

            // Default WorkDir
            WorkDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NovelTTS");
        }

        // ─── Command handlers ──────────────────────────────────────────────────

        private void BrowseWorkDir(object _)
        {
            try
            {
                var dlg = new VistaFolderBrowserDialog
                {
                    Description = "Chọn thư mục lưu dự án",
                    UseDescriptionForTitle = true
                };
                if (dlg.ShowDialog() == true)
                    WorkDir = dlg.SelectedPath;
            }
            catch (Exception ex) { AppendLog($"[ERROR] BrowseWorkDir: {ex.Message}"); }
        }

        private void StartPipeline(object _)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NovelUrl))
                { MessageBox.Show("Vui lòng nhập URL truyện.", "Thiếu thông tin"); return; }
                if (string.IsNullOrWhiteSpace(WorkDir))
                { MessageBox.Show("Vui lòng chọn thư mục lưu.", "Thiếu thông tin"); return; }

                // Reset progress
                CrawlProgress = DownloadProgress = ParseProgress = 0;
                CrawlTotal = 100;
                AppendLog("═══════════════════════════════════════");
                AppendLog($"[START] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                AppendLog($"URL: {NovelUrl}");
                AppendLog($"WorkDir: {WorkDir}");

                // Create or open project
                _currentProject = _projectService.CreateProject(NovelUrl, WorkDir, ChaptersPerMerge);
                _dbManager = _projectService.GetDatabaseManager(_currentProject);
                _chapterRepo = new ChapterRepository(_dbManager);
                _logger = new AppLogger(_currentProject.LogsDir);

                // Build pipeline
                var httpProvider = new HttpClientProvider(30);
                var retryFactory = new RetryPolicyFactory(RetryCount, MinDelay);

                _pipeline = new PipelineCoordinator(
                    _currentProject, _dbManager, _chapterRepo, _logger, retryFactory, httpProvider);

                _pipeline.OnStatusMessage += msg => UiInvoke(() => AppendLog(msg));
                _pipeline.OnCrawlProgress += (d, t) => UiInvoke(() => { CrawlProgress = d; if (t > 0) CrawlTotal = t; });
                _pipeline.OnDownloadProgress += (d, t) => UiInvoke(() => DownloadProgress = d);
                _pipeline.OnParseProgress += (d, t) => UiInvoke(() => ParseProgress = d);

                _pipeline.Start();
                IsPipelineRunning = true;
                StatusText = "Đang crawl...";

                AppendLog("Pipeline đã khởi động (3 threads).");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] StartPipeline: {ex.Message}");
                IsPipelineRunning = false;
            }
        }

        private void PausePipeline(object _)
        {
            try
            {
                _pipeline?.Pause();
                StatusText = "Đã tạm dừng";
                AppendLog("[PAUSE] Pipeline tạm dừng");
            }
            catch (Exception ex) { AppendLog($"[ERROR] Pause: {ex.Message}"); }
        }

        private void ResumePipeline(object _)
        {
            try
            {
                _pipeline?.Resume();
                StatusText = "Đang crawl...";
                AppendLog("[RESUME] Pipeline tiếp tục");
            }
            catch (Exception ex) { AppendLog($"[ERROR] Resume: {ex.Message}"); }
        }

        private void StopPipeline(object _)
        {
            try
            {
                _pipeline?.Stop();
                IsPipelineRunning = false;
                StatusText = "Đã dừng";
                AppendLog("[STOP] Pipeline đã dừng");
            }
            catch (Exception ex) { AppendLog($"[ERROR] Stop: {ex.Message}"); }
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

        private readonly StringBuilder _logBuffer = new StringBuilder();

        private void AppendLog(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logBuffer.AppendLine(line);
            // Keep last 500 lines for performance
            if (_logBuffer.Length > 80000)
            {
                string full = _logBuffer.ToString();
                int cutAt = full.IndexOf('\n', full.Length - 40000);
                _logBuffer.Clear();
                if (cutAt >= 0) _logBuffer.Append(full.Substring(cutAt + 1));
            }
            LogText = _logBuffer.ToString();
        }

        private void UiInvoke(Action action)
        {
            try { _uiDispatcher.Invoke(action); }
            catch { }
        }

        private void RaiseAllCommands()
        {
            StartCommand.RaiseCanExecuteChanged();
            PauseCommand.RaiseCanExecuteChanged();
            ResumeCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }

        private string GetTempLogDir() =>
            Path.Combine(Path.GetTempPath(), "NovelTTS", "logs");
    }
}
