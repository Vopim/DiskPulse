using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DiskPulse
{
    [DataContract]
    public sealed class HistoryFile
    {
        [DataMember(Name = "version", Order = 1)]
        public int Version = 1;

        [DataMember(Name = "created", Order = 2)]
        public string Created;

        [DataMember(Name = "days", Order = 3)]
        public List<DayRecord> Days = new List<DayRecord>();
    }

    [DataContract]
    public sealed class DayRecord
    {
        [DataMember(Name = "date", Order = 1)]
        public string Date;

        [DataMember(Name = "updated", Order = 2)]
        public string Updated;

        [DataMember(Name = "drives", Order = 3)]
        public List<DriveRecord> Drives = new List<DriveRecord>();

        public DateTime DateValue
        {
            get
            {
                DateTime value;
                if (DateTime.TryParseExact(Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out value))
                {
                    return value.Date;
                }
                return DateTime.MinValue;
            }
        }

        public DateTime UpdatedValue
        {
            get
            {
                DateTime value;
                if (DateTime.TryParse(Updated, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out value))
                {
                    return value;
                }
                return DateValue;
            }
        }

        public long TotalUsed()
        {
            long total = 0;
            if (Drives != null)
            {
                foreach (DriveRecord drive in Drives) total += drive.Used;
            }
            return total;
        }

        public long TotalSize()
        {
            long total = 0;
            if (Drives != null)
            {
                foreach (DriveRecord drive in Drives) total += drive.Total;
            }
            return total;
        }

        public DriveRecord FindDrive(string name)
        {
            if (Drives == null || name == null) return null;
            foreach (DriveRecord drive in Drives)
            {
                if (string.Equals(drive.Name, name, StringComparison.OrdinalIgnoreCase)) return drive;
            }
            return null;
        }
    }

    [DataContract]
    public sealed class DriveRecord
    {
        [DataMember(Name = "name", Order = 1)]
        public string Name;

        [DataMember(Name = "label", Order = 2)]
        public string Label;

        [DataMember(Name = "total", Order = 3)]
        public long Total;

        [DataMember(Name = "used", Order = 4)]
        public long Used;

        public long Free
        {
            get
            {
                long free = Total - Used;
                return free < 0 ? 0 : free;
            }
        }

        public double Ratio
        {
            get
            {
                if (Total <= 0) return 0;
                return (double)Used / (double)Total;
            }
        }
    }
}
