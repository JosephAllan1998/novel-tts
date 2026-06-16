using System;

namespace NovelTTS.Models
{
    public enum DownloadStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3
    }

    public enum ParseStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3
    }

    public enum MergeStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3
    }

    public enum AudioStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3
    }

    public enum AudioFormat
    {
        WAV = 0,
        MP3 = 1
    }

    public enum PipelineStage
    {
        Idle = 0,
        Crawling = 1,
        Parsing = 2,
        Merging = 3,
        TTS = 4,
        Done = 5
    }
}
