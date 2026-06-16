using novel_tts.Core.Enums;
using novel_tts.Core.Interfaces;
using novel_tts.Core.Models;
using novel_tts.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace novel_tts.Applications.Services
{
    public class MergePipelineManager
    {
        private readonly INovelRepository _repository;
        private readonly LoggerService _logger;

        public MergePipelineManager(INovelRepository repository, LoggerService logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<List<MergeJob>> CreateMergeJobsAsync(string novelId, string novelSaveDirectory, int mergeSize = 10)
        {
            var mergeJobs = new List<MergeJob>();
            string mergeDir = Path.Combine(novelSaveDirectory, "Merge");

            try
            {
                if (!Directory.Exists(mergeDir)) Directory.CreateDirectory(mergeDir);

                // Lấy các chương đã bóc tách TXT thành công
                var chapters = await _repository.GetPendingChaptersAsync(novelId);
                var completedChapters = chapters.Where(c => c.ParseStatus == JobStatus.Completed).OrderBy(c => c.Index).ToList();

                if (!completedChapters.Any()) return mergeJobs;

                // Chia lô (Batching)
                for (int i = 0; i < completedChapters.Count; i += mergeSize)
                {
                    var batch = completedChapters.Skip(i).Take(mergeSize).ToList();
                    int startIdx = batch.First().Index;
                    int endIdx = batch.Last().Index;

                    // Tên file dạng: 000001_000010.txt
                    string fileName = $"{startIdx:D6}_{endIdx:D6}.txt";
                    string outputPath = Path.Combine(mergeDir, fileName);

                    // Nối nội dung các file TXT
                    StringBuilder sbMerge = new StringBuilder();
                    foreach (var chapter in batch)
                    {
                        if (File.Exists(chapter.TxtFilePath))
                        {
                            string content = File.ReadAllText(chapter.TxtFilePath, Encoding.UTF8);
                            sbMerge.AppendLine(content);
                            sbMerge.AppendLine("\n***\n"); // Dấu phân cách giữa các chương
                        }
                    }

                    File.WriteAllText(outputPath, sbMerge.ToString(), Encoding.UTF8);

                    var mergeJob = new MergeJob
                    {
                        NovelId = novelId,
                        StartIndex = startIdx,
                        EndIndex = endIdx,
                        OutputTxtPath = outputPath,
                        Status = JobStatus.Pending
                    };

                    // TODO: Gọi _repository.SaveMergeJobAsync(mergeJob) để lưu vào SQLite
                    mergeJobs.Add(mergeJob);

                    _logger.LogInfo("merge.log", "CreateMergeJobsAsync", $"Merged chapters {startIdx} to {endIdx}.", fileName);
                }

                return mergeJobs;
            }
            catch (Exception ex)
            {
                _logger.LogError("CreateMergeJobsAsync", ex, $"NovelId: {novelId}");
                throw;
            }
        }
    }
}
