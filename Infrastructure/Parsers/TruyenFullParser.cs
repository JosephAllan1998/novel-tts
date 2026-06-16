using HtmlAgilityPack;
using novel_tts.Core.Interfaces;
using novel_tts.Core.Models;
using novel_tts.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace novel_tts.Infrastructure.Parsers
{
    public class TruyenFullParser : IHtmlParser
    {
        private readonly LoggerService _logger;

        public TruyenFullParser(LoggerService logger)
        {
            _logger = logger;
        }

        public async Task<bool> ParseHtmlToTxtAsync(Chapter chapter)
        {
            try
            {
                if (!File.Exists(chapter.HtmlFilePath))
                    throw new FileNotFoundException($"HTML file not found: {chapter.HtmlFilePath}");

                string html = File.ReadAllText(chapter.HtmlFilePath, Encoding.UTF8);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // ID chuẩn của nội dung truyện trên TruyenFull thường là 'chapter-c'
                var contentNode = doc.DocumentNode.SelectSingleNode("//div[@id='chapter-c']");

                if (contentNode == null)
                    throw new Exception("Could not find content node 'chapter-c'");

                // Loại bỏ các thẻ quảng cáo, script hoặc rác nếu có
                var adsNodes = contentNode.SelectNodes(".//div[contains(@class, 'ads')] | .//script | .//style");
                if (adsNodes != null)
                {
                    foreach (var ad in adsNodes) ad.Remove();
                }

                // Format lại text (xuống dòng chuẩn xác)
                string rawText = contentNode.InnerText;
                rawText = System.Net.WebUtility.HtmlDecode(rawText);

                // Xử lý các dòng trống thừa
                var lines = rawText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                StringBuilder cleanText = new StringBuilder();
                cleanText.AppendLine(chapter.Title); // Đưa Tên chương lên đầu
                cleanText.AppendLine();

                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        cleanText.AppendLine(line.Trim());
                    }
                }

                // Đảm bảo thư mục TXT tồn tại
                string directory = Path.GetDirectoryName(chapter.TxtFilePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(chapter.TxtFilePath, cleanText.ToString(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("ParseHtmlToTxtAsync", ex, chapter.HtmlFilePath);
                chapter.LastErrorMessage = ex.Message;
                return false;
            }
        }
    }
}
