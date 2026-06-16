using System;

namespace NovelTTS.Models
{
    public class NovelProject
    {
        public int ProjectId { get; set; }
        public string NovelSlug { get; set; }
        public string NovelTitle { get; set; }
        public string BaseUrl { get; set; }
        public int TotalChapters { get; set; }
        public string ProjectPath { get; set; }
        public int ChaptersPerMerge { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string HtmlDir => System.IO.Path.Combine(ProjectPath, "Html");
        public string TxtDir  => System.IO.Path.Combine(ProjectPath, "Txt");
        public string MergeDir => System.IO.Path.Combine(ProjectPath, "Merge");
        public string AudioDir => System.IO.Path.Combine(ProjectPath, "Audio");
        public string LogsDir  => System.IO.Path.Combine(ProjectPath, "Logs");
        public string MetaDir  => System.IO.Path.Combine(ProjectPath, "Metadata");
        public string DbPath   => System.IO.Path.Combine(ProjectPath, "Metadata", "novel.db");
    }
}
