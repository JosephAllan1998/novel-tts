using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using HtmlAgilityPack;
using Fizzler.Systems.HtmlAgilityPack;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Models;

namespace NovelTTS.Services.Parser
{
    /// <summary>
    /// Thread 3 – Dequeues Chapter items, parses HTML content via CSS selector,
    /// strips ads/scripts, saves clean text to Txt/, updates DB ParseStatus.
    /// </summary>
    public class HtmlParser
    {
        private readonly NovelProject                _project;
        private readonly ChapterRepository           _chapterRepo;
        private readonly AppLogger                   _logger;
        private readonly BlockingCollection<Chapter> _parseQueue;
        private readonly ManualResetEventSlim        _pauseEvent;

        public event Action<int, int> OnProgress;
        private int _parsedCount = 0;

        // CSS selector for chapter content on truyenfull.today
        private const string ContentSelector = "div#chapter-c";

        public HtmlParser(
            NovelProject project,
            ChapterRepository chapterRepo,
            AppLogger logger,
            BlockingCollection<Chapter> parseQueue,
            ManualResetEventSlim pauseEvent)
        {
            _project     = project;
            _chapterRepo = chapterRepo;
            _logger      = logger;
            _parseQueue  = parseQueue;
            _pauseEvent  = pauseEvent;
        }

        public void Run(CancellationToken ct)
        {
            const string method = "HtmlParser.Run";
            _logger.Parser(method, "Thread started");

            try
            {
                foreach (var chapter in _parseQueue.GetConsumingEnumerable(ct))
                {
                    _pauseEvent.Wait(ct);
                    if (ct.IsCancellationRequested) break;

                    ProcessChapter(chapter, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Parser(method, "Cancelled via CancellationToken");
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
            }
            finally
            {
                _logger.Parser(method, "Thread finished");
            }
        }

        private void ProcessChapter(Chapter chapter, CancellationToken ct)
        {
            const string method = "HtmlParser.ProcessChapter";
            string txtPath = Path.Combine(_project.TxtDir, $"{chapter.PaddedNumber}.txt");

            try
            {
                _logger.Parser(method, $"Parsing chapter {chapter.ChapterNumber}",
                    input: chapter.HtmlFilePath, output: txtPath);

                // Skip if already parsed
                if (File.Exists(txtPath) && new FileInfo(txtPath).Length > 10)
                {
                    _logger.Parser(method, $"Chapter {chapter.ChapterNumber} TXT already exists – skip");
                    _chapterRepo.UpdateParseStatus(chapter.ChapterId, ParseStatus.Completed, txtPath,
                        projectId: chapter.ProjectId, url: chapter.Url);
                    return;
                }

                if (string.IsNullOrEmpty(chapter.HtmlFilePath) || !File.Exists(chapter.HtmlFilePath))
                {
                    _logger.Parser(method, $"HTML file not found: {chapter.HtmlFilePath}");
                    _chapterRepo.UpdateParseStatus(chapter.ChapterId, ParseStatus.Failed,
                        error: "HTML file not found",
                        projectId: chapter.ProjectId, url: chapter.Url);
                    return;
                }

                _chapterRepo.UpdateParseStatus(chapter.ChapterId, ParseStatus.InProgress,
                    projectId: chapter.ProjectId, url: chapter.Url);

                string html = File.ReadAllText(chapter.HtmlFilePath, Encoding.UTF8);
                string text = ExtractContent(html, chapter);

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.Parser(method, $"No content extracted for chapter {chapter.ChapterNumber}");
                    _chapterRepo.UpdateParseStatus(chapter.ChapterId, ParseStatus.Failed,
                        error: "No content extracted",
                        projectId: chapter.ProjectId, url: chapter.Url);
                    return;
                }

                // Ensure directory exists
                string dir = Path.GetDirectoryName(txtPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(txtPath, text, Encoding.UTF8);

                _chapterRepo.UpdateParseStatus(chapter.ChapterId, ParseStatus.Completed, txtPath,
                    projectId: chapter.ProjectId, url: chapter.Url);

                int count = Interlocked.Increment(ref _parsedCount);
                _logger.Parser(method, $"Chapter {chapter.ChapterNumber} parsed OK – {text.Length} chars",
                    output: txtPath);
                OnProgress?.Invoke(count, 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Error(method, ex, input: chapter.HtmlFilePath ?? "");
                try
                {
                    _chapterRepo.UpdateParseStatus(chapter.ChapterId, ParseStatus.Failed, error: ex.Message,
                        projectId: chapter.ProjectId, url: chapter.Url);
                }
                catch { }
            }
        }

        private string ExtractContent(string html, Chapter chapter)
        {
            const string method = "HtmlParser.ExtractContent";
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Remove script, style, iframe nodes
                RemoveNodes(doc, "//script");
                RemoveNodes(doc, "//style");
                RemoveNodes(doc, "//iframe");
                RemoveNodes(doc, "//ins");    // ads
                RemoveNodes(doc, "//*[@class='ads']");

                // Primary: use CSS selector
                var contentNode = doc.DocumentNode.QuerySelector(ContentSelector);
                if (contentNode == null)
                {
                    // Fallback selectors
                    contentNode = doc.DocumentNode.QuerySelector("div.chapter-content")
                               ?? doc.DocumentNode.QuerySelector("div#chapter-content")
                               ?? doc.DocumentNode.QuerySelector("div.box-chap");
                }

                if (contentNode == null)
                {
                    _logger.Parser(method, $"Content node not found for chapter {chapter.ChapterNumber}");
                    return string.Empty;
                }

                // Build clean text
                var sb = new StringBuilder();
                sb.AppendLine($"=== {chapter.ChapterTitle} ===");
                sb.AppendLine();

                // Get inner text preserving line breaks from <p> and <br>
                foreach (var node in contentNode.DescendantsAndSelf())
                {
                    if (node.NodeType == HtmlNodeType.Text)
                    {
                        string text = HtmlEntity.DeEntitize(node.InnerText).Trim();
                        if (!string.IsNullOrEmpty(text))
                            sb.AppendLine(text);
                    }
                    else if (node.Name == "p" || node.Name == "br")
                    {
                        sb.AppendLine();
                    }
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
                return string.Empty;
            }
        }

        private void RemoveNodes(HtmlDocument doc, string xPath)
        {
            try
            {
                var nodes = doc.DocumentNode.SelectNodes(xPath);
                if (nodes == null) return;
                foreach (var node in nodes)
                    node.Remove();
            }
            catch { }
        }
    }
}
