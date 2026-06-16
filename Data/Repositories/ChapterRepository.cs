using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Dapper;
using NovelTTS.Models;

namespace NovelTTS.Data.Repositories
{
    public class ChapterRepository
    {
        private readonly DatabaseManager _db;

        public ChapterRepository(DatabaseManager db)
        {
            _db = db;
        }

        public void Insert(Chapter chapter)
        {
            try
            {
                const string sql = @"
INSERT OR IGNORE INTO Chapter
    (ProjectId, ChapterNumber, ChapterTitle, Url, DownloadStatus, ParseStatus,
     HtmlFilePath, TxtFilePath, DownloadError, ParseError, CreatedAt, UpdatedAt)
VALUES
    (@ProjectId, @ChapterNumber, @ChapterTitle, @Url, @DownloadStatus, @ParseStatus,
     @HtmlFilePath, @TxtFilePath, @DownloadError, @ParseError,
     datetime('now'), datetime('now'));";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, chapter);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[ChapterRepository.Insert] ProjectId={chapter.ProjectId} ChapterNumber={chapter.ChapterNumber} | {ex.Message}", ex);
            }
        }

        public void BulkInsert(IEnumerable<Chapter> chapters)
        {
            const string sql = @"
INSERT OR IGNORE INTO Chapter
    (ProjectId, ChapterNumber, ChapterTitle, Url, DownloadStatus, ParseStatus,
     HtmlFilePath, TxtFilePath, DownloadError, ParseError, CreatedAt, UpdatedAt)
VALUES
    (@ProjectId, @ChapterNumber, @ChapterTitle, @Url, @DownloadStatus, @ParseStatus,
     @HtmlFilePath, @TxtFilePath, @DownloadError, @ParseError,
     datetime('now'), datetime('now'));";
            try
            {
                using (var conn = _db.GetConnection())
                using (var tx = conn.BeginTransaction())
                {
                    conn.Execute(sql, chapters, tx);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.BulkInsert] {ex.Message}", ex);
            }
        }

        public IEnumerable<Chapter> GetPendingDownloads(int projectId)
        {
            try
            {
                const string sql = @"
SELECT * FROM Chapter
WHERE ProjectId = @ProjectId
  AND DownloadStatus IN (0, 3)
ORDER BY ChapterNumber ASC;";
                using (var conn = _db.GetConnection())
                {
                    return conn.Query<Chapter>(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.GetPendingDownloads] {ex.Message}", ex);
            }
        }

        public IEnumerable<Chapter> GetPendingParse(int projectId)
        {
            try
            {
                const string sql = @"
SELECT * FROM Chapter
WHERE ProjectId = @ProjectId
  AND DownloadStatus = 2
  AND ParseStatus IN (0, 3)
ORDER BY ChapterNumber ASC;";
                using (var conn = _db.GetConnection())
                {
                    return conn.Query<Chapter>(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.GetPendingParse] {ex.Message}", ex);
            }
        }

        public IEnumerable<Chapter> GetAllByProject(int projectId)
        {
            try
            {
                const string sql = "SELECT * FROM Chapter WHERE ProjectId = @ProjectId ORDER BY ChapterNumber ASC;";
                using (var conn = _db.GetConnection())
                {
                    return conn.Query<Chapter>(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.GetAllByProject] {ex.Message}", ex);
            }
        }

        public int CountByDownloadStatus(int projectId, DownloadStatus status)
        {
            try
            {
                const string sql = "SELECT COUNT(*) FROM Chapter WHERE ProjectId=@ProjectId AND DownloadStatus=@Status;";
                using (var conn = _db.GetConnection())
                {
                    return conn.ExecuteScalar<int>(sql, new { ProjectId = projectId, Status = (int)status });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.CountByDownloadStatus] {ex.Message}", ex);
            }
        }

        public int CountByParseStatus(int projectId, ParseStatus status)
        {
            try
            {
                const string sql = "SELECT COUNT(*) FROM Chapter WHERE ProjectId=@ProjectId AND ParseStatus=@Status;";
                using (var conn = _db.GetConnection())
                {
                    return conn.ExecuteScalar<int>(sql, new { ProjectId = projectId, Status = (int)status });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.CountByParseStatus] {ex.Message}", ex);
            }
        }

        public void UpdateDownloadStatus(int chapterId, DownloadStatus status, string htmlFilePath = "", string error = "")
        {
            try
            {
                const string sql = @"
UPDATE Chapter
SET DownloadStatus = @Status,
    HtmlFilePath   = @HtmlFilePath,
    DownloadError  = @Error,
    UpdatedAt      = datetime('now')
WHERE ChapterId = @ChapterId;";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, new
                    {
                        Status      = (int)status,
                        HtmlFilePath = htmlFilePath ?? "",
                        Error       = error ?? "",
                        ChapterId   = chapterId
                    });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[ChapterRepository.UpdateDownloadStatus] ChapterId={chapterId} Status={status} | {ex.Message}", ex);
            }
        }

        public void UpdateParseStatus(int chapterId, ParseStatus status, string txtFilePath = "", string error = "")
        {
            try
            {
                const string sql = @"
UPDATE Chapter
SET ParseStatus  = @Status,
    TxtFilePath  = @TxtFilePath,
    ParseError   = @Error,
    UpdatedAt    = datetime('now')
WHERE ChapterId = @ChapterId;";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, new
                    {
                        Status      = (int)status,
                        TxtFilePath = txtFilePath ?? "",
                        Error       = error ?? "",
                        ChapterId   = chapterId
                    });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[ChapterRepository.UpdateParseStatus] ChapterId={chapterId} Status={status} | {ex.Message}", ex);
            }
        }

        public bool ExistsByUrl(int projectId, string url)
        {
            try
            {
                const string sql = "SELECT COUNT(*) FROM Chapter WHERE ProjectId=@ProjectId AND Url=@Url;";
                using (var conn = _db.GetConnection())
                {
                    return conn.ExecuteScalar<int>(sql, new { ProjectId = projectId, Url = url }) > 0;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.ExistsByUrl] {ex.Message}", ex);
            }
        }

        public int GetMaxChapterNumber(int projectId)
        {
            try
            {
                const string sql = "SELECT COALESCE(MAX(ChapterNumber),0) FROM Chapter WHERE ProjectId=@ProjectId;";
                using (var conn = _db.GetConnection())
                {
                    return conn.ExecuteScalar<int>(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.GetMaxChapterNumber] {ex.Message}", ex);
            }
        }
    }
}
