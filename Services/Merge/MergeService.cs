using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Models;

namespace NovelTTS.Services.Merge
{
    /// <summary>
    /// Reads completed TXT chapters from disk, groups them by ChaptersPerMerge,
    /// concatenates into a single larger file, and persists MergeJob records.
    /// Supports re-run (skips already completed jobs).
    /// </summary>
    public class MergeService
    {
        private readonly NovelProject _project;
        private readonly ChapterRepository _chapterRepo;
        private readonly MergeJobRepository _mergeRepo;
        private readonly AppLogger _logger;

        public event Action<int, int> OnProgress;    // (done, total)
        public event Action<string> OnStatusMessage;

        public MergeService(
            NovelProject project,
            ChapterRepository chapterRepo,
            MergeJobRepository mergeRepo,
            AppLogger logger)
        {
            _project = project;
            _chapterRepo = chapterRepo;
            _mergeRepo = mergeRepo;
            _logger = logger;
        }

        /// <summary>
        /// Rebuilds all MergeJobs for the project using the given chaptersPerMerge value.
        /// Deletes existing pending/failed jobs and regenerates.
        /// </summary>
        public void PlanMergeJobs(int chaptersPerMerge, CancellationToken ct)
        {
            const string method = "MergeService.PlanMergeJobs";
            try
            {
                _logger.Merge(method, "Planning merge jobs", input: $"ChaptersPerMerge={chaptersPerMerge}");

                var chapters = _chapterRepo.GetAllByProject(_project.ProjectId)
                    .Where(c => c.ParseStatus == ParseStatus.Completed)
                    .OrderBy(c => c.ChapterNumber)
                    .ToList();

                if (chapters.Count == 0)
                {
                    _logger.Merge(method, "No completed chapters found");
                    OnStatusMessage?.Invoke("Không có chương nào đã parse xong.");
                    return;
                }

                // Delete existing pending/failed merge jobs so we can re-plan
                _mergeRepo.DeleteByProject(_project.ProjectId);

                var jobs = new List<MergeJob>();
                for (int i = 0; i < chapters.Count; i += chaptersPerMerge)
                {
                    if (ct.IsCancellationRequested) break;

                    var batch = chapters.Skip(i).Take(chaptersPerMerge).ToList();
                    int from = batch.First().ChapterNumber;
                    int to = batch.Last().ChapterNumber;
                    string file = Path.Combine(_project.MergeDir, $"{from:D6}_{to:D6}.txt");

                    jobs.Add(new MergeJob
                    {
                        ProjectId = _project.ProjectId,
                        FromChapter = from,
                        ToChapter = to,
                        OutputFilePath = file,
                        MergeStatus = MergeStatus.Pending
                    });
                }

                _mergeRepo.BulkInsert(jobs);
                _logger.Merge(method, $"Planned {jobs.Count} merge jobs", output: $"Jobs={jobs.Count}");
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
                throw;
            }
        }

        /// <summary>
        /// Executes all pending MergeJobs. Can be called on any thread.
        /// </summary>
        public void ExecuteMergeJobs(CancellationToken ct)
        {
            const string method = "MergeService.ExecuteMergeJobs";
            try
            {
                var jobs = _mergeRepo.GetPending(_project.ProjectId).ToList();
                int total = jobs.Count;
                int done = 0;

                _logger.Merge(method, $"Executing {total} merge jobs");
                OnStatusMessage?.Invoke($"Bắt đầu merge {total} nhóm chương...");

                foreach (var job in jobs)
                {
                    if (ct.IsCancellationRequested) break;

                    ExecuteJob(job, ct);
                    done++;
                    OnProgress?.Invoke(done, total);
                    OnStatusMessage?.Invoke($"Merge {done}/{total}: {Path.GetFileName(job.OutputFilePath)}");
                }

                _logger.Merge(method, $"Merge complete: {done}/{total} jobs processed");
            }
            catch (OperationCanceledException)
            {
                _logger.Merge(method, "Merge cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
                throw;
            }
        }

        private void ExecuteJob(MergeJob job, CancellationToken ct)
        {
            const string method = "MergeService.ExecuteJob";
            try
            {
                _logger.Merge(method,
                    $"Merging chapters {job.FromChapter}–{job.ToChapter}",
                    output: job.OutputFilePath);

                _mergeRepo.UpdateStatus(job.JobId, MergeStatus.InProgress);

                // Get chapters in this range
                var chapters = _chapterRepo.GetAllByProject(_project.ProjectId)
                    .Where(c => c.ChapterNumber >= job.FromChapter
                             && c.ChapterNumber <= job.ToChapter
                             && c.ParseStatus == ParseStatus.Completed)
                    .OrderBy(c => c.ChapterNumber)
                    .ToList();

                if (chapters.Count == 0)
                {
                    _mergeRepo.UpdateStatus(job.JobId, MergeStatus.Failed, error: "No parsed chapters in range");
                    return;
                }

                string dir = Path.GetDirectoryName(job.OutputFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                foreach (var ch in chapters)
                {
                    if (ct.IsCancellationRequested) break;

                    string txtPath = ch.TxtFilePath;
                    if (string.IsNullOrEmpty(txtPath) || !File.Exists(txtPath))
                    {
                        // Try to resolve from convention
                        txtPath = Path.Combine(_project.TxtDir, $"{ch.PaddedNumber}.txt");
                    }

                    if (!File.Exists(txtPath))
                    {
                        _logger.Merge(method, $"TXT not found for chapter {ch.ChapterNumber}: {txtPath}");
                        continue;
                    }

                    string content = File.ReadAllText(txtPath, Encoding.UTF8);
                    sb.Append(content);
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine(new string('─', 60));
                    sb.AppendLine();
                }

                File.WriteAllText(job.OutputFilePath, sb.ToString(), Encoding.UTF8);
                _mergeRepo.UpdateStatus(job.JobId, MergeStatus.Completed, job.OutputFilePath);

                _logger.Merge(method, $"Merge done: {chapters.Count} chapters → {job.OutputFilePath}",
                    output: $"{new FileInfo(job.OutputFilePath).Length / 1024} KB");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Error(method, ex);
                try { _mergeRepo.UpdateStatus(job.JobId, MergeStatus.Failed, error: ex.Message); } catch { }
            }
        }
    }
}
