using System;
using System.IO;

namespace DiskPulse
{
    internal static class AppPaths
    {
        public const string AppName = "DiskPulse";
        public const string DisplayName = "DiskPulse 磁盘存储记录";
        public const string Version = "1.1.1";

        private static string _root;
        private static bool _portableFallback;

        public static bool IsPortableFallback
        {
            get
            {
                string root = Root;
                return _portableFallback;
            }
        }

        public static string Root
        {
            get
            {
                if (_root != null) return _root;

                string custom = Environment.GetEnvironmentVariable("DISKPULSE_DATA_DIR");
                if (!string.IsNullOrEmpty(custom))
                {
                    string customPath = Path.GetFullPath(custom);
                    if (TryPrepare(customPath))
                    {
                        _root = customPath;
                        return _root;
                    }
                }

                string exeDirectory = "";
                try { exeDirectory = Path.GetDirectoryName(ExecutablePath); }
                catch { }
                string portable = null;
                if (!string.IsNullOrEmpty(exeDirectory))
                {
                    portable = Path.Combine(exeDirectory, "DiskPulseData");
                }

                // If the app has already been running in portable mode, keep using
                // that folder so history is not split between two locations.
                if (portable != null && File.Exists(Path.Combine(portable, "history.json")) && TryPrepare(portable))
                {
                    _portableFallback = true;
                    _root = portable;
                    return _root;
                }

                string preferred = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DiskPulse");
                if (TryPrepare(preferred))
                {
                    _root = preferred;
                    return _root;
                }

                // Portable/restricted fallback: keep working when LocalAppData is
                // not writable (e.g. locked-down environments or a portable exe).
                if (portable != null && TryPrepare(portable))
                {
                    _portableFallback = true;
                    _root = portable;
                    return _root;
                }

                string temp = Path.Combine(Path.GetTempPath(), "DiskPulse");
                if (TryPrepare(temp))
                {
                    _portableFallback = true;
                    _root = temp;
                    return _root;
                }

                _root = preferred;
                return _root;
            }
        }

        private static bool TryPrepare(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                string probe = Path.Combine(path, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.WriteByte(0);
                }
                try { File.Delete(probe); }
                catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string HistoryFile
        {
            get { return Path.Combine(Root, "history.json"); }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(Root, "settings.json"); }
        }

        public static string LogDirectory
        {
            get { return Path.Combine(Root, "logs"); }
        }

        public static string ExecutablePath
        {
            get
            {
                try
                {
                    return System.Windows.Forms.Application.ExecutablePath;
                }
                catch
                {
                    return System.Reflection.Assembly.GetExecutingAssembly().Location;
                }
            }
        }

        public static void Ensure()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LogDirectory);
        }
    }
}
