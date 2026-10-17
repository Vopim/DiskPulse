using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiskPulse
{
    internal static class Logger
    {
        private static readonly object Sync = new object();
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                CleanupOldLogs();
            }
            catch
            {
            }
        }

        private static void CleanupOldLogs()
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-14);
                foreach (string file in Directory.GetFiles(AppPaths.LogDirectory, "app-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static string LogFile
        {
            get
            {
                return Path.Combine(AppPaths.LogDirectory, "app-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                Initialize();
                StringBuilder sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                sb.Append(" [").Append(level).Append("] ");
                sb.Append(message);
                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex.ToString());
                }
                sb.AppendLine();
                lock (Sync)
                {
                    File.AppendAllText(LogFile, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
