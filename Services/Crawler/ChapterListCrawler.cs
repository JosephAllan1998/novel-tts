using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Http;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace NovelTTS.Services.Crawler
{
    /// <summary>
    /// Thread 1 – Crawls the chapter list pages and enqueues Chapter objects
    /// into the download queue. Stops when a page returns the same content as
    /// the previous one (end-of-pagination detection).
    /// </summary>
    public class ChapterListCrawler
    {
        private readonly NovelProject _project;
        private readonly ChapterRepository _chapterRepo;
        private readonly HttpClientProvider _httpProvider;
        private readonly RetryPolicyFactory _retryFactory;
        private readonly AppLogger _logger;
        private readonly BlockingCollection<Chapter> _downloadQueue;
        private readonly ManualResetEventSlim _pauseEvent;

        public event Action<int, int> OnProgress;  // (chaptersFound, estimated)

        public ChapterListCrawler(
            NovelProject project,
            ChapterRepository chapterRepo,
            HttpClientProvider httpProvider,
            RetryPolicyFactory retryFactory,
            AppLogger logger,
            BlockingCollection<Chapter> downloadQueue,
            ManualResetEventSlim pauseEvent)
        {
            _project = project;
            _chapterRepo = chapterRepo;
            _httpProvider = httpProvider;
            _retryFactory = retryFactory;
            _logger = logger;
            _downloadQueue = downloadQueue;
            _pauseEvent = pauseEvent;
        }

        public void Run(CancellationToken ct)
        {
            const string method = "ChapterListCrawler.Run";
            _logger.Crawler(method, "Thread started", input: _project.BaseUrl);

            try
            {
                int page = 1;
                string prevHash = string.Empty;
                int totalFound = 0;
                int chapterNumber = _chapterRepo.GetMaxChapterNumber(_project.ProjectId) + 1;
                // Resume: start chapter number after what's already in DB

                while (!ct.IsCancellationRequested)
                {
                    _pauseEvent.Wait(ct);       // honour Pause
                    if (ct.IsCancellationRequested) break;

                    string pageUrl = $"{_project.BaseUrl.TrimEnd('/')}/trang-{page}/#list-chapter";
                    _logger.Crawler(method, $"Fetching page {page}", input: pageUrl);

                    string html = FetchPage(pageUrl, ct);
                    if (html == null)
                    {
                        _logger.Crawler(method, $"Null HTML for page {page} – stopping");
                        break;
                    }

                    string currentHash = ComputeHash(html);
                    if (currentHash == prevHash)
                    {
                        _logger.Crawler(method, $"Page {page} identical to previous – end of list detected");
                        break;
                    }

                    prevHash = currentHash;

                    var urls = ExtractChapterUrls(html);
                    if (urls.Count == 0)
                    {
                        _logger.Crawler(method, $"No chapter URLs found on page {page} – stopping");
                        break;
                    }

                    var newChapters = new List<Chapter>();
                    foreach (string url in urls)
                    {
                        if (_chapterRepo.ExistsByUrl(_project.ProjectId, url))
                            continue;   // already in DB (resume scenario)

                        var chapter = new Chapter
                        {
                            ProjectId = _project.ProjectId,
                            ChapterNumber = chapterNumber++,
                            ChapterTitle = ExtractChapterTitle(url),
                            Url = url,
                            DownloadStatus = DownloadStatus.Pending,
                            ParseStatus = ParseStatus.Pending
                        };
                        newChapters.Add(chapter);
                    }

                    if (newChapters.Count > 0)
                    {
                        _chapterRepo.BulkInsert(newChapters);
                        foreach (var ch in newChapters)
                        {
                            _downloadQueue.Add(ch, ct);
                            totalFound++;
                        }
                        _logger.Crawler(method, $"Page {page}: +{newChapters.Count} chapters, total={totalFound}");
                        OnProgress?.Invoke(totalFound, 0);
                    }

                    page++;
                    _retryFactory.PoliteDelay(800, 2000, ct);
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
                // Signal downloader that no more items will be added
                _downloadQueue.CompleteAdding();
                _logger.Crawler(method, "Thread finished – DownloadQueue marked complete");
            }
        }

        // ─── Private helpers ───────────────────────────────────────────────────

        private string FetchPage(string url, CancellationToken ct)
        {
            const string method = "ChapterListCrawler.FetchPage";
            try
            {
                _httpProvider.RotateUserAgent();
                _httpProvider.SetReferer(_project.BaseUrl);

                var policy = _retryFactory.BuildHttpPolicy((ex, ts, attempt, ctx) =>
                    _logger.Crawler(method, $"Retry {attempt} after {ts.TotalSeconds:F1}s", input: url,
                        output: ex?.Message ?? "bad status"));

                var response = policy.Execute(() =>
                {
                    // Sync over async — acceptable in dedicated thread
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    return _httpProvider.Client.SendAsync(req, ct).GetAwaiter().GetResult();
                });

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Crawler(method, $"Non-success: {(int)response.StatusCode}", input: url);
                    return null;
                }

                var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                string encoding = Encoding.UTF8.GetString(bytes);
                return encoding;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Error(method, ex, input: url);
                return null;
            }
        }

        private List<string> ExtractChapterUrls(string html)
        {
            const string method = "ChapterListCrawler.ExtractChapterUrls";
            var result = new List<string>();
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // CSS selector: #list-chapter a[href*="/chuong-"]
                var nodes = doc.DocumentNode.QuerySelectorAll("#list-chapter a");
                string slug = _project.NovelSlug;

                foreach (var node in nodes)
                {
                    string href = node.GetAttributeValue("href", "");
                    if (!string.IsNullOrWhiteSpace(href)
                        && href.Contains("/chuong-")
                        && href.Contains(slug))
                    {
                        // Normalise to absolute URL
                        if (!href.StartsWith("http"))
                            href = "https://truyenfull.today" + href;

                        if (!result.Contains(href))
                            result.Add(href);
                    }
                }

                _logger.Crawler(method, $"Extracted {result.Count} URLs from page HTML");
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
            }
            return result;
        }

        private string ExtractChapterTitle(string url)
        {
            try
            {
                // e.g. https://truyenfull.today/de-ba/chuong-1/ → "chuong-1"
                var parts = url.TrimEnd('/').Split('/');
                return parts[parts.Length - 1];
            }
            catch
            {
                return url;
            }
        }

        private string ComputeHash(string text)
        {
            try
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
                    byte[] hash = md5.ComputeHash(bytes);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return text.GetHashCode().ToString();
            }
        }
    }
}
