namespace novel_tts.Core.Enums
{
    public enum JobStatus
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        Paused = 4
    }

    public enum TtsEngineType
    {
        SystemSpeech = 1,
        EdgeTts = 2,
        GoogleTts = 3
    }
}
