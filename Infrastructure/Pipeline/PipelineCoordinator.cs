using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using NovelTTS.Data;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Infrastructure.Http;
using NovelTTS.Models;
using NovelTTS.Services.Crawler;
using NovelTTS.Services.Parser;

namespace NovelTTS.Infrastructure.Pipeline
{
    /// <summary>
    /// Orchestrates the full 3-stage pipeline:
    ///   Thread 1 → ChapterListCrawler  → _downloadQueue (BlockingCollection)
    ///   Thread 2 → HtmlDownloader      → _parseQueue    (BlockingCollection)
    ///   Thread 3 → HtmlParser          → (writes TXT, updates DB)
    /// Supports Start / Pause / Resume / Stop.
    /// </summary>
    public class PipelineCoordinator : IDisposable
    {
        // ─── Queues ────────────────────────────────────────────────────────────
        private BlockingCollection<Chapter> _downloadQueue;
        private BlockingCollection<Chapter> _parseQueue;

        // ─── Threads ───────────────────────────────────────────────────────────
        private Thread _crawlerThread;
        private Thread _downloaderThread;
        private Thread _parserThread;

        // ─── Cancellation ──────────────────────────────────────────────────────
        private CancellationTokenSource _cts;
        private ManualResetEventSlim _pauseEvent;   // Set = running, Reset = paused

        // ─── Dependencies ──────────────────────────────────────────────────────
        private readonly NovelProject _project;
        private readonly DatabaseManager _db;
        private readonly ChapterRepository _chapterRepo;
        private readonly AppLogger _logger;
        private readonly RetryPolicyFactory _retryFactory;
        private readonly HttpClientProvider _httpProvider;

        // ─── Progress callbacks ────────────────────────────────────────────────
        public event Action<string> OnStatusMessage;
        public event Action<int, int> OnCrawlProgress;    // (completed, total)
        public event Action<int, int> OnDownloadProgress;
        public event Action<int, int> OnParseProgress;

        private bool _disposed;

        public PipelineCoordinator(
            NovelProject project,
            DatabaseManager db,
            ChapterRepository chapterRepo,
            AppLogger logger,
            RetryPolicyFactory retryFactory,
            HttpClientProvider httpProvider)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _chapterRepo = chapterRepo ?? throw new ArgumentNullException(nameof(chapterRepo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _retryFactory = retryFactory ?? throw new ArgumentNullException(nameof(retryFactory));
            _httpProvider = httpProvider ?? throw new ArgumentNullException(nameof(httpProvider));
        }

        // ─── Control ───────────────────────────────────────────────────────────

        public void Start()
        {
            try
            {
                _logger.Crawler("PipelineCoordinator.Start", "Pipeline starting",
                    input: $"Project={_project.NovelSlug}");

                _cts = new CancellationTokenSource();
                _pauseEvent = new ManualResetEventSlim(true);  // start in running state

                _downloadQueue = new BlockingCollection<Chapter>(boundedCapacity: 200);
                _parseQueue = new BlockingCollection<Chapter>(boundedCapacity: 200);

                var crawlerSvc = new ChapterListCrawler(
                    _project, _chapterRepo, _httpProvider, _retryFactory, _logger,
                    _downloadQueue, _pauseEvent);

                var downloaderSvc = new HtmlDownloader(
                    _project, _chapterRepo, _httpProvider, _retryFactory, _logger,
                    _downloadQueue, _parseQueue, _pauseEvent);

                var parserSvc = new HtmlParser(
                    _project, _chapterRepo, _logger,
                    _parseQueue, _pauseEvent);

                // Wire progress events
                crawlerSvc.OnProgress += (done, total) =>
                {
                    OnCrawlProgress?.Invoke(done, total);
                    OnStatusMessage?.Invoke($"[Crawl] Tìm được {done}/{total} chương");
                };
                downloaderSvc.OnProgress += (done, total) =>
                {
                    OnDownloadProgress?.Invoke(done, total);
                    OnStatusMessage?.Invoke($"[Download] {done}/{total} chương HTML");
                };
                parserSvc.OnProgress += (done, total) =>
                {
                    OnParseProgress?.Invoke(done, total);
                    OnStatusMessage?.Invoke($"[Parse] {done}/{total} chương TXT");
                };

                _crawlerThread = new Thread(() => crawlerSvc.Run(_cts.Token))
                { Name = "Thread-Crawler", IsBackground = true };
                _downloaderThread = new Thread(() => downloaderSvc.Run(_cts.Token))
                { Name = "Thread-Downloader", IsBackground = true };
                _parserThread = new Thread(() => parserSvc.Run(_cts.Token))
                { Name = "Thread-Parser", IsBackground = true };

                _crawlerThread.Start();
                _downloaderThread.Start();
                _parserThread.Start();

                _logger.Crawler("PipelineCoordinator.Start", "All 3 threads started");
            }
            catch (Exception ex)
            {
                _logger.Error("PipelineCoordinator.Start", ex);
                throw;
            }
        }

        public void Pause()
        {
            try
            {
                _pauseEvent?.Reset();  // threads will block on WaitOne
                _logger.Crawler("PipelineCoordinator.Pause", "Pipeline paused");
                OnStatusMessage?.Invoke("[Pipeline] Đã tạm dừng");
            }
            catch (Exception ex)
            {
                _logger.Error("PipelineCoordinator.Pause", ex);
            }
        }

        public void Resume()
        {
            try
            {
                _pauseEvent?.Set();   // release all blocked threads
                _logger.Crawler("PipelineCoordinator.Resume", "Pipeline resumed");
                OnStatusMessage?.Invoke("[Pipeline] Tiếp tục xử lý...");
            }
            catch (Exception ex)
            {
                _logger.Error("PipelineCoordinator.Resume", ex);
            }
        }

        public void Stop()
        {
            try
            {
                _pauseEvent?.Set();     // unblock first so threads can check CancellationToken
                _cts?.Cancel();
                _logger.Crawler("PipelineCoordinator.Stop", "Cancellation requested");

                // Mark queues as complete so threads exit cleanly
                try { _downloadQueue?.CompleteAdding(); } catch { }
                try { _parseQueue?.CompleteAdding(); } catch { }

                // Wait up to 10s for graceful shutdown
                _crawlerThread?.Join(TimeSpan.FromSeconds(10));
                _downloaderThread?.Join(TimeSpan.FromSeconds(10));
                _parserThread?.Join(TimeSpan.FromSeconds(10));

                _logger.Crawler("PipelineCoordinator.Stop", "All threads stopped");
                OnStatusMessage?.Invoke("[Pipeline] Đã dừng");
            }
            catch (Exception ex)
            {
                _logger.Error("PipelineCoordinator.Stop", ex);
            }
        }

        public bool IsRunning =>
            (_crawlerThread != null && _crawlerThread.IsAlive) ||
            (_downloaderThread != null && _downloaderThread.IsAlive) ||
            (_parserThread != null && _parserThread.IsAlive);

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _cts?.Dispose();
            _downloadQueue?.Dispose();
            _parseQueue?.Dispose();
            _httpProvider?.Dispose();
            _disposed = true;
        }
    }
}
