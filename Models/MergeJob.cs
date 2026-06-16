using System;

namespace NovelTTS.Models
{
    public class MergeJob
    {
        public int JobId { get; set; }
        public int ProjectId { get; set; }
        public int FromChapter { get; set; }
        public int ToChapter { get; set; }
        public string OutputFilePath { get; set; }
        public MergeStatus MergeStatus { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Filename key, e.g. 000001_000010</summary>
        public string FileKey => $"{FromChapter:D6}_{ToChapter:D6}";
    }
}
