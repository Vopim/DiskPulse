using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiskPulse
{
    internal sealed class CsvImportResult
    {
        public int TotalRows;
        public int ValidRows;
        public int ImportedDays;
        public int UpdatedDays;
        public bool Saved = true;
        public readonly List<string> Errors = new List<string>();
    }

    internal static class CsvImporter
    {
        private const double Gib = 1073741824d;

        public static CsvImportResult Import(HistoryStore store, string path)
        {
            CsvImportResult result = new CsvImportResult();
            if (!File.Exists(path)) throw new FileNotFoundException("找不到要导入的文件", path);

            string[] lines = ReadAllLinesAuto(path);
            if (lines.Length == 0) throw new InvalidDataException("CSV 文件为空");

            int dateIdx = -1;
            int driveIdx = -1;
            int labelIdx = -1;
            int totalIdx = -1;
            int usedIdx = -1;
            int freeIdx = -1;

            int dataStart = 0;
            if (lines.Length > 0)
            {
                string[] header = ParseCsvLine(lines[0]);
                if (LooksLikeHeader(header))
                {
                    for (int i = 0; i < header.Length; i++)
                    {
                        string h = Clean(header[i]);
                        if (dateIdx < 0 && (h.IndexOf("日期", StringComparison.Ordinal) >= 0 || h == "date" || h == "day")) dateIdx = i;
                        else if (driveIdx < 0 && (h.IndexOf("磁盘", StringComparison.Ordinal) >= 0 || h.IndexOf("盘", StringComparison.Ordinal) >= 0 || h == "drive")) driveIdx = i;
                        else if (labelIdx < 0 && (h.IndexOf("卷标", StringComparison.Ordinal) >= 0 || h == "label")) labelIdx = i;
                        else if (totalIdx < 0 && (h.IndexOf("总容量", StringComparison.Ordinal) >= 0 || h.IndexOf("容量", StringComparison.Ordinal) >= 0 || h == "total")) totalIdx = i;
                        else if (usedIdx < 0 && (h.IndexOf("已用", StringComparison.Ordinal) >= 0 || h == "used")) usedIdx = i;
                        else if (freeIdx < 0 && (h.IndexOf("可用", StringComparison.Ordinal) >= 0 || h.IndexOf("剩余", StringComparison.Ordinal) >= 0 || h == "free")) freeIdx = i;
                    }
                    dataStart = 1;
                }
                else
                {
                    // Default export column order without a header.
                    dateIdx = 0; driveIdx = 1; labelIdx = 2; totalIdx = 3; usedIdx = 4; freeIdx = 5;
                }
            }

            if (dateIdx < 0 || driveIdx < 0) throw new InvalidDataException("CSV 缺少必需的“日期”或“磁盘”列");
            if (usedIdx < 0 && totalIdx < 0) throw new InvalidDataException("CSV 缺少“已用”或“总容量”列");

            // Build a map of existing days so imported rows merge with them.
            Dictionary<string, DayRecord> byDate = new Dictionary<string, DayRecord>(StringComparer.Ordinal);
            Dictionary<string, bool> existedBefore = new Dictionary<string, bool>(StringComparer.Ordinal);
            HashSet<string> importedDays = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> updatedDays = new HashSet<string>(StringComparer.Ordinal);
            foreach (DayRecord day in store.Days)
            {
                if (day == null || day.DateValue == DateTime.MinValue) continue;
                if (day.Drives == null) day.Drives = new List<DriveRecord>();
                byDate[day.Date] = day;
                existedBefore[day.Date] = true;
            }

            for (int lineIndex = dataStart; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] fields = ParseCsvLine(line);
                if (fields.Length == 1 && string.IsNullOrWhiteSpace(fields[0])) continue;
                result.TotalRows++;

                string dateText = Get(fields, dateIdx).Trim();
                string driveName = NormalizeDrive(Get(fields, driveIdx));
                string label = labelIdx >= 0 ? Get(fields, labelIdx).Trim() : "";

                DateTime date;
                if (!TryParseDate(dateText, out date))
                {
                    result.Errors.Add("第 " + (lineIndex + 1) + " 行：日期无效 \"" + dateText + "\"");
                    continue;
                }
                if (string.IsNullOrEmpty(driveName))
                {
                    result.Errors.Add("第 " + (lineIndex + 1) + " 行：磁盘名无效");
                    continue;
                }

                double totalGb = 0;
                double usedGb = 0;
                double freeGb = 0;
                bool hasTotal = TryParseNumber(totalIdx >= 0 ? Get(fields, totalIdx) : "", out totalGb) && totalGb > 0;
                bool hasUsed = TryParseNumber(usedIdx >= 0 ? Get(fields, usedIdx) : "", out usedGb);
                bool hasFree = TryParseNumber(freeIdx >= 0 ? Get(fields, freeIdx) : "", out freeGb);

                if (!hasUsed && hasTotal && hasFree) usedGb = Math.Max(0, totalGb - freeGb);
                if (!hasTotal && hasUsed && hasFree) totalGb = usedGb + freeGb;
                if (!hasTotal || !hasUsed || totalGb <= 0)
                {
                    result.Errors.Add("第 " + (lineIndex + 1) + " 行：缺少或非法的容量数值");
                    continue;
                }

                long total = (long)Math.Round(totalGb * Gib);
                long used = (long)Math.Round(usedGb * Gib);
                if (total <= 0)
                {
                    result.Errors.Add("第 " + (lineIndex + 1) + " 行：总容量必须大于 0");
                    continue;
                }
                if (used < 0) used = 0;
                if (used > total) used = total;

                string dateKey = date.ToString("yyyy-MM-dd");
                DayRecord day;
                if (!byDate.TryGetValue(dateKey, out day))
                {
                    day = new DayRecord();
                    day.Date = dateKey;
                    day.Updated = DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Local).ToString("o");
                    byDate[dateKey] = day;
                    importedDays.Add(dateKey);
                }
                else if (existedBefore.ContainsKey(dateKey))
                {
                    updatedDays.Add(dateKey);
                }

                DriveRecord drive = day.FindDrive(driveName);
                if (drive == null)
                {
                    drive = new DriveRecord();
                    drive.Name = driveName;
                    day.Drives.Add(drive);
                }
                drive.Total = total;
                drive.Used = used;
                if (!string.IsNullOrEmpty(label)) drive.Label = label;
                else if (string.IsNullOrEmpty(drive.Label)) drive.Label = "本地磁盘";

                result.ValidRows++;
            }

            result.ImportedDays = importedDays.Count;
            result.UpdatedDays = updatedDays.Count;

            if (result.ValidRows == 0)
            {
                result.Saved = false;
                return result;
            }

            List<DayRecord> allDays = new List<DayRecord>(byDate.Values);
            allDays.Sort(delegate(DayRecord a, DayRecord b) { return a.DateValue.CompareTo(b.DateValue); });
            store.Data.Days = allDays;
            store.Save();
            result.Saved = store.LastSaveSucceeded;
            return result;
        }

        private static string[] ReadAllLinesAuto(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string text;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            else
            {
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    text = Encoding.Default.GetString(bytes);
                }
            }
            return text.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private static string[] ParseCsvLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                    else sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        private static bool LooksLikeHeader(string[] fields)
        {
            foreach (string field in fields)
            {
                string h = Clean(field);
                if (h.IndexOf("日期", StringComparison.Ordinal) >= 0 || h == "date" ||
                    h.IndexOf("磁盘", StringComparison.Ordinal) >= 0 || h == "drive" ||
                    h.IndexOf("卷标", StringComparison.Ordinal) >= 0 || h == "label" ||
                    h.IndexOf("容量", StringComparison.Ordinal) >= 0 || h == "total" ||
                    h.IndexOf("已用", StringComparison.Ordinal) >= 0 || h == "used" ||
                    h.IndexOf("可用", StringComparison.Ordinal) >= 0 || h == "free")
                {
                    return true;
                }
            }
            return false;
        }

        private static string Clean(string value)
        {
            if (value == null) return "";
            return value.Trim().Trim('\uFEFF', '"', ' ', '\t');
        }

        private static string Get(string[] fields, int index)
        {
            if (fields == null || index < 0 || index >= fields.Length) return "";
            return fields[index] ?? "";
        }

        private static bool TryParseDate(string text, out DateTime date)
        {
            if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return true;
            }
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return date.Year >= 1970 && date.Year <= 2100;
            }
            return false;
        }

        private static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeDrive(string raw)
        {
            string value = Clean(raw);
            if (value.Length == 0) return "";
            value = value.TrimEnd('\\', '/', ':');
            if (value.Length == 0) return "";
            char letter = char.ToUpperInvariant(value[0]);
            if (letter < 'A' || letter > 'Z') return "";
            return letter + ":";
        }
    }
}
