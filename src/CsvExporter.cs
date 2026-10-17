using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiskPulse
{
    internal static class CsvExporter
    {
        public static int Export(HistoryStore store, string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("日期,磁盘,卷标,总容量(GB),已用(GB),可用(GB),使用率(%)");
            int rows = 0;
            foreach (DayRecord day in store.Days)
            {
                if (day.Drives == null) continue;
                foreach (DriveRecord drive in day.Drives)
                {
                    double totalGb = drive.Total / 1024d / 1024d / 1024d;
                    double usedGb = drive.Used / 1024d / 1024d / 1024d;
                    double freeGb = Math.Max(0, drive.Total - drive.Used) / 1024d / 1024d / 1024d;
                    sb.Append(Csv(day.Date)).Append(',')
                      .Append(Csv(drive.Name)).Append(',')
                      .Append(Csv(drive.Label)).Append(',')
                      .Append(totalGb.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(usedGb.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(freeGb.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append((drive.Ratio * 100d).ToString("0.00", CultureInfo.InvariantCulture))
                      .AppendLine();
                    rows++;
                }
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return rows;
        }

        private static string Csv(string value)
        {
            if (value == null) return "";
            if (value.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
