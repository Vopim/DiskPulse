using System;
using System.Collections.Generic;
using System.IO;

namespace DiskPulse
{
    internal sealed class DiskSampler
    {
        private readonly HistoryStore _store;
        public DateTime LastRecordTime = DateTime.MinValue;

        public DiskSampler(HistoryStore store)
        {
            _store = store;
            DayRecord latest = store.Latest;
            if (latest != null) LastRecordTime = latest.UpdatedValue;
        }

        public bool RecordNow(string reason)
        {
            try
            {
                List<DriveRecord> drives = Capture();
                if (drives.Count == 0)
                {
                    Logger.Error("没有读取到可用的本地磁盘", null);
                    return false;
                }

                _store.UpsertDay(drives);
                LastRecordTime = DateTime.Now;
                if (!_store.LastSaveSucceeded)
                {
                    Logger.Error("记录已更新，但写入磁盘失败", null);
                    return false;
                }

                long used = 0;
                foreach (DriveRecord drive in drives) used += drive.Used;
                Logger.Info("记录磁盘用量 (" + reason + "): " + drives.Count + " 块盘, 已用 " + Ui.FormatBytes(used));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("记录磁盘用量失败", ex);
                return false;
            }
        }

        public bool MaybeRecord(int staleMinutes, string reason)
        {
            DateTime today = DateTime.Now.Date;
            DayRecord latest = _store.Latest;
            bool dateChanged = latest == null || latest.DateValue < today;
            bool stale = LastRecordTime == DateTime.MinValue ||
                         (DateTime.Now - LastRecordTime).TotalMinutes >= staleMinutes;
            if (!dateChanged && !stale) return false;
            return RecordNow(reason);
        }

        public static List<DriveRecord> Capture()
        {
            List<DriveRecord> result = new List<DriveRecord>();
            DriveInfo[] all;
            try
            {
                all = DriveInfo.GetDrives();
            }
            catch (Exception ex)
            {
                Logger.Error("枚举磁盘失败", ex);
                return result;
            }

            foreach (DriveInfo drive in all)
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed) continue;
                    if (!drive.IsReady) continue;
                    if (drive.TotalSize <= 0) continue;

                    DriveRecord record = new DriveRecord();
                    record.Name = (drive.Name ?? "").TrimEnd('\\');
                    if (string.IsNullOrEmpty(record.Name)) record.Name = drive.Name;
                    string label = "";
                    try { label = drive.VolumeLabel; }
                    catch { }
                    record.Label = string.IsNullOrEmpty(label) ? "本地磁盘" : label;
                    record.Total = drive.TotalSize;
                    long free = drive.TotalFreeSpace;
                    if (free < 0) free = 0;
                    if (free > record.Total) free = record.Total;
                    record.Used = record.Total - free;
                    result.Add(record);
                }
                catch (Exception ex)
                {
                    Logger.Error("读取磁盘 " + SafeName(drive) + " 失败", ex);
                }
            }

            result.Sort(delegate(DriveRecord a, DriveRecord b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static string SafeName(DriveInfo drive)
        {
            try { return drive.Name; }
            catch { return "?"; }
        }
    }
}
