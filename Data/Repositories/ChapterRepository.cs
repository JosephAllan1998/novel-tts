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

        // ─── Insert ────────────────────────────────────────────────────────────

        public void Insert(Chapter chapter)
        {
            try
            {
                const string sql = @"
INSERT OR IGNORE INTO Chapter
    (ProjectId, ChapterNumber, ChapterTitle, Url, DownloadStatus, ParseStatus, CreatedAt, UpdatedAt)
VALUES
    (@ProjectId, @ChapterNumber, @ChapterTitle, @Url, @DownloadStatus, @ParseStatus,
     datetime('now'), datetime('now'));
SELECT last_insert_rowid();";
                using (var conn = _db.GetConnection())
                {
                    long id = (long)conn.ExecuteScalar(sql, chapter);
                    if (id > 0) chapter.ChapterId = (int)id;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[ChapterRepository.Insert] ProjectId={chapter.ProjectId} ChapterNumber={chapter.ChapterNumber} | {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Inserts each chapter individually so that the generated ChapterId is written
        /// back into the in-memory object. This is critical: callers (HtmlDownloader,
        /// HtmlParser) use chapter.ChapterId to call UpdateDownloadStatus /
        /// UpdateParseStatus — if ChapterId is 0 those updates hit no rows.
        /// Using INSERT OR IGNORE means duplicate URLs for the same project are silently
        /// skipped; the existing row's ID is then fetched via GetByUrl.
        /// </summary>
        public void BulkInsert(IEnumerable<Chapter> chapters)
        {
            const string insertSql = @"
INSERT OR IGNORE INTO Chapter
    (ProjectId, ChapterNumber, ChapterTitle, Url, DownloadStatus, ParseStatus, CreatedAt, UpdatedAt)
VALUES
    (@ProjectId, @ChapterNumber, @ChapterTitle, @Url, @DownloadStatus, @ParseStatus,
     datetime('now'), datetime('now'));";

            const string idSql = @"
SELECT ChapterId FROM Chapter
WHERE ProjectId = @ProjectId AND Url = @Url
LIMIT 1;";

            try
            {
                using (var conn = _db.GetConnection())
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var chapter in chapters)
                    {
                        conn.Execute(insertSql, chapter, tx);
                        // Always re-fetch the real PK (handles both new inserts and
                        // pre-existing rows that were skipped by INSERT OR IGNORE)
                        int chapterId = conn.ExecuteScalar<int>(idSql,
                            new { chapter.ProjectId, chapter.Url }, tx);
                        chapter.ChapterId = chapterId;
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.BulkInsert] {ex.Message}", ex);
            }
        }

        // ─── Queries ───────────────────────────────────────────────────────────

        public Chapter GetByUrl(int projectId, string url)
        {
            try
            {
                const string sql = @"
SELECT * FROM Chapter
WHERE ProjectId = @ProjectId AND Url = @Url
LIMIT 1;";
                using (var conn = _db.GetConnection())
                {
                    return conn.QueryFirstOrDefault<Chapter>(sql, new { ProjectId = projectId, Url = url });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[ChapterRepository.GetByUrl] {ex.Message}", ex);
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

        // ─── Updates ───────────────────────────────────────────────────────────

        /// <summary>
        /// Updates download status. Uses ChapterId when available (> 0),
        /// otherwise falls back to (ProjectId, Url) to handle cases where
        /// the chapter came from an in-memory queue before the DB ID was set.
        /// </summary>
        public void UpdateDownloadStatus(int chapterId, DownloadStatus status,
            string htmlFilePath = "", string error = "",
            int projectId = 0, string url = "")
        {
            try
            {
                string sql;
                object param;

                if (chapterId > 0)
                {
                    sql = @"
UPDATE Chapter
SET DownloadStatus = @Status,
    HtmlFilePath   = @HtmlFilePath,
    DownloadError  = @Error,
    UpdatedAt      = datetime('now')
WHERE ChapterId = @ChapterId;";
                    param = new
                    {
                        Status       = (int)status,
                        HtmlFilePath = htmlFilePath ?? "",
                        Error        = error ?? "",
                        ChapterId    = chapterId
                    };
                }
                else
                {
                    // Fallback: key on (ProjectId, Url)
                    sql = @"
UPDATE Chapter
SET DownloadStatus = @Status,
    HtmlFilePath   = @HtmlFilePath,
    DownloadError  = @Error,
    UpdatedAt      = datetime('now')
WHERE ProjectId = @ProjectId AND Url = @Url;";
                    param = new
                    {
                        Status       = (int)status,
                        HtmlFilePath = htmlFilePath ?? "",
                        Error        = error ?? "",
                        ProjectId    = projectId,
                        Url          = url ?? ""
                    };
                }

                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, param);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[ChapterRepository.UpdateDownloadStatus] ChapterId={chapterId} Status={status} | {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Updates parse status. Uses ChapterId when available (> 0),
        /// otherwise falls back to (ProjectId, Url).
        /// </summary>
        public void UpdateParseStatus(int chapterId, ParseStatus status,
            string txtFilePath = "", string error = "",
            int projectId = 0, string url = "")
        {
            try
            {
                string sql;
                object param;

                if (chapterId > 0)
                {
                    sql = @"
UPDATE Chapter
SET ParseStatus  = @Status,
    TxtFilePath  = @TxtFilePath,
    ParseError   = @Error,
    UpdatedAt    = datetime('now')
WHERE ChapterId = @ChapterId;";
                    param = new
                    {
                        Status      = (int)status,
                        TxtFilePath = txtFilePath ?? "",
                        Error       = error ?? "",
                        ChapterId   = chapterId
                    };
                }
                else
                {
                    sql = @"
UPDATE Chapter
SET ParseStatus  = @Status,
    TxtFilePath  = @TxtFilePath,
    ParseError   = @Error,
    UpdatedAt    = datetime('now')
WHERE ProjectId = @ProjectId AND Url = @Url;";
                    param = new
                    {
                        Status      = (int)status,
                        TxtFilePath = txtFilePath ?? "",
                        Error       = error ?? "",
                        ProjectId   = projectId,
                        Url         = url ?? ""
                    };
                }

                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, param);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[ChapterRepository.UpdateParseStatus] ChapterId={chapterId} Status={status} | {ex.Message}", ex);
            }
        }

        // ─── Helpers ───────────────────────────────────────────────────────────

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
