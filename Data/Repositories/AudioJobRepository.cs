using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Dapper;
using NovelTTS.Models;

namespace NovelTTS.Data.Repositories
{
    public class AudioJobRepository
    {
        private readonly DatabaseManager _db;

        public AudioJobRepository(DatabaseManager db)
        {
            _db = db;
        }

        public void Insert(AudioJob job)
        {
            try
            {
                const string sql = @"
INSERT OR IGNORE INTO AudioJob
    (ProjectId, MergeJobId, SourceFilePath, OutputFilePath, AudioStatus, Format, ErrorMessage, CreatedAt, UpdatedAt)
VALUES
    (@ProjectId, @MergeJobId, @SourceFilePath, @OutputFilePath, @AudioStatus, @Format, @ErrorMessage,
     datetime('now'), datetime('now'));";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, job);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[AudioJobRepository.Insert] MergeJobId={job.MergeJobId} | {ex.Message}", ex);
            }
        }

        public void BulkInsert(IEnumerable<AudioJob> jobs)
        {
            const string sql = @"
INSERT OR IGNORE INTO AudioJob
    (ProjectId, MergeJobId, SourceFilePath, OutputFilePath, AudioStatus, Format, ErrorMessage, CreatedAt, UpdatedAt)
VALUES
    (@ProjectId, @MergeJobId, @SourceFilePath, @OutputFilePath, @AudioStatus, @Format, @ErrorMessage,
     datetime('now'), datetime('now'));";
            try
            {
                using (var conn = _db.GetConnection())
                using (var tx = conn.BeginTransaction())
                {
                    conn.Execute(sql, jobs, tx);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[AudioJobRepository.BulkInsert] {ex.Message}", ex);
            }
        }

        public IEnumerable<AudioJob> GetPending(int projectId)
        {
            try
            {
                const string sql = @"
SELECT * FROM AudioJob
WHERE ProjectId = @ProjectId
  AND AudioStatus IN (0, 3)
ORDER BY JobId ASC;";
                using (var conn = _db.GetConnection())
                {
                    return conn.Query<AudioJob>(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[AudioJobRepository.GetPending] {ex.Message}", ex);
            }
        }

        public void UpdateStatus(int jobId, AudioStatus status, string outputFilePath = "", string error = "")
        {
            try
            {
                const string sql = @"
UPDATE AudioJob
SET AudioStatus    = @Status,
    OutputFilePath = @OutputFilePath,
    ErrorMessage   = @Error,
    UpdatedAt      = datetime('now')
WHERE JobId = @JobId;";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, new
                    {
                        Status         = (int)status,
                        OutputFilePath = outputFilePath ?? "",
                        Error          = error ?? "",
                        JobId          = jobId
                    });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[AudioJobRepository.UpdateStatus] JobId={jobId} Status={status} | {ex.Message}", ex);
            }
        }

        public int CountByStatus(int projectId, AudioStatus status)
        {
            try
            {
                const string sql = "SELECT COUNT(*) FROM AudioJob WHERE ProjectId=@ProjectId AND AudioStatus=@Status;";
                using (var conn = _db.GetConnection())
                {
                    return conn.ExecuteScalar<int>(sql, new { ProjectId = projectId, Status = (int)status });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[AudioJobRepository.CountByStatus] {ex.Message}", ex);
            }
        }

        public void DeleteByProject(int projectId)
        {
            try
            {
                const string sql = "DELETE FROM AudioJob WHERE ProjectId = @ProjectId;";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[AudioJobRepository.DeleteByProject] {ex.Message}", ex);
            }
        }
    }
}
