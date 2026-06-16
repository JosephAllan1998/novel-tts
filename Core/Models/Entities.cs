using novel_tts.Core.Enums;
using System;

namespace novel_tts.Core.Models
{
    public class Novel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; }
        public string SourceUrl { get; set; }
        public int TotalChapters { get; set; }
        public string SaveDirectory { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class Chapter
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string NovelId { get; set; }
        public int Index { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string HtmlFilePath { get; set; }
        public string TxtFilePath { get; set; }
        public JobStatus DownloadStatus { get; set; } = JobStatus.Pending;
        public JobStatus ParseStatus { get; set; } = JobStatus.Pending;
        public int RetryCount { get; set; } = 0;
        public string LastErrorMessage { get; set; }
    }

    public class MergeJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string NovelId { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public string OutputTxtPath { get; set; }
        public JobStatus Status { get; set; } = JobStatus.Pending;
    }

    public class AudioJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string MergeJobId { get; set; }
        public string OutputAudioPath { get; set; }
        public TtsEngineType EngineUsed { get; set; }
        public JobStatus Status { get; set; } = JobStatus.Pending;
    }
}
