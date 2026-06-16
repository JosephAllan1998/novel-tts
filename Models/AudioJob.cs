using System;

namespace NovelTTS.Models
{
    public class AudioJob
    {
        public int JobId { get; set; }
        public int ProjectId { get; set; }
        public int MergeJobId { get; set; }
        public string SourceFilePath { get; set; }
        public string OutputFilePath { get; set; }
        public AudioStatus AudioStatus { get; set; }
        public AudioFormat Format { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
