using HtmlAgilityPack;
using novel_tts.Core.Interfaces;
using novel_tts.Core.Models;
using novel_tts.Infrastructure.Resilience;
using novel_tts.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace novel_tts.Infrastructure.Crawlers
{
    public class TruyenFullCrawler : ICrawlerEngine
    {
        private readonly HttpClient _httpClient;
        private readonly LoggerService _logger;
        private readonly PolicyProvider _policyProvider;

        public TruyenFullCrawler(HttpClient httpClient, LoggerService logger, PolicyProvider policyProvider)
        {
            _httpClient = httpClient;
            _logger = logger;
            _policyProvider = policyProvider;

            // Fake User-Agent để tránh bị block 403
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
        }

        public async Task<int> GetTotalPaginationAsync(string novelUrl, CancellationToken cancellationToken)
        {
            try
            {
                var policy = _policyProvider.GetHttpRetryPolicy();
                var response = await policy.ExecuteAsync(() => _httpClient.GetAsync(novelUrl, cancellationToken));
                response.EnsureSuccessStatusCode();

                string html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Tìm nút "Trang cuối" hoặc các nút phân trang
                var paginationNodes = doc.DocumentNode.SelectNodes("//ul[contains(@class, 'pagination')]/li/a");
                if (paginationNodes == null || !paginationNodes.Any())
                    return 1; // Chỉ có 1 trang

                int maxPage = 1;
                foreach (var node in paginationNodes)
                {
                    string href = node.GetAttributeValue("href", "");
                    // href có dạng: https://truyenfull.today/de-ba/trang-10/
                    if (href.Contains("trang-"))
                    {
                        var parts = href.Split(new[] { "trang-" }, StringSplitOptions.None);
                        if (parts.Length > 1)
                        {
                            var pageStr = parts[1].Trim('/').Split('/')[0]; // Lấy số trang
                            if (int.TryParse(pageStr, out int pageNum) && pageNum > maxPage)
                            {
                                maxPage = pageNum;
                            }
                        }
                    }
                }

                _logger.LogInfo("crawler.log", "GetTotalPaginationAsync", $"Found total pages: {maxPage}", novelUrl);
                return maxPage;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetTotalPaginationAsync", ex, novelUrl);
                throw;
            }
        }

        public async Task<List<Chapter>> ScrapeChapterUrlsAsync(string paginationUrl, string novelId, CancellationToken cancellationToken)
        {
            var chapters = new List<Chapter>();
            try
            {
                var policy = _policyProvider.GetHttpRetryPolicy();
                var response = await policy.ExecuteAsync(() => _httpClient.GetAsync(paginationUrl, cancellationToken));
                response.EnsureSuccessStatusCode();

                string html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // TruyenFull thường chứa list chapter trong class 'list-chapter'
                var chapterNodes = doc.DocumentNode.SelectNodes("//ul[contains(@class, 'list-chapter')]/li/a");
                if (chapterNodes != null)
                {
                    foreach (var node in chapterNodes)
                    {
                        chapters.Add(new Chapter
                        {
                            NovelId = novelId,
                            Title = node.InnerText.Trim(),
                            Url = node.GetAttributeValue("href", "")
                        });
                    }
                }

                _logger.LogInfo("crawler.log", "ScrapeChapterUrlsAsync", $"Scraped {chapters.Count} chapters.", paginationUrl);
                return chapters;
            }
            catch (Exception ex)
            {
                _logger.LogError("ScrapeChapterUrlsAsync", ex, paginationUrl);
                throw;
            }
        }

        public async Task<bool> DownloadChapterHtmlAsync(Chapter chapter, CancellationToken cancellationToken)
        {
            try
            {
                var policy = _policyProvider.GetHttpRetryPolicy();
                var response = await policy.ExecuteAsync(() => _httpClient.GetAsync(chapter.Url, cancellationToken));
                response.EnsureSuccessStatusCode();

                string html = await response.Content.ReadAsStringAsync();

                // Đảm bảo thư mục tồn tại
                string directory = Path.GetDirectoryName(chapter.HtmlFilePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(chapter.HtmlFilePath, html, System.Text.Encoding.UTF8);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("DownloadChapterHtmlAsync", ex, chapter.Url);
                chapter.LastErrorMessage = ex.Message;
                return false;
            }
        }
    }
}
