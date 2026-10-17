using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DiskPulse
{
    internal static class JsonUtil
    {
        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length == 0) return null;
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return serializer.ReadObject(stream) as T;
            }
        }

        public static string Serialize<T>(T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                string json = Encoding.UTF8.GetString(stream.ToArray());
                return Pretty(json);
            }
        }

        public static void WriteAtomic(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));

            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }

            string backup = path + ".bak";
            try
            {
                File.Copy(path, backup, true);
                File.Replace(temp, path, null);
            }
            catch
            {
                try
                {
                    File.Copy(temp, path, true);
                }
                finally
                {
                    try { File.Delete(temp); }
                    catch { }
                }
            }
        }

        // DataContractJsonSerializer has no "indented" switch in this framework profile,
        // so format the generated JSON with a tiny whitespace-aware pass.
        public static string Pretty(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            StringBuilder sb = new StringBuilder(json.Length + 1024);
            int indent = 0;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        indent++;
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                        break;
                    case '}':
                    case ']':
                        indent--;
                        if (indent < 0) indent = 0;
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    case ' ':
                    case '\t':
                    case '\r':
                    case '\n':
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Name = "autoStart", Order = 1)]
        public bool AutoStart;

        [DataMember(Name = "rangeDays", Order = 2)]
        public int RangeDays;

        [DataMember(Name = "trayHintShown", Order = 3)]
        public bool TrayHintShown;

        public AppSettings()
        {
            AutoStart = true;
            RangeDays = 30;
        }

        public static AppSettings Load()
        {
            AppSettings settings = null;
            try
            {
                settings = JsonUtil.Read<AppSettings>(AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Logger.Error("读取设置失败，使用默认设置", ex);
            }

            if (settings == null)
            {
                settings = new AppSettings();
                settings.Save();
            }
            settings.Normalize();
            return settings;
        }

        public void Normalize()
        {
            if (RangeDays != 7 && RangeDays != 30 && RangeDays != 90 && RangeDays != 0)
            {
                RangeDays = 30;
            }
        }

        public void Save()
        {
            try
            {
                JsonUtil.WriteAtomic(AppPaths.SettingsFile, JsonUtil.Serialize(this));
            }
            catch (Exception ex)
            {
                Logger.Error("保存设置失败", ex);
            }
        }
    }
}
