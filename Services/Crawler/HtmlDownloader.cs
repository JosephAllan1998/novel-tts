using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Http;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Models;

namespace NovelTTS.Services.Crawler
{
    /// <summary>
    /// Thread 2 – Dequeues Chapter items, downloads HTML, saves to disk,
    /// updates DB DownloadStatus, and enqueues into the parseQueue.
    /// </summary>
    public class HtmlDownloader
    {
        private readonly NovelProject                _project;
        private readonly ChapterRepository           _chapterRepo;
        private readonly HttpClientProvider          _httpProvider;
        private readonly RetryPolicyFactory          _retryFactory;
        private readonly AppLogger                   _logger;
        private readonly BlockingCollection<Chapter> _downloadQueue;
        private readonly BlockingCollection<Chapter> _parseQueue;
        private readonly ManualResetEventSlim        _pauseEvent;

        public event Action<int, int> OnProgress;  // (downloaded, total)
        private int _downloadedCount = 0;

        public HtmlDownloader(
            NovelProject project,
            ChapterRepository chapterRepo,
            HttpClientProvider httpProvider,
            RetryPolicyFactory retryFactory,
            AppLogger logger,
            BlockingCollection<Chapter> downloadQueue,
            BlockingCollection<Chapter> parseQueue,
            ManualResetEventSlim pauseEvent)
        {
            _project       = project;
            _chapterRepo   = chapterRepo;
            _httpProvider  = httpProvider;
            _retryFactory  = retryFactory;
            _logger        = logger;
            _downloadQueue = downloadQueue;
            _parseQueue    = parseQueue;
            _pauseEvent    = pauseEvent;
        }

        public void Run(CancellationToken ct)
        {
            const string method = "HtmlDownloader.Run";
            _logger.Crawler(method, "Thread started");

            try
            {
                // First, load resume candidates directly from DB (already Pending/Failed)
                // These may have been missed if app was restarted
                var resumeChapters = _chapterRepo.GetPendingDownloads(_project.ProjectId);
                foreach (var ch in resumeChapters)
                {
                    if (ct.IsCancellationRequested) break;
                    // Only add if not already in the queue (from crawler thread)
                    // We use a local set in memory; re-crawl handles the rest
                    // In resume mode the crawler thread may not run, so seed queue here
                }

                foreach (var chapter in _downloadQueue.GetConsumingEnumerable(ct))
                {
                    _pauseEvent.Wait(ct);
                    if (ct.IsCancellationRequested) break;

                    ProcessChapter(chapter, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Crawler(method, "Cancelled via CancellationToken");
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
            }
            finally
            {
                _parseQueue.CompleteAdding();
                _logger.Crawler(method, "Thread finished – ParseQueue marked complete");
            }
        }

        private void ProcessChapter(Chapter chapter, CancellationToken ct)
        {
            const string method = "HtmlDownloader.ProcessChapter";
            string htmlPath = Path.Combine(_project.HtmlDir, $"{chapter.PaddedNumber}.html");

            try
            {
                _logger.Crawler(method, $"Downloading chapter {chapter.ChapterNumber}",
                    input: chapter.Url, output: htmlPath);

                // Skip if already downloaded
                if (File.Exists(htmlPath) && new FileInfo(htmlPath).Length > 500)
                {
                    _logger.Crawler(method, $"Chapter {chapter.ChapterNumber} already on disk – skip download");
                    chapter.HtmlFilePath    = htmlPath;
                    chapter.DownloadStatus  = DownloadStatus.Completed;
                    _chapterRepo.UpdateDownloadStatus(chapter.ChapterId, DownloadStatus.Completed, htmlPath);
                    _parseQueue.Add(chapter, ct);
                    return;
                }

                // Mark as in-progress
                _chapterRepo.UpdateDownloadStatus(chapter.ChapterId, DownloadStatus.InProgress);

                _httpProvider.RotateUserAgent();
                _httpProvider.SetReferer(_project.BaseUrl);

                var policy = _retryFactory.BuildHttpPolicy((ex, ts, attempt, ctx) =>
                    _logger.Crawler(method,
                        $"Retry {attempt} for chapter {chapter.ChapterNumber} after {ts.TotalSeconds:F1}s",
                        input: chapter.Url, output: ex?.Message ?? "bad status"));

                var response = policy.Execute(() =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, chapter.Url);
                    return _httpProvider.Client.SendAsync(req, ct).GetAwaiter().GetResult();
                });

                if (!response.IsSuccessStatusCode)
                {
                    string errMsg = $"HTTP {(int)response.StatusCode}";
                    _logger.Crawler(method, $"Failed chapter {chapter.ChapterNumber}: {errMsg}", input: chapter.Url);
                    _chapterRepo.UpdateDownloadStatus(chapter.ChapterId, DownloadStatus.Failed, error: errMsg);
                    return;
                }

                string html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                // Ensure directory exists
                string dir = Path.GetDirectoryName(htmlPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(htmlPath, html, Encoding.UTF8);

                chapter.HtmlFilePath   = htmlPath;
                chapter.DownloadStatus = DownloadStatus.Completed;
                _chapterRepo.UpdateDownloadStatus(chapter.ChapterId, DownloadStatus.Completed, htmlPath);

                int count = Interlocked.Increment(ref _downloadedCount);
                _logger.Crawler(method, $"Chapter {chapter.ChapterNumber} downloaded OK",
                    input: chapter.Url, output: htmlPath);
                OnProgress?.Invoke(count, 0);

                _parseQueue.Add(chapter, ct);

                _retryFactory.PoliteDelay(500, 1500, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Error(method, ex, input: chapter.Url);
                try
                {
                    _chapterRepo.UpdateDownloadStatus(chapter.ChapterId, DownloadStatus.Failed,
                        error: ex.Message);
                }
                catch { }
            }
        }
    }
}
