using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Dapper;
using NovelTTS.Models;

namespace NovelTTS.Data.Repositories
{
    public class NovelRepository
    {
        private readonly DatabaseManager _db;

        public NovelRepository(DatabaseManager db)
        {
            _db = db;
        }

        public int Insert(NovelProject novel)
        {
            try
            {
                const string sql = @"
INSERT INTO Novel (NovelSlug, NovelTitle, BaseUrl, TotalChapters, ProjectPath, ChaptersPerMerge, CreatedAt, UpdatedAt)
VALUES (@NovelSlug, @NovelTitle, @BaseUrl, @TotalChapters, @ProjectPath, @ChaptersPerMerge,
        datetime('now'), datetime('now'));
SELECT last_insert_rowid();";
                using (var conn = _db.GetConnection())
                {
                    int id = (int)(long)conn.ExecuteScalar(sql, novel);
                    return id;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[NovelRepository.Insert] NovelSlug={novel.NovelSlug} | {ex.Message}", ex);
            }
        }

        public NovelProject GetById(int projectId)
        {
            try
            {
                const string sql = "SELECT * FROM Novel WHERE ProjectId = @ProjectId LIMIT 1;";
                using (var conn = _db.GetConnection())
                {
                    return conn.QueryFirstOrDefault<NovelProject>(sql, new { ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[NovelRepository.GetById] ProjectId={projectId} | {ex.Message}", ex);
            }
        }

        public IEnumerable<NovelProject> GetAll()
        {
            try
            {
                const string sql = "SELECT * FROM Novel ORDER BY CreatedAt DESC;";
                using (var conn = _db.GetConnection())
                {
                    return conn.Query<NovelProject>(sql);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[NovelRepository.GetAll] {ex.Message}", ex);
            }
        }

        public void UpdateTotalChapters(int projectId, int total)
        {
            try
            {
                const string sql = @"
UPDATE Novel SET TotalChapters = @Total, UpdatedAt = datetime('now')
WHERE ProjectId = @ProjectId;";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, new { Total = total, ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[NovelRepository.UpdateTotalChapters] ProjectId={projectId} Total={total} | {ex.Message}", ex);
            }
        }

        public void UpdateChaptersPerMerge(int projectId, int chaptersPerMerge)
        {
            try
            {
                const string sql = @"
UPDATE Novel SET ChaptersPerMerge = @ChaptersPerMerge, UpdatedAt = datetime('now')
WHERE ProjectId = @ProjectId;";
                using (var conn = _db.GetConnection())
                {
                    conn.Execute(sql, new { ChaptersPerMerge = chaptersPerMerge, ProjectId = projectId });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[NovelRepository.UpdateChaptersPerMerge] {ex.Message}", ex);
            }
        }
    }
}
