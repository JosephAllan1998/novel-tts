using System;

namespace NovelTTS.Models
{
    public class Chapter
    {
        public int ChapterId { get; set; }
        public int ProjectId { get; set; }
        public int ChapterNumber { get; set; }
        public string ChapterTitle { get; set; }
        public string Url { get; set; }

        public DownloadStatus DownloadStatus { get; set; }
        public ParseStatus ParseStatus { get; set; }

        public string HtmlFilePath { get; set; }
        public string TxtFilePath { get; set; }
        public string DownloadError { get; set; }
        public string ParseError { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Formatted zero-padded chapter number, e.g. 000001</summary>
        public string PaddedNumber => ChapterNumber.ToString("D6");
    }
}
