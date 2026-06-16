using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace NovelTTS.Data
{
    /// <summary>
    /// Manages SQLite database creation and connection provisioning.
    /// </summary>
    public class DatabaseManager
    {
        private readonly string _dbPath;
        private readonly StringBuilder _log = new StringBuilder();

        public DatabaseManager(string dbPath)
        {
            _dbPath = dbPath;
        }

        /// <summary>Returns an open SQLiteConnection. Caller must dispose.</summary>
        public SQLiteConnection GetConnection()
        {
            try
            {
                var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                connection.Open();
                return connection;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[DatabaseManager.GetConnection] Failed to open DB at '{_dbPath}': {ex.Message}", ex);
            }
        }

        /// <summary>Creates the database file and all tables if they do not exist.</summary>
        public void InitializeDatabase()
        {
            try
            {
                string dirPath = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);

                if (!File.Exists(_dbPath))
                    SQLiteConnection.CreateFile(_dbPath);

                using (var conn = GetConnection())
                {
                    ExecuteNonQuery(conn, Sql_CreateNovelTable());
                    ExecuteNonQuery(conn, Sql_CreateChapterTable());
                    ExecuteNonQuery(conn, Sql_CreateMergeJobTable());
                    ExecuteNonQuery(conn, Sql_CreateAudioJobTable());
                    ExecuteNonQuery(conn, "PRAGMA journal_mode=WAL;");  // better concurrent read/write
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[DatabaseManager.InitializeDatabase] {ex.Message}", ex);
            }
        }

        private void ExecuteNonQuery(SQLiteConnection conn, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        // ─── DDL ───────────────────────────────────────────────────────────────

        private string Sql_CreateNovelTable() => @"
CREATE TABLE IF NOT EXISTS Novel (
    ProjectId       INTEGER PRIMARY KEY AUTOINCREMENT,
    NovelSlug       TEXT    NOT NULL,
    NovelTitle      TEXT    NOT NULL DEFAULT '',
    BaseUrl         TEXT    NOT NULL,
    TotalChapters   INTEGER NOT NULL DEFAULT 0,
    ProjectPath     TEXT    NOT NULL,
    ChaptersPerMerge INTEGER NOT NULL DEFAULT 10,
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
);";

        private string Sql_CreateChapterTable() => @"
CREATE TABLE IF NOT EXISTS Chapter (
    ChapterId       INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId       INTEGER NOT NULL,
    ChapterNumber   INTEGER NOT NULL,
    ChapterTitle    TEXT    NOT NULL DEFAULT '',
    Url             TEXT    NOT NULL,
    DownloadStatus  INTEGER NOT NULL DEFAULT 0,
    ParseStatus     INTEGER NOT NULL DEFAULT 0,
    HtmlFilePath    TEXT    NOT NULL DEFAULT '',
    TxtFilePath     TEXT    NOT NULL DEFAULT '',
    DownloadError   TEXT    NOT NULL DEFAULT '',
    ParseError      TEXT    NOT NULL DEFAULT '',
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ProjectId) REFERENCES Novel(ProjectId),
    UNIQUE (ProjectId, ChapterNumber)
);
CREATE INDEX IF NOT EXISTS idx_chapter_project_download ON Chapter(ProjectId, DownloadStatus);
CREATE INDEX IF NOT EXISTS idx_chapter_project_parse    ON Chapter(ProjectId, ParseStatus);";

        private string Sql_CreateMergeJobTable() => @"
CREATE TABLE IF NOT EXISTS MergeJob (
    JobId           INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId       INTEGER NOT NULL,
    FromChapter     INTEGER NOT NULL,
    ToChapter       INTEGER NOT NULL,
    OutputFilePath  TEXT    NOT NULL DEFAULT '',
    MergeStatus     INTEGER NOT NULL DEFAULT 0,
    ErrorMessage    TEXT    NOT NULL DEFAULT '',
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ProjectId) REFERENCES Novel(ProjectId),
    UNIQUE (ProjectId, FromChapter, ToChapter)
);";

        private string Sql_CreateAudioJobTable() => @"
CREATE TABLE IF NOT EXISTS AudioJob (
    JobId           INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId       INTEGER NOT NULL,
    MergeJobId      INTEGER NOT NULL,
    SourceFilePath  TEXT    NOT NULL DEFAULT '',
    OutputFilePath  TEXT    NOT NULL DEFAULT '',
    AudioStatus     INTEGER NOT NULL DEFAULT 0,
    Format          INTEGER NOT NULL DEFAULT 1,
    ErrorMessage    TEXT    NOT NULL DEFAULT '',
    CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ProjectId)  REFERENCES Novel(ProjectId),
    FOREIGN KEY (MergeJobId) REFERENCES MergeJob(JobId)
);";
    }
}
