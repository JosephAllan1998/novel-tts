using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace novel_tts.Infrastructure.Services
{
    public class LoggerService
    {
        private readonly string _logDirectory;
        private readonly object _lockObject = new object();

        public LoggerService(string projectBaseDirectory)
        {
            _logDirectory = Path.Combine(projectBaseDirectory, "Logs");
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        public void LogInfo(string logFileName, string methodName, string message, string input = null, string output = null)
        {
            WriteLog(logFileName, "INFO", methodName, message, input, output, null);
        }

        public void LogError(string methodName, Exception ex, string input = null)
        {
            WriteLog("error.log", "ERROR", methodName, ex.Message, input, null, ex.StackTrace);
        }

        private void WriteLog(string fileName, string level, string methodName, string message, string input, string output, string stackTrace)
        {
            lock (_lockObject)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"==============================================================================");
                    sb.AppendLine($"Timestamp   : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    sb.AppendLine($"Thread ID   : {Thread.CurrentThread.ManagedThreadId}");
                    sb.AppendLine($"Level       : {level}");
                    sb.AppendLine($"Method      : {methodName}");

                    if (!string.IsNullOrEmpty(input))
                        sb.AppendLine($"Input       : {input}");

                    if (!string.IsNullOrEmpty(output))
                        sb.AppendLine($"Output      : {output}");

                    sb.AppendLine($"Message     : {message}");

                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        sb.AppendLine($"StackTrace  : ");
                        sb.AppendLine(stackTrace);
                    }
                    sb.AppendLine($"==============================================================================");

                    string fullPath = Path.Combine(_logDirectory, fileName);
                    File.AppendAllText(fullPath, sb.ToString(), Encoding.UTF8);
                }
                catch (Exception)
                {
                    // Đảm bảo log lỗi không làm sập ứng dụng chính
                }
            }
        }
    }
}
