using novel_tts.Core.Enums;
using novel_tts.Core.Interfaces;
using novel_tts.Core.Models;
using novel_tts.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace novel_tts.Infrastructure.Repositories
{
    public class SqliteNovelRepository : INovelRepository
    {
        private readonly string _connectionString;
        private readonly LoggerService _logger;

        public SqliteNovelRepository(string projectBaseDirectory, LoggerService logger)
        {
            _logger = logger;
            try
            {
                string metadataDir = Path.Combine(projectBaseDirectory, "Metadata");
                if (!Directory.Exists(metadataDir))
                {
                    Directory.CreateDirectory(metadataDir);
                }

                string dbPath = Path.Combine(metadataDir, "project_state.db");
                _connectionString = $"Data Source={dbPath};Version=3;";

                InitializeDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogError("SqliteNovelRepository_Constructor", ex, projectBaseDirectory);
                throw;
            }
        }

        private void InitializeDatabase()
        {
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    StringBuilder sbSchema = new StringBuilder();

                    sbSchema.Append(@"
                        CREATE TABLE IF NOT EXISTS Novels (
                            Id TEXT PRIMARY KEY,
                            Title TEXT,
                            SourceUrl TEXT,
                            TotalChapters INTEGER,
                            SaveDirectory TEXT,
                            CreatedAt TEXT
                        );
                        CREATE TABLE IF NOT EXISTS Chapters (
                            Id TEXT PRIMARY KEY,
                            NovelId TEXT,
                            `Index` INTEGER,
                            Title TEXT,
                            Url TEXT,
                            HtmlFilePath TEXT,
                            TxtFilePath TEXT,
                            DownloadStatus INTEGER,
                            ParseStatus INTEGER,
                            RetryCount INTEGER,
                            LastErrorMessage TEXT,
                            FOREIGN KEY(NovelId) REFERENCES Novels(Id)
                        );
                    ");

                    using (var command = new SQLiteCommand(sbSchema.ToString(), connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
                _logger.LogInfo("crawler.log", "InitializeDatabase", "SQLite Tables initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError("InitializeDatabase", ex);
                throw;
            }
        }

        public async Task SaveNovelAsync(Novel novel)
        {
            string inputLog = $"NovelId: {novel.Id}, Title: {novel.Title}";
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string query = @"
                        INSERT OR REPLACE INTO Novels (Id, Title, SourceUrl, TotalChapters, SaveDirectory, CreatedAt)
                        VALUES (@Id, @Title, @SourceUrl, @TotalChapters, @SaveDirectory, @CreatedAt);";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Id", novel.Id);
                        command.Parameters.AddWithValue("@Title", novel.Title);
                        command.Parameters.AddWithValue("@SourceUrl", novel.SourceUrl);
                        command.Parameters.AddWithValue("@TotalChapters", novel.TotalChapters);
                        command.Parameters.AddWithValue("@SaveDirectory", novel.SaveDirectory);
                        command.Parameters.AddWithValue("@CreatedAt", novel.CreatedAt.ToString("o"));

                        await command.ExecuteNonQueryAsync();
                    }
                }
                _logger.LogInfo("crawler.log", "SaveNovelAsync", "Novel saved/updated successfully.", inputLog);
            }
            catch (Exception ex)
            {
                _logger.LogError("SaveNovelAsync", ex, inputLog);
                throw;
            }
        }

        public async Task SaveChapterAsync(Chapter chapter)
        {
            string inputLog = $"ChapterIndex: {chapter.Index}, Title: {chapter.Title}";
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string query = @"
                        INSERT OR REPLACE INTO Chapters (Id, NovelId, `Index`, Title, Url, HtmlFilePath, TxtFilePath, DownloadStatus, ParseStatus, RetryCount, LastErrorMessage)
                        VALUES (@Id, @NovelId, @Index, @Title, @Url, @HtmlFilePath, @TxtFilePath, @DownloadStatus, @ParseStatus, @RetryCount, @LastErrorMessage);";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Id", chapter.Id);
                        command.Parameters.AddWithValue("@NovelId", chapter.NovelId);
                        command.Parameters.AddWithValue("@Index", chapter.Index);
                        command.Parameters.AddWithValue("@Title", chapter.Title);
                        command.Parameters.AddWithValue("@Url", chapter.Url);
                        command.Parameters.AddWithValue("@HtmlFilePath", chapter.HtmlFilePath);
                        command.Parameters.AddWithValue("@TxtFilePath", chapter.TxtFilePath);
                        command.Parameters.AddWithValue("@DownloadStatus", (int)chapter.DownloadStatus);
                        command.Parameters.AddWithValue("@ParseStatus", (int)chapter.ParseStatus);
                        command.Parameters.AddWithValue("@RetryCount", chapter.RetryCount);
                        command.Parameters.AddWithValue("@LastErrorMessage", (object)chapter.LastErrorMessage ?? DBNull.Value);

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("SaveChapterAsync", ex, inputLog);
                throw;
            }
        }

        public async Task UpdateChapterStatusAsync(string chapterId, JobStatus status, string errorMessage = null)
        {
            string inputLog = $"ChapterId: {chapterId}, Status: {status}";
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string query = @"
                        UPDATE Chapters 
                        SET DownloadStatus = @Status, LastErrorMessage = @Error
                        WHERE Id = @Id;";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Status", (int)status);
                        command.Parameters.AddWithValue("@Error", (object)errorMessage ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Id", chapterId);

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("UpdateChapterStatusAsync", ex, inputLog);
                throw;
            }
        }

        public async Task<List<Chapter>> GetPendingChaptersAsync(string novelId)
        {
            string inputLog = $"NovelId: {novelId}";
            var chapters = new List<Chapter>();
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string query = "SELECT * FROM Chapters WHERE NovelId = @NovelId AND DownloadStatus != 2 ORDER BY `Index` ASC;";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@NovelId", novelId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                chapters.Add(new Chapter
                                {
                                    Id = reader["Id"].ToString(),
                                    NovelId = reader["NovelId"].ToString(),
                                    Index = Convert.ToInt32(reader["Index"]),
                                    Title = reader["Title"].ToString(),
                                    Url = reader["Url"].ToString(),
                                    HtmlFilePath = reader["HtmlFilePath"].ToString(),
                                    TxtFilePath = reader["TxtFilePath"].ToString(),
                                    DownloadStatus = (JobStatus)Convert.ToInt32(reader["DownloadStatus"]),
                                    ParseStatus = (JobStatus)Convert.ToInt32(reader["ParseStatus"]),
                                    RetryCount = Convert.ToInt32(reader["RetryCount"]),
                                    LastErrorMessage = reader["LastErrorMessage"]?.ToString()
                                });
                            }
                        }
                    }
                }
                return chapters;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetPendingChaptersAsync", ex, inputLog);
                throw;
            }
        }
    }
}
