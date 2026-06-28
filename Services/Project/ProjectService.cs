using System;
using System.IO;
using NovelTTS.Data;
using NovelTTS.Data.Repositories;
using NovelTTS.Infrastructure.Logging;
using NovelTTS.Models;

namespace NovelTTS.Services.Project
{
    /// <summary>
    /// Manages NovelProject lifecycle: create, open, and resume-state detection.
    /// </summary>
    public class ProjectService
    {
        private readonly AppLogger _logger;

        public ProjectService(AppLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates a new project with the given URL and work directory.
        /// Initialises folder structure, SQLite DB, and inserts novel record.
        /// </summary>
        public NovelProject CreateProject(string novelUrl, string workBaseDir, int chaptersPerMerge = 10)
        {
            const string method = "ProjectService.CreateProject";
            try
            {
                _logger.Info("project", method, $"Creating/opening project: {novelUrl}");

                string slug       = ExtractSlug(novelUrl);
                string projectDir = Path.Combine(workBaseDir, slug);

                // Create directory structure first so the DB path exists
                CreateDirectories(projectDir);

                var dbManager = new DatabaseManager(Path.Combine(projectDir, "Metadata", "novel.db"));
                dbManager.InitializeDatabase();

                var novelRepo = new NovelRepository(dbManager);

                // ── Resume guard: reuse existing project record ────────────────
                NovelProject existing = novelRepo.GetBySlugAndPath(slug, projectDir);
                if (existing != null)
                {
                    // ProjectPath is a computed property from the DB row so set it explicitly
                    existing.ProjectPath = projectDir;
                    _logger.Info("project", method,
                        $"Reusing existing project: slug={slug} id={existing.ProjectId}");
                    return existing;
                }

                // ── New project ────────────────────────────────────────────────
                var novel = new NovelProject
                {
                    NovelSlug        = slug,
                    NovelTitle       = slug,
                    BaseUrl          = NormaliseUrl(novelUrl),
                    TotalChapters    = 0,
                    ProjectPath      = projectDir,
                    ChaptersPerMerge = chaptersPerMerge,
                    CreatedAt        = DateTime.Now,
                    UpdatedAt        = DateTime.Now
                };

                novel.ProjectId = novelRepo.Insert(novel);

                _logger.Info("project", method,
                    $"New project created: slug={slug} path={projectDir} id={novel.ProjectId}");
                return novel;
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex, input: novelUrl);
                throw;
            }
        }

        /// <summary>
        /// Opens an existing project from its project directory (contains Metadata/novel.db).
        /// </summary>
        public NovelProject OpenProject(string projectDir)
        {
            const string method = "ProjectService.OpenProject";
            try
            {
                _logger.Info("project", method, $"Opening project at: {projectDir}");

                string dbPath = Path.Combine(projectDir, "Metadata", "novel.db");
                if (!File.Exists(dbPath))
                    throw new FileNotFoundException($"Database not found: {dbPath}");

                var dbManager = new DatabaseManager(dbPath);
                var novelRepo = new NovelRepository(dbManager);
                var novels    = novelRepo.GetAll();

                NovelProject project = null;
                foreach (var n in novels)
                {
                    project = n;
                    break;
                }

                if (project == null)
                    throw new InvalidOperationException("No novel record found in database.");

                project.ProjectPath = projectDir;

                // Re-create directories if missing (e.g., moved project)
                CreateDirectories(projectDir);

                _logger.Info("project", method,
                    $"Opened: {project.NovelSlug} chapters={project.TotalChapters}");
                return project;
            }
            catch (Exception ex)
            {
                _logger.Error(method, ex, input: projectDir);
                throw;
            }
        }

        /// <summary>
        /// Returns a DatabaseManager for the given project.
        /// </summary>
        public DatabaseManager GetDatabaseManager(NovelProject project)
        {
            try
            {
                return new DatabaseManager(project.DbPath);
            }
            catch (Exception ex)
            {
                _logger.Error("ProjectService.GetDatabaseManager", ex);
                throw;
            }
        }

        // ─── Folder structure ──────────────────────────────────────────────────

        private static void CreateDirectories(string projectDir)
        {
            try
            {
                foreach (string sub in new[] { "Metadata", "Html", "Txt", "Merge", "Audio", "Logs" })
                {
                    string path = Path.Combine(projectDir, sub);
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[ProjectService.CreateDirectories] Could not create dirs at {projectDir}: {ex.Message}", ex);
            }
        }

        // ─── URL helpers ───────────────────────────────────────────────────────

        private static string ExtractSlug(string url)
        {
            try
            {
                // https://truyenfull.today/de-ba/ → "de-ba"
                string trimmed = url.TrimEnd('/');
                int lastSlash  = trimmed.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash < trimmed.Length - 1)
                    return trimmed.Substring(lastSlash + 1).ToLowerInvariant();
                return "novel_" + DateTime.Now.Ticks;
            }
            catch
            {
                return "novel_" + DateTime.Now.Ticks;
            }
        }

        private static string NormaliseUrl(string url)
        {
            try
            {
                url = url.Trim();
                if (!url.EndsWith("/")) url += "/";
                return url;
            }
            catch { return url; }
        }
    }
}
