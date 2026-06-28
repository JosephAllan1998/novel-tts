using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Dapper;
using NovelTTS.Models;

namespace NovelTTS.Data.Repositories
{
    public class MergeJobRepository
    {
        private readonly DatabaseManager _db;

        public MergeJobRepository(DatabaseManager db)
        {
            _db = db;
        }

        public void Insert(MergeJob job)
        {
            try
            {
                const string sql = @"
INSERT OR IGNORE INTO MergeJob
    (ProjectId, FromChapter, ToChapter, OutputFilePath, MergeStatus, CreatedAt, UpdatedAt)
VALUES
    (@ProjectId, @FromChapter, @ToChapter, @OutputFilePath, @MergeStatus,
     datetime('now'), datetime('now'));";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, job);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[MergeJobRepository.Insert] From={job.FromChapter} To={job.ToChapter} | {ex.Message}", ex);
            }
        }

        public void BulkInsert(IEnumerable<MergeJob> jobs)
        {
            const string sql = @"
INSERT OR IGNORE INTO MergeJob
    (ProjectId, FromChapter, ToChapter, OutputFilePath, MergeStatus, CreatedAt, UpdatedAt)
VALUES
    (@ProjectId, @FromChapter, @ToChapter, @OutputFilePath, @MergeStatus,
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
                throw new InvalidOperationException($"[MergeJobRepository.BulkInsert] {ex.Message}", ex);
            }
        }

        public IEnumerable<MergeJob> GetPending(int projectId)
        {
            try
            {
                const string sql = @"
SELECT * FROM MergeJob
WHERE ProjectId = @ProjectId
  AND MergeStatus IN (0, 3)
ORDER BY FromChapter ASC;";
                using (var conn = _db.GetConnection())
                {
                    return conn.Query<MergeJob>(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[MergeJobRepository.GetPending] {ex.Message}", ex);
            }
        }

        public IEnumerable<MergeJob> GetCompleted(int projectId)
        {
            try
            {
                const string sql = @"
SELECT * FROM MergeJob WHERE ProjectId = @ProjectId AND MergeStatus = 2 ORDER BY FromChapter ASC;";
                using (var conn = _db.GetConnection())
                {
                    return conn.Query<MergeJob>(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[MergeJobRepository.GetCompleted] {ex.Message}", ex);
            }
        }

        public void UpdateStatus(int jobId, MergeStatus status, string outputFilePath = "", string error = "")
        {
            try
            {
                const string sql = @"
UPDATE MergeJob
SET MergeStatus    = @Status,
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
                    $"[MergeJobRepository.UpdateStatus] JobId={jobId} Status={status} | {ex.Message}", ex);
            }
        }

        public void DeleteByProject(int projectId)
        {
            try
            {
                const string sql = "DELETE FROM MergeJob WHERE ProjectId = @ProjectId;";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[MergeJobRepository.DeleteByProject] {ex.Message}", ex);
            }
        }
    }
}
