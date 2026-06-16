using System;
using System.IO;
using System.Text;
using System.Threading;

namespace NovelTTS.Infrastructure.Logging
{
    /// <summary>
    /// Thread-safe file logger with per-category log files.
    /// Categories: crawler, parser, merge, tts, error
    /// </summary>
    public class AppLogger
    {
        private static readonly object _syncRoot = new object();
        private readonly string _logsDir;

        public AppLogger(string logsDir)
        {
            _logsDir = logsDir;
            try
            {
                if (!Directory.Exists(_logsDir))
                    Directory.CreateDirectory(_logsDir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppLogger.ctor] Cannot create logs dir: {ex.Message}");
            }
        }

        // ─── Public API ────────────────────────────────────────────────────────

        public void Crawler(string method, string message, string input = "", string output = "")
            => Write("crawler.log", "CRAWLER", method, message, input, output, null);

        public void Parser(string method, string message, string input = "", string output = "")
            => Write("parser.log", "PARSER", method, message, input, output, null);

        public void Merge(string method, string message, string input = "", string output = "")
            => Write("merge.log", "MERGE", method, message, input, output, null);

        public void Tts(string method, string message, string input = "", string output = "")
            => Write("tts.log", "TTS", method, message, input, output, null);

        public void Error(string method, Exception ex, string input = "")
        {
            Write("error.log", "ERROR", method, ex.Message, input, "", ex);
            // Also write to category-specific logs
            Write("error.log", "ERROR", method, ex.StackTrace ?? "", input, "", null);
        }

        public void Info(string category, string method, string message)
            => Write($"{category.ToLower()}.log", category.ToUpper(), method, message, "", "", null);

        // ─── Core ──────────────────────────────────────────────────────────────

        private void Write(string fileName, string category, string method, string message,
                           string input, string output, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]");
                sb.Append($" [T{Thread.CurrentThread.ManagedThreadId:D3}]");
                sb.Append($" [{category}]");
                sb.Append($" [{method}]");

                if (!string.IsNullOrEmpty(input))
                    sb.Append($" IN={input}");

                sb.Append($" | {message}");

                if (!string.IsNullOrEmpty(output))
                    sb.Append($" | OUT={output}");

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append($"  EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        sb.AppendLine();
                        sb.Append($"  STACKTRACE: {ex.StackTrace}");
                    }
                    if (ex.InnerException != null)
                    {
                        sb.AppendLine();
                        sb.Append($"  INNER: {ex.InnerException.Message}");
                    }
                }

                string line = sb.ToString();
                string filePath = Path.Combine(_logsDir, fileName);

                lock (_syncRoot)
                {
                    File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
                }

                // Also output to debug console
                System.Diagnostics.Debug.WriteLine(line);
            }
            catch
            {
                // Logging must never crash the application
            }
        }
    }
}
