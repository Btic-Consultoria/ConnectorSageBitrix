using System;
using System.IO;
using System.Text;

namespace ConnectorSageBitrix.Logging
{
    public class Logger : IDisposable
    {
        private readonly string _logPrefix;
        private readonly StreamWriter _logWriter;
        private readonly object _lockObj = new object();
        private bool _disposed = false;

        public Logger(string logDir, string prefix)
        {
            // Ensure log directory exists
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            _logPrefix = prefix;

            // Open log file
            string logPath = Path.Combine(logDir, "connector.log");
            _logWriter = new StreamWriter(
                new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                Encoding.UTF8
            )
            { AutoFlush = true };
        }

        public void Info(string message, params object[] args)
        {
            WriteLog("INFO", message, args);
        }

        public void Debug(string message, params object[] args)
        {
            WriteLog("DEBUG", message, args);
        }

        public void Error(string message, params object[] args)
        {
            WriteLog("ERROR", message, args);
        }

        public void Fatal(string message, params object[] args)
        {
            WriteLog("FATAL", message, args);
        }

        private void WriteLog(string level, string message, params object[] args)
        {
            if (_disposed) return;

            string formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {_logPrefix}[{level}] {formattedMessage}";

            lock (_lockObj)
            {
                // Write to console
                Console.WriteLine(logEntry);

                // Write to file
                _logWriter.WriteLine(logEntry);
            }
        }

        public void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _logWriter?.Dispose();
                _disposed = true;
            }
        }
    }
}