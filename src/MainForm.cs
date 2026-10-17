using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DiskPulse
{
    internal sealed class MainForm : Form
    {
        private readonly HistoryStore _store;
        private readonly DiskSampler _sampler;
        private readonly AppSettings _settings;

        private readonly StatCard[] _cards = new StatCard[4];
        private readonly SegmentedControl _range;
        private readonly ChartControl _chart;
        private readonly List<DriveChip> _chips = new List<DriveChip>();
        private readonly PillButton _recordButton;
        private readonly PillButton _exportButton;
        private readonly PillButton _folderButton;
        private readonly ToggleSwitch _autoToggle;
        private readonly Label _autoLabel;
        private readonly CaptionButton _minButton;
        private readonly CaptionButton _maxButton;
        private readonly CaptionButton _closeButton;
        private readonly ToolTip _toolTip;
        private readonly Timer _recordTimer;
        private readonly Timer _statusTimer;
        private NotifyIcon _tray;
        private readonly Icon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayAutoItem;
        private TrayApplicationContext _context;

        private readonly Dictionary<string, Color> _driveColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<DriveRecord> _latestDrives = new List<DriveRecord>();
        private bool _showTotal = true;
        private int _rangeDays = 30;
        private bool _exiting;
        private bool _initialSized;
        private bool _autoStartChanging;
        private bool _autoStartWarning;
        private bool _autoStartWarningShown;
        private string _status = "";
        private bool _statusError;
        private DateTime _statusUntil = DateTime.MinValue;
        private bool _dragging;
        private Point _dragStart;
        private int _footerY;

        private const int WmNcHitTest = 0x84;
        private const int HtClient = 1;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public MainForm(HistoryStore store, DiskSampler sampler, AppSettings settings, bool startHidden)
        {
            _store = store;
            _sampler = sampler;
            _settings = settings;
            _rangeDays = settings.RangeDays;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            Text = AppPaths.DisplayName;
            ShowInTaskbar = !startHidden;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1180, 800);
            KeyPreview = true;

            Icon trayIconSource = AppIcon.Load();
            _trayIcon = trayIconSource;
            Icon = trayIconSource;

            _cards[0] = NewCard("总已用空间", Theme.Accent, CardIconKind.Disk);
            _cards[1] = NewCard("较昨日", Theme.Red, CardIconKind.Trend);
            _cards[2] = NewCard("可用空间", Theme.Cyan, CardIconKind.Free);
            _cards[3] = NewCard("今日记录", Theme.Violet, CardIconKind.Clock);
            foreach (StatCard card in _cards) Controls.Add(card);

            _range = new SegmentedControl();
            _range.Items = new string[] { "7 天", "30 天", "90 天", "全部" };
            _range.SelectedIndex = RangeIndex(_rangeDays);
            _range.SelectionChanged += delegate
            {
                _rangeDays = RangeFromIndex(_range.SelectedIndex);
                _settings.RangeDays = _rangeDays;
                _settings.Save();
                RefreshData(false);
                SetStatus("已切换到" + _range.Items[_range.SelectedIndex] + "视图");
            };
            Controls.Add(_range);

            _chart = new ChartControl();
            Controls.Add(_chart);

            _recordButton = new PillButton();
            _recordButton.Text = "立即记录";
            _recordButton.Primary = true;
            _recordButton.Accent = Theme.Accent;
            _recordButton.Click += delegate { DoRecord("手动记录"); };
            Controls.Add(_recordButton);

            _exportButton = new PillButton();
            _exportButton.Text = "导出 CSV";
            _exportButton.Click += delegate { ExportCsv(); };
            Controls.Add(_exportButton);

            _folderButton = new PillButton();
            _folderButton.Text = "打开目录";
            _folderButton.Click += delegate { OpenDataFolder(); };
            Controls.Add(_folderButton);

            _autoToggle = new ToggleSwitch();
            bool actualAutoStart = AutoStart.IsCurrentCommand();
            _autoToggle.SetCheckedSilently(actualAutoStart);
            _autoStartWarning = settings.AutoStart != actualAutoStart;
            _autoToggle.CheckedChanged += delegate { SetAutoStart(_autoToggle.Checked); };
            Controls.Add(_autoToggle);

            _autoLabel = new Label();
            _autoLabel.Text = "开机自动记录";
            _autoLabel.AutoSize = false;
            _autoLabel.BackColor = Theme.Base;
            _autoLabel.ForeColor = Theme.TextSecondary;
            _autoLabel.Font = Theme.Font(8.9f, FontStyle.Regular);
            _autoLabel.TextAlign = ContentAlignment.MiddleLeft;
            _autoLabel.UseMnemonic = false;
            Controls.Add(_autoLabel);

            _minButton = NewCaptionButton(CaptionKind.Minimize);
            _maxButton = NewCaptionButton(CaptionKind.Maximize);
            _closeButton = NewCaptionButton(CaptionKind.Close);
            _minButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            _maxButton.Click += delegate { ToggleMaximize(); };
            _closeButton.Click += delegate { HideToTray(); };
            Controls.Add(_minButton);
            Controls.Add(_maxButton);
            Controls.Add(_closeButton);

            _toolTip = new ToolTip();
            _toolTip.AutoPopDelay = 5000;
            _toolTip.InitialDelay = 450;
            _toolTip.ReshowDelay = 100;
            _toolTip.SetToolTip(_minButton, "最小化");
            _toolTip.SetToolTip(_maxButton, "最大化 / 还原");
            _toolTip.SetToolTip(_closeButton, "关闭到托盘（继续后台记录）");
            _toolTip.SetToolTip(_autoToggle, "登录 Windows 后静默启动并在后台记录");
            _toolTip.SetToolTip(_recordButton, "立即保存今天的磁盘使用快照 (F5)");
            _toolTip.SetToolTip(_exportButton, "将全部历史记录导出为 CSV (Ctrl+E)");
            _toolTip.SetToolTip(_folderButton, "打开历史记录与日志所在文件夹");

            _recordTimer = new Timer();
            _recordTimer.Interval = 60000;
            _recordTimer.Tick += delegate { OnRecordTimer(); };
            _recordTimer.Start();

            _statusTimer = new Timer();
            _statusTimer.Interval = 1000;
            _statusTimer.Tick += delegate
            {
                if (DateTime.Now >= _statusUntil)
                {
                    _status = "";
                    _statusTimer.Stop();
                    InvalidateFooter();
                }
            };

            BuildTray();

            FormClosing += OnFormClosing;
            Resize += delegate
            {
                if (WindowState == FormWindowState.Normal) _maxButton.IsMaximized = false;
                LayoutUi();
                Invalidate();
            };
            MouseDown += OnTitleMouseDown;
            MouseMove += OnTitleMouseMove;
            MouseUp += OnTitleMouseUp;
            MouseDoubleClick += OnTitleDoubleClick;

            RefreshData(false);
        }

        private StatCard NewCard(string title, Color accent, CardIconKind icon)
        {
            StatCard card = new StatCard();
            card.Title = title;
            card.Accent = accent;
            card.Icon = icon;
            return card;
        }

        private CaptionButton NewCaptionButton(CaptionKind kind)
        {
            CaptionButton button = new CaptionButton();
            button.Kind = kind;
            return button;
        }

        public void AttachContext(TrayApplicationContext context)
        {
            _context = context;
        }

        public void EnsureHandleCreated()
        {
            if (!IsHandleCreated)
            {
                IntPtr handle = Handle;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!_initialSized)
            {
                _initialSized = true;
                float s = Ui.S(this, 1f);
                int minW = (int)Math.Round(1000f * s);
                int minH = (int)Math.Round(660f * s);
                MinimumSize = new Size(minW, minH);

                Rectangle working = Screen.FromControl(this).WorkingArea;
                int desiredW = (int)Math.Round(1180f * s);
                int desiredH = (int)Math.Round(800f * s);
                int maxW = Math.Max(minW, working.Width - (int)Math.Round(32f * s));
                int maxH = Math.Max(minH, working.Height - (int)Math.Round(32f * s));
                int width = Math.Min(desiredW, maxW);
                int height = Math.Min(desiredH, maxH);
                Size = new Size(width, height);
                try
                {
                    uint windowDpi = 0;
                    try { windowDpi = GetDpiForWindow(Handle); }
                    catch { }
                    float graphicsDpi = 0f;
                    try { using (Graphics g = CreateGraphics()) graphicsDpi = g.DpiX; }
                    catch { }
                    Logger.Info("UI init: DeviceDpi=" + DeviceDpi + ", WindowDpi=" + windowDpi + ", GraphicsDpi=" + graphicsDpi +
                        ", ClientSize=" + ClientSize.Width + "x" + ClientSize.Height +
                        ", WorkingArea=" + Screen.FromControl(this).WorkingArea + ", Scale=" + Ui.S(this, 1f));
                }
                catch
                {
                }
                LayoutUi();
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        private void OnRecordTimer()
        {
            if (_sampler.MaybeRecord(55, "后台定时"))
            {
                RefreshData(false);
                SetStatus("已自动记录 " + DateTime.Now.ToString("HH:mm") + " 的磁盘用量");
            }
            else
            {
                InvalidateFooter();
            }
        }

        private void DoRecord(string reason)
        {
            bool ok = _sampler.RecordNow(reason);
            RefreshData(false);
            if (ok)
            {
                SetStatus("已保存 " + DateTime.Now.ToString("HH:mm") + " 的磁盘快照");
            }
            else
            {
                SetStatus("记录失败，请查看数据目录中的日志", true);
            }
        }

        private void SetStatus(string text)
        {
            SetStatus(text, false);
        }

        private void SetStatus(string text, bool error)
        {
            _status = text ?? "";
            _statusError = error;
            _statusUntil = DateTime.Now.AddSeconds(7);
            _statusTimer.Start();
            InvalidateFooter();
        }

        private void InvalidateFooter()
        {
            if (Width <= 0 || Height <= 0) return;
            Invalidate(new Rectangle(0, _footerY - 2, Width, Height - _footerY + 2));
        }

        private void RefreshData(bool recordIfStale)
        {
            if (recordIfStale)
            {
                _sampler.MaybeRecord(5, "打开界面");
            }

            DayRecord latest = _store.Latest;
            _latestDrives = latest == null ? new List<DriveRecord>() : new List<DriveRecord>(latest.Drives);
            RebuildDriveColors();
            UpdateCards(latest);
            RebuildChips(latest);
            UpdateChart(latest);
            LayoutUi();
            Invalidate();
        }

        private void RebuildDriveColors()
        {
            _driveColors.Clear();
            Color[] palette = new Color[]
            {
                Theme.Cyan, Theme.Violet, Theme.Amber, Theme.Pink, Theme.Green,
                Color.FromArgb(96, 165, 250), Color.FromArgb(251, 146, 60), Color.FromArgb(45, 212, 191)
            };
            for (int i = 0; i < _latestDrives.Count && i < palette.Length; i++)
            {
                _driveColors[_latestDrives[i].Name] = palette[i];
            }
        }

        private Color DriveColor(string name)
        {
            Color color;
            if (_driveColors.TryGetValue(name, out color)) return color;
            int index = _driveColors.Count;
            Color[] palette = new Color[] { Theme.Cyan, Theme.Violet, Theme.Amber, Theme.Pink, Theme.Green };
            return palette[index % palette.Length];
        }

        private void UpdateCards(DayRecord latest)
        {
            if (latest == null)
            {
                _cards[0].Value = "--";
                _cards[0].SubText = "等待首次记录";
                _cards[1].Value = "--";
                _cards[1].ValueColor = Theme.TextSecondary;
                _cards[1].DeltaDirection = 0;
                _cards[1].SubText = "需要至少两天的记录";
                _cards[2].Value = "--";
                _cards[2].SubText = "等待首次记录";
                _cards[3].Title = "今日记录";
                _cards[3].Value = "--";
                _cards[3].SubText = "后台会自动记录";
                return;
            }

            long used = latest.TotalUsed();
            long total = latest.TotalSize();
            long free = Math.Max(0, total - used);
            double freeRatio = total > 0 ? (double)free / (double)total : 0;

            _cards[0].Value = Ui.FormatBytes(used);
            _cards[0].SubText = latest.Drives.Count + " 块磁盘 · 共 " + Ui.FormatBytes(total);

            DayRecord previous = _store.GetPreviousBefore(latest.DateValue);
            if (previous == null)
            {
                _cards[1].Value = "--";
                _cards[1].ValueColor = Theme.TextSecondary;
                _cards[1].DeltaDirection = 0;
                _cards[1].SubText = "需要至少两天的记录";
            }
            else
            {
                long delta = used - previous.TotalUsed();
                _cards[1].Value = Ui.FormatSignedBytes(delta);
                _cards[1].ValueColor = Ui.StatusColor(delta);
                _cards[1].DeltaDirection = delta > 0 ? 1 : (delta < 0 ? -1 : 0);
                _cards[1].SubText = "对比 " + previous.DateValue.ToString("MM-dd") + " 的记录";
            }

            _cards[2].Value = Ui.FormatBytes(free);
            _cards[2].SubText = "剩余 " + Ui.FormatPercent(freeRatio) + " · 共 " + Ui.FormatBytes(total);

            bool today = latest.DateValue.Date == DateTime.Today;
            _cards[3].Title = today ? "今日记录" : "最近记录";
            _cards[3].Value = latest.UpdatedValue.ToString("HH:mm");
            _cards[3].SubText = today
                ? "今天 · 每天自动更新"
                : latest.DateValue.ToString("MM-dd") + " · 自动记录";
        }

        private void RebuildChips(DayRecord latest)
        {
            foreach (DriveChip chip in _chips)
            {
                Controls.Remove(chip);
                chip.Dispose();
            }
            _chips.Clear();

            long totalUsed = 0;
            long totalSize = 0;
            if (latest != null)
            {
                foreach (DriveRecord drive in latest.Drives)
                {
                    totalUsed += drive.Used;
                    totalSize += drive.Total;
                }
            }

            DriveChip totalChip = new DriveChip();
            totalChip.IsTotal = true;
            totalChip.DriveName = "总计";
            totalChip.Caption = "全部本地磁盘";
            totalChip.Used = totalUsed;
            totalChip.Total = totalSize;
            totalChip.DotColor = Theme.Accent;
            totalChip.Selected = _showTotal;
            totalChip.Click += delegate { SelectTotal(); };
            _chips.Add(totalChip);
            Controls.Add(totalChip);

            int shown = 0;
            foreach (DriveRecord drive in _latestDrives)
            {
                if (shown >= 5) break;
                string name = drive.Name;
                DriveChip chip = new DriveChip();
                chip.DriveName = name;
                chip.Caption = drive.Label;
                try
                {
                    string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
                    if (systemRoot != null && string.Equals(name, systemRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        chip.Caption = "系统盘";
                    }
                }
                catch
                {
                }
                chip.Used = drive.Used;
                chip.Total = drive.Total;
                chip.DotColor = DriveColor(name);
                chip.Selected = _selectedDrives.Contains(name);
                chip.Click += delegate { ToggleDrive(name); };
                _chips.Add(chip);
                Controls.Add(chip);
                shown++;
            }

            // Drop selections for drives that disappeared.
            List<string> stale = new List<string>();
            foreach (string name in _selectedDrives)
            {
                bool found = false;
                foreach (DriveRecord drive in _latestDrives)
                {
                    if (string.Equals(drive.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) stale.Add(name);
            }
            foreach (string name in stale) _selectedDrives.Remove(name);

            if (!_showTotal && _selectedDrives.Count == 0) _showTotal = true;
            foreach (DriveChip chip in _chips)
            {
                if (chip.IsTotal)
                {
                    chip.Selected = _showTotal;
                }
                else
                {
                    chip.Selected = _selectedDrives.Contains(chip.DriveName);
                }
            }
        }

        private void SelectTotal()
        {
            if (_showTotal && _selectedDrives.Count == 0)
            {
                SetStatus("至少保留一条曲线");
                return;
            }
            _showTotal = true;
            _selectedDrives.Clear();
            RefreshChipSelection();
            RefreshData(false);
        }

        private void ToggleDrive(string name)
        {
            if (_showTotal)
            {
                _showTotal = false;
                _selectedDrives.Clear();
            }

            if (_selectedDrives.Contains(name)) _selectedDrives.Remove(name);
            else _selectedDrives.Add(name);

            if (_selectedDrives.Count == 0)
            {
                _showTotal = true;
            }

            RefreshChipSelection();
            RefreshData(false);
        }

        private void RefreshChipSelection()
        {
            foreach (DriveChip chip in _chips)
            {
                chip.Selected = chip.IsTotal ? _showTotal : _selectedDrives.Contains(chip.DriveName);
                chip.Invalidate();
            }
        }

        private void UpdateChart(DayRecord latest)
        {
            DateTime end;
            DateTime start;
            if (_rangeDays == 0)
            {
                if (_store.Days.Count == 0)
                {
                    end = DateTime.Today;
                    start = end.AddDays(-29);
                }
                else
                {
                    start = _store.Days[0].DateValue;
                    end = DateTime.Today;
                    if (latest != null && latest.DateValue > end) end = latest.DateValue;
                }
            }
            else
            {
                end = DateTime.Today;
                start = end.AddDays(-(_rangeDays - 1));
            }

            if (end < start) end = start;
            List<DayRecord> days = _store.GetDays(start, end);
            List<ChartSeries> series = new List<ChartSeries>();

            if (_showTotal && days.Count > 0)
            {
                ChartSeries line = new ChartSeries();
                line.Key = "TOTAL";
                line.Name = "全部磁盘";
                line.Color = Theme.Accent;
                foreach (DayRecord day in days)
                {
                    if (day.Drives == null || day.Drives.Count == 0) continue;
                    ChartPoint point = new ChartPoint();
                    point.Date = day.DateValue;
                    point.Used = day.TotalUsed();
                    point.Total = day.TotalSize();
                    line.Points.Add(point);
                }
                if (line.Points.Count > 0) series.Add(line);
            }

            if (!_showTotal && _selectedDrives.Count > 0)
            {
                foreach (DriveRecord drive in _latestDrives)
                {
                    if (!_selectedDrives.Contains(drive.Name)) continue;
                    ChartSeries line = new ChartSeries();
                    line.Key = drive.Name;
                    line.Name = drive.Name;
                    line.Color = DriveColor(drive.Name);
                    foreach (DayRecord day in days)
                    {
                        DriveRecord value = day.FindDrive(drive.Name);
                        if (value == null) continue;
                        ChartPoint point = new ChartPoint();
                        point.Date = day.DateValue;
                        point.Used = value.Used;
                        point.Total = value.Total;
                        line.Points.Add(point);
                    }
                    if (line.Points.Count > 0) series.Add(line);
                }
            }

            if (series.Count == 0 && _showTotal && days.Count > 0)
            {
                // No usable drive data in the range.
            }

            string emptyTitle = "这个时间范围没有记录";
            string emptySub = "试试切换到「全部」，或等待下一次自动记录。";
            if (_store.Days.Count == 0)
            {
                emptyTitle = "还没有趋势数据";
                emptySub = "软件会在后台每天自动记录；也可以点击「立即记录」。";
            }
            else if (series.Count > 0 && CountPoints(series) == 1)
            {
                emptySub = "第一天记录已保存，明天就能看到趋势曲线。";
            }

            DateTime domainEnd = end;
            if (domainEnd <= start) domainEnd = start.AddDays(1);
            _chart.SetData(series, start, domainEnd, emptyTitle, emptySub);
        }

        private static int CountPoints(List<ChartSeries> series)
        {
            int count = 0;
            foreach (ChartSeries item in series) count += item.Points.Count;
            return count;
        }

        private void LayoutUi()
        {
            if (Width <= 20 || Height <= 20) return;
            float s = Ui.S(this, 1f);
            float margin = 28f * s;
            float contentW = Width - margin * 2f;
            float titleH = 58f * s;

            _footerY = Height - (int)Math.Round(52f * s);
            float cardsTop = 154f * s;
            float cardGap = 14f * s;
            float cardH = 104f * s;
            float cardW = (contentW - cardGap * 3f) / 4f;
            for (int i = 0; i < _cards.Length; i++)
            {
                _cards[i].Bounds = new Rectangle(
                    (int)Math.Round(margin + i * (cardW + cardGap)),
                    (int)Math.Round(cardsTop),
                    (int)Math.Round(cardW),
                    (int)Math.Round(cardH));
            }

            int rangeW = (int)Math.Round(292f * s);
            _range.Bounds = new Rectangle(
                (int)Math.Round(Width - margin - rangeW),
                (int)Math.Round(82f * s),
                rangeW,
                (int)Math.Round(38f * s));

            float chipsTop = 276f * s;
            float chipH = 58f * s;
            float buttonY = chipsTop + (chipH - 36f * s) / 2f;
            float recordW = 96f * s;
            float exportW = 88f * s;
            float folderW = 96f * s;
            float buttonGap = 8f * s;
            float folderX = Width - margin - folderW;
            float exportX = folderX - buttonGap - exportW;
            float recordX = exportX - buttonGap - recordW;
            _folderButton.Bounds = new Rectangle((int)Math.Round(folderX), (int)Math.Round(buttonY),
                (int)Math.Round(folderW), (int)Math.Round(36f * s));
            _exportButton.Bounds = new Rectangle((int)Math.Round(exportX), (int)Math.Round(buttonY),
                (int)Math.Round(exportW), (int)Math.Round(36f * s));
            _recordButton.Bounds = new Rectangle((int)Math.Round(recordX), (int)Math.Round(buttonY),
                (int)Math.Round(recordW), (int)Math.Round(36f * s));

            float chipsRight = recordX - 16f * s;
            int chipCount = _chips.Count;
            if (chipCount > 0)
            {
                float chipGap = 9f * s;
                float chipW = (chipsRight - margin - chipGap * (chipCount - 1)) / chipCount;
                if (chipW > 200f * s) chipW = 200f * s;
                if (chipW < 96f * s) chipW = 96f * s;
                for (int i = 0; i < chipCount; i++)
                {
                    _chips[i].Bounds = new Rectangle(
                        (int)Math.Round(margin + i * (chipW + chipGap)),
                        (int)Math.Round(chipsTop),
                        (int)Math.Round(chipW),
                        (int)Math.Round(chipH));
                }
            }

            float chartTop = 352f * s;
            float chartBottom = _footerY - 16f * s;
            _chart.Bounds = new Rectangle(
                (int)Math.Round(margin),
                (int)Math.Round(chartTop),
                (int)Math.Round(contentW),
                (int)Math.Round(Math.Max(120f * s, chartBottom - chartTop)));

            _autoToggle.Bounds = new Rectangle(
                (int)Math.Round(margin),
                _footerY + (int)Math.Round(14f * s),
                (int)Math.Round(40f * s),
                (int)Math.Round(23f * s));
            _autoLabel.Bounds = new Rectangle(
                (int)Math.Round(margin + 49f * s),
                _footerY + (int)Math.Round(10f * s),
                (int)Math.Round(124f * s),
                (int)Math.Round(30f * s));

            float closeW = 38f * s;
            float closeH = 34f * s;
            float closeX = Width - 16f * s - closeW;
            _closeButton.Bounds = new Rectangle((int)Math.Round(closeX), (int)Math.Round(12f * s),
                (int)Math.Round(closeW), (int)Math.Round(closeH));
            _maxButton.Bounds = new Rectangle((int)Math.Round(closeX - 42f * s), (int)Math.Round(12f * s),
                (int)Math.Round(closeW), (int)Math.Round(closeH));
            _minButton.Bounds = new Rectangle((int)Math.Round(closeX - 84f * s), (int)Math.Round(12f * s),
                (int)Math.Round(closeW), (int)Math.Round(closeH));

            // Keep the title bar buttons above the content.
            _minButton.BringToFront();
            _maxButton.BringToFront();
            _closeButton.BringToFront();
        }

        private static int RangeIndex(int days)
        {
            if (days == 7) return 0;
            if (days == 90) return 2;
            if (days == 0) return 3;
            return 1;
        }

        private static int RangeFromIndex(int index)
        {
            if (index == 0) return 7;
            if (index == 2) return 90;
            if (index == 3) return 0;
            return 30;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = Ui.S(this, 1f);
            using (SolidBrush background = new SolidBrush(Theme.Base))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            float titleH = 58f * s;
            using (Pen line = new Pen(Theme.BorderSoft, 1f * s))
            {
                g.DrawLine(line, 0, titleH, Width, titleH);
            }

            // Logo mark
            RectangleF logo = new RectangleF(26f * s, 14f * s, 30f * s, 30f * s);
            using (LinearGradientBrush brush = new LinearGradientBrush(logo, Theme.Accent, Theme.Cyan, 45f))
            {
                Ui.FillRound(g, logo, 9f * s, brush);
            }
            using (Pen pulse = new Pen(Color.White, 1.8f * s))
            {
                pulse.StartCap = LineCap.Round;
                pulse.EndCap = LineCap.Round;
                pulse.LineJoin = LineJoin.Round;
                PointF[] points = new PointF[]
                {
                    new PointF(logo.X + 7f * s, logo.Y + 17f * s),
                    new PointF(logo.X + 11f * s, logo.Y + 17f * s),
                    new PointF(logo.X + 14f * s, logo.Y + 10f * s),
                    new PointF(logo.X + 18f * s, logo.Y + 23f * s),
                    new PointF(logo.X + 21f * s, logo.Y + 17f * s),
                    new PointF(logo.X + 24f * s, logo.Y + 17f * s)
                };
                g.DrawLines(pulse, points);
            }

            Ui.DrawText(g, "DiskPulse", Theme.NumberFont(12.5f, FontStyle.Bold), Theme.Text,
                new RectangleF(66f * s, 9f * s, 300f * s, 24f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Ui.DrawText(g, "磁盘存储记录  ·  v" + AppPaths.Version, Theme.Font(7.7f, FontStyle.Regular), Theme.Muted,
                new RectangleF(67f * s, 31f * s, 300f * s, 18f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            Ui.DrawText(g, "存储趋势", Theme.Font(20f, FontStyle.Bold), Theme.Text,
                new RectangleF(28f * s, 76f * s, 420f * s, 36f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Ui.DrawText(g, "每天自动记录磁盘已用空间，长期观察增长与释放", Theme.Font(8.9f, FontStyle.Regular), Theme.TextSecondary,
                new RectangleF(30f * s, 110f * s, 540f * s, 22f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            // Footer
            using (Pen line = new Pen(Theme.BorderSoft, 1f * s))
            {
                g.DrawLine(line, 0, _footerY, Width, _footerY);
            }

            float statusX = 224f * s;
            using (Pen separator = new Pen(Theme.BorderSoft, 1f * s))
            {
                g.DrawLine(separator, statusX - 16f * s, _footerY + 14f * s, statusX - 16f * s, _footerY + 38f * s);
            }
            bool activeStatus = _status.Length > 0 && DateTime.Now < _statusUntil;
            Color dotColor = activeStatus && _statusError ? Theme.Red : Theme.Green;
            float dotY = _footerY + 24f * s;
            using (SolidBrush halo = new SolidBrush(Theme.Alpha(dotColor, 36)))
            {
                g.FillEllipse(halo, statusX - 3f * s, dotY - 3f * s, 12f * s, 12f * s);
            }
            using (SolidBrush dot = new SolidBrush(dotColor))
            {
                g.FillEllipse(dot, statusX + 0.5f * s, dotY + 0.5f * s, 5f * s, 5f * s);
            }

            string statusText;
            if (activeStatus) statusText = _status;
            else
            {
                string last = _sampler.LastRecordTime == DateTime.MinValue
                    ? "等待首次记录"
                    : "上次记录 " + _sampler.LastRecordTime.ToString("HH:mm");
                statusText = "后台记录中 · " + last;
            }
            Color statusTextColor = activeStatus && _statusError ? Theme.Red : Theme.TextSecondary;
            float rightReserve = 460f * s;
            Ui.DrawText(g, statusText, Theme.Font(8.4f, FontStyle.Regular), statusTextColor,
                new RectangleF(statusX + 13f * s, _footerY + 13f * s,
                    Math.Max(80f, Width - statusX - 13f * s - rightReserve), 30f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            Ui.DrawTextRight(g, "数据目录  " + Ui.ShortPath(AppPaths.Root, 52),
                Theme.NumberFont(7.7f, FontStyle.Regular), Theme.Faint,
                new RectangleF(Width - 28f * s - 400f * s, _footerY + 13f * s, 400f * s, 30f * s));

            if (WindowState != FormWindowState.Maximized)
            {
                using (Pen border = new Pen(Theme.Border, 1f * s))
                {
                    g.DrawRectangle(border, 0.5f * s, 0.5f * s, Width - 1f * s, Height - 1f * s);
                }
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void HideToTray()
        {
            if (!Visible) return;
            Hide();
            ShowInTaskbar = false;
            if (!_settings.TrayHintShown)
            {
                _settings.TrayHintShown = true;
                _settings.Save();
                try
                {
                    _tray.ShowBalloonTip(2800, "DiskPulse 仍在后台记录",
                        "双击托盘图标可以重新打开主界面；右键可退出。", ToolTipIcon.Info);
                }
                catch
                {
                }
            }
        }

        public void ShowMainWindow(bool focus)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(ShowMainWindow), focus);
                return;
            }
            if (_exiting) return;
            ShowInTaskbar = true;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            RefreshData(true);
            if (_autoStartWarning && !_autoStartWarningShown)
            {
                _autoStartWarningShown = true;
                if (_settings.AutoStart && !AutoStart.IsCurrentCommand())
                {
                    if (RuntimeInfo.IsRestrictedProcess() || AppPaths.IsPortableFallback)
                    {
                        SetStatus("受限运行：请从资源管理器双击 启用开机自启.cmd", true);
                    }
                    else
                    {
                        SetStatus("无法写入开机启动项，请从资源管理器直接运行一次", true);
                    }
                }
                else
                {
                    SetStatus("开机启动项未能同步，请检查权限后重试", true);
                }
            }
            if (focus)
            {
                Activate();
                BringToFront();
            }
            Invalidate();
        }

        private void ExitApplication()
        {
            _exiting = true;
            try { _tray.Visible = false; }
            catch { }
            _recordTimer.Stop();
            _statusTimer.Stop();
            Close();
        }

        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                _maxButton.IsMaximized = false;
            }
            else
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
                _maxButton.IsMaximized = true;
            }
            _maxButton.Invalidate();
            LayoutUi();
        }

        private void OnTitleMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            float s = Ui.S(this, 1f);
            if (e.Y < 58f * s && WindowState == FormWindowState.Normal)
            {
                _dragging = true;
                _dragStart = e.Location;
                Capture = true;
            }
        }

        private void OnTitleMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point screen = PointToScreen(e.Location);
            Location = new Point(screen.X - _dragStart.X, screen.Y - _dragStart.Y);
        }

        private void OnTitleMouseUp(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Capture = false;
        }

        private void OnTitleDoubleClick(object sender, MouseEventArgs e)
        {
            float s = Ui.S(this, 1f);
            if (e.Y < 58f * s && e.Button == MouseButtons.Left)
            {
                ToggleMaximize();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmNcHitTest)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HtClient && WindowState == FormWindowState.Normal)
                {
                    int screenX = (short)((long)m.LParam & 0xFFFF);
                    int screenY = (short)(((long)m.LParam >> 16) & 0xFFFF);
                    Point p = PointToClient(new Point(screenX, screenY));
                    float border = Ui.S(this, 5f);
                    bool left = p.X <= border;
                    bool right = p.X >= ClientSize.Width - border;
                    bool top = p.Y <= border;
                    bool bottom = p.Y >= ClientSize.Height - border;
                    if (left && top) m.Result = (IntPtr)HtTopLeft;
                    else if (right && top) m.Result = (IntPtr)HtTopRight;
                    else if (left && bottom) m.Result = (IntPtr)HtBottomLeft;
                    else if (right && bottom) m.Result = (IntPtr)HtBottomRight;
                    else if (left) m.Result = (IntPtr)HtLeft;
                    else if (right) m.Result = (IntPtr)HtRight;
                    else if (top) m.Result = (IntPtr)HtTop;
                    else if (bottom) m.Result = (IntPtr)HtBottom;
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                DoRecord("快捷键");
                return true;
            }
            if (keyData == (Keys.Control | Keys.E))
            {
                ExportCsv();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                HideToTray();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Renderer = new DarkMenuRenderer();
            _trayMenu.Font = Theme.Font(9f, FontStyle.Regular);
            _trayMenu.BackColor = Color.FromArgb(17, 26, 43);
            _trayMenu.ForeColor = Theme.Text;
            _trayMenu.ShowImageMargin = false;
            _trayMenu.Padding = new Padding(4, 6, 4, 6);

            ToolStripMenuItem openItem = new ToolStripMenuItem("打开主界面");
            openItem.Font = Theme.Font(9.2f, FontStyle.Bold);
            openItem.Click += delegate { ShowMainWindow(true); };
            _trayMenu.Items.Add(openItem);

            ToolStripMenuItem recordItem = new ToolStripMenuItem("立即记录");
            recordItem.Click += delegate { DoRecord("托盘"); };
            _trayMenu.Items.Add(recordItem);

            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayAutoItem = new ToolStripMenuItem("开机自动记录");
            _trayAutoItem.CheckOnClick = false;
            _trayAutoItem.Click += delegate { SetAutoStart(!AutoStart.IsCurrentCommand()); };
            _trayMenu.Items.Add(_trayAutoItem);

            ToolStripMenuItem folderItem = new ToolStripMenuItem("打开数据目录");
            folderItem.Click += delegate { OpenDataFolder(); };
            _trayMenu.Items.Add(folderItem);

            _trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出 DiskPulse");
            exitItem.Click += delegate { ExitApplication(); };
            _trayMenu.Items.Add(exitItem);

            _trayMenu.Opening += delegate { _trayAutoItem.Checked = AutoStart.IsCurrentCommand(); };

            _tray = new NotifyIcon();
            _tray.Icon = _trayIcon;
            _tray.Text = "DiskPulse 磁盘存储记录";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _trayMenu;
            _tray.DoubleClick += delegate { ShowMainWindow(true); };
        }

        private void SetAutoStart(bool enabled)
        {
            if (_autoStartChanging) return;
            _autoStartChanging = true;
            try
            {
                bool ok = AutoStart.SetEnabled(enabled);
                if (ok)
                {
                    _settings.AutoStart = enabled;
                    _settings.Save();
                    _autoToggle.SetCheckedSilently(enabled);
                    _trayAutoItem.Checked = enabled;
                    _autoStartWarning = false;
                    SetStatus(enabled ? "已开启开机自动记录" : "已关闭开机自动记录");
                }
                else
                {
                    _autoToggle.SetCheckedSilently(AutoStart.IsCurrentCommand());
                    _trayAutoItem.Checked = AutoStart.IsCurrentCommand();
                    _autoStartWarning = true;
                    if (RuntimeInfo.IsRestrictedProcess() || AppPaths.IsPortableFallback)
                    {
                        SetStatus("受限运行：请从资源管理器双击 启用开机自启.cmd", true);
                    }
                    else
                    {
                        SetStatus("无法修改开机启动项，请检查权限", true);
                    }
                }
            }
            finally
            {
                _autoStartChanging = false;
            }
        }

        private void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                ProcessStartInfo info = new ProcessStartInfo("explorer.exe", "\"" + AppPaths.Root + "\"");
                info.UseShellExecute = true;
                Process.Start(info);
                SetStatus("已打开数据目录");
            }
            catch (Exception ex)
            {
                Logger.Error("打开数据目录失败", ex);
                SetStatus("无法打开数据目录", true);
            }
        }

        private void ExportCsv()
        {
            try
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "导出磁盘历史记录";
                    dialog.Filter = "CSV 文件 (*.csv)|*.csv";
                    dialog.DefaultExt = "csv";
                    dialog.AddExtension = true;
                    dialog.FileName = "DiskPulse-" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
                    try
                    {
                        dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    }
                    catch
                    {
                    }
                    if (dialog.ShowDialog() != DialogResult.OK) return;

                    int rows = CsvExporter.Export(_store, dialog.FileName);
                    SetStatus("已导出 " + rows + " 条记录 · " + Path.GetFileName(dialog.FileName));
                }
            }
            catch (Exception ex)
            {
                Logger.Error("导出 CSV 失败", ex);
                SetStatus("导出失败，请查看日志", true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_recordTimer != null) _recordTimer.Dispose();
                if (_statusTimer != null) _statusTimer.Dispose();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                if (_trayMenu != null) _trayMenu.Dispose();
                if (_toolTip != null) _toolTip.Dispose();
                if (_trayIcon != null) _trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
