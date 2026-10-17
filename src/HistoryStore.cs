using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DiskPulse
{
    internal sealed class HistoryStore
    {
        private readonly string _path;

        public HistoryFile Data;
        public bool LastSaveSucceeded = true;

        public HistoryStore(string path)
        {
            _path = path;
            Data = new HistoryFile();
        }

        public string FilePath
        {
            get { return _path; }
        }

        public IList<DayRecord> Days
        {
            get { return Data.Days; }
        }

        public DayRecord Latest
        {
            get
            {
                if (Data.Days == null || Data.Days.Count == 0) return null;
                return Data.Days[Data.Days.Count - 1];
            }
        }

        public DateTime LatestDate
        {
            get
            {
                DayRecord latest = Latest;
                return latest == null ? DateTime.MinValue : latest.DateValue;
            }
        }

        public void Load()
        {
            try
            {
                HistoryFile loaded = JsonUtil.Read<HistoryFile>(_path);
                if (loaded != null)
                {
                    Data = loaded;
                }
            }
            catch (SerializationException ex)
            {
                Logger.Error("历史数据格式损坏，将备份并尝试从 .bak 恢复", ex);
                BackupCorruptFile();
                Data = TryLoadBackup();
            }
            catch (Exception ex)
            {
                // Do not overwrite a file that is temporarily locked or unreadable.
                Logger.Error("读取历史数据失败，本次启动不会覆盖原文件", ex);
                throw;
            }

            if (Data.Days == null) Data.Days = new List<DayRecord>();
            if (string.IsNullOrEmpty(Data.Created))
            {
                Data.Created = DateTime.Now.ToString("o");
            }

            // Sort, drop invalid or duplicate dates, keep the last record for each date.
            List<DayRecord> cleaned = new List<DayRecord>();
            Dictionary<string, DayRecord> byDate = new Dictionary<string, DayRecord>(StringComparer.Ordinal);
            foreach (DayRecord day in Data.Days)
            {
                if (day == null || day.DateValue == DateTime.MinValue) continue;
                if (day.Drives == null) day.Drives = new List<DriveRecord>();
                if (string.IsNullOrEmpty(day.Updated)) day.Updated = day.DateValue.ToString("o");
                byDate[day.Date] = day;
            }
            foreach (DayRecord day in byDate.Values) cleaned.Add(day);
            cleaned.Sort(delegate(DayRecord a, DayRecord b) { return a.DateValue.CompareTo(b.DateValue); });
            Data.Days = cleaned;
        }

        private void BackupCorruptFile()
        {
            try
            {
                if (!System.IO.File.Exists(_path)) return;
                string backup = _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                System.IO.File.Move(_path, backup);
                Logger.Info("已将损坏的历史文件备份到 " + backup);
            }
            catch (Exception ex)
            {
                Logger.Error("备份损坏文件失败", ex);
            }
        }

        private HistoryFile TryLoadBackup()
        {
            try
            {
                HistoryFile backup = JsonUtil.Read<HistoryFile>(_path + ".bak");
                if (backup != null)
                {
                    Logger.Info("已从 history.json.bak 恢复历史数据");
                    return backup;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取历史备份失败", ex);
            }
            return new HistoryFile();
        }

        public DayRecord UpsertDay(List<DriveRecord> drives)
        {
            DateTime now = DateTime.Now;
            string date = now.ToString("yyyy-MM-dd");
            DayRecord record = null;
            foreach (DayRecord day in Data.Days)
            {
                if (day.Date == date)
                {
                    record = day;
                    break;
                }
            }

            if (record == null)
            {
                record = new DayRecord();
                record.Date = date;
                Data.Days.Add(record);
            }

            record.Updated = now.ToString("o");
            record.Drives = drives;
            Data.Days.Sort(delegate(DayRecord a, DayRecord b) { return a.DateValue.CompareTo(b.DateValue); });
            Save();
            return record;
        }

        public void Save()
        {
            try
            {
                EnsureReasonableHistory();
                JsonUtil.WriteAtomic(_path, JsonUtil.Serialize(Data));
                LastSaveSucceeded = true;
            }
            catch (Exception ex)
            {
                LastSaveSucceeded = false;
                Logger.Error("保存历史数据失败", ex);
            }
        }

        private void EnsureReasonableHistory()
        {
            // Keep roughly ten years; this also protects the UI from absurd files.
            const int maxDays = 3700;
            if (Data.Days.Count > maxDays)
            {
                Data.Days.RemoveRange(0, Data.Days.Count - maxDays);
            }
        }

        public DayRecord GetDay(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            foreach (DayRecord day in Data.Days)
            {
                if (day.Date == key) return day;
            }
            return null;
        }

        public DayRecord GetPreviousBefore(DateTime date)
        {
            DayRecord result = null;
            foreach (DayRecord day in Data.Days)
            {
                if (day.DateValue < date.Date)
                {
                    result = day;
                }
                else
                {
                    break;
                }
            }
            return result;
        }

        public List<DayRecord> GetDays(DateTime start, DateTime end)
        {
            List<DayRecord> result = new List<DayRecord>();
            foreach (DayRecord day in Data.Days)
            {
                DateTime value = day.DateValue;
                if (value >= start.Date && value <= end.Date) result.Add(day);
            }
            return result;
        }

        public long LatestTotalUsed()
        {
            DayRecord latest = Latest;
            return latest == null ? 0 : latest.TotalUsed();
        }
    }
}
