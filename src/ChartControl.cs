using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace DiskPulse
{
    internal sealed class ChartPoint
    {
        public DateTime Date;
        public long Used;
        public long Total;
    }

    internal sealed class ChartSeries
    {
        public string Key = "";
        public string Name = "";
        public Color Color = Theme.Accent;
        public List<ChartPoint> Points = new List<ChartPoint>();
    }

    internal sealed class ChartControl : Control
    {
        private List<ChartSeries> _series = new List<ChartSeries>();
        private DateTime _start = DateTime.Today;
        private DateTime _end = DateTime.Today.AddDays(1);
        private string _emptyTitle = "暂无趋势数据";
        private string _emptySub = "保持软件在后台运行，每天会自动记录一次。";
        private DateTime? _hoverDate;
        private RectangleF _plot;
        private float _xPadding;

        public ChartControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
        }

        public void SetData(List<ChartSeries> series, DateTime start, DateTime end, string emptyTitle, string emptySub)
        {
            _series = series ?? new List<ChartSeries>();
            _start = start.Date;
            _end = end.Date;
            if (_end <= _start) _end = _start.AddDays(1);
            _emptyTitle = emptyTitle;
            _emptySub = emptySub;
            _hoverDate = null;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_plot.Contains(e.Location))
            {
                double span = Math.Max(1d, (_end - _start).TotalDays);
                double ratio = (e.X - _plot.Left) / Math.Max(1f, _plot.Width);
                if (ratio < 0) ratio = 0;
                if (ratio > 1) ratio = 1;
                DateTime candidate = _start.AddDays(ratio * span).Date;
                _hoverDate = FindNearestDate(candidate);
                Invalidate();
            }
            else if (_hoverDate.HasValue)
            {
                _hoverDate = null;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverDate.HasValue)
            {
                _hoverDate = null;
                Invalidate();
            }
        }

        private DateTime? FindNearestDate(DateTime candidate)
        {
            DateTime? best = null;
            int bestDistance = int.MaxValue;
            foreach (ChartSeries series in _series)
            {
                foreach (ChartPoint point in series.Points)
                {
                    int distance = Math.Abs((point.Date.Date - candidate.Date).Days);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = point.Date.Date;
                    }
                }
            }
            return best;
        }

        private bool HasAnyPoints()
        {
            foreach (ChartSeries series in _series)
            {
                if (series.Points != null && series.Points.Count > 0) return true;
            }
            return false;
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

            RectangleF card = new RectangleF(0.5f * s, 0.5f * s, Width - 1f * s, Height - 1f * s);
            using (LinearGradientBrush bg = new LinearGradientBrush(
                card.Height <= 0 ? new RectangleF(card.X, card.Y, card.Width, 1) : card,
                Theme.Card, Color.FromArgb(14, 22, 38), 90f))
            {
                Ui.FillRound(g, card, 16f * s, bg);
            }
            Ui.DrawRound(g, card, 16f * s, Theme.BorderSoft, 1f * s);

            float pad = 24f * s;
            Ui.DrawText(g, "存储使用趋势", Theme.Font(11.5f, FontStyle.Bold), Theme.Text,
                new RectangleF(pad, 15f * s, 320f * s, 24f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            Ui.DrawText(g, "已用空间 · 每天自动采样", Theme.Font(8.2f, FontStyle.Regular), Theme.Muted,
                new RectangleF(pad, 39f * s, 360f * s, 18f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            DrawLegend(g, card, s);

            if (!HasAnyPoints())
            {
                DrawEmpty(g, card, s);
                return;
            }

            float plotTop = 82f * s;
            float plotBottom = Height - 42f * s;
            if (plotBottom - plotTop < 40f * s)
            {
                plotTop = 78f * s;
                plotBottom = Height - 30f * s;
            }

            long minValue;
            long maxValue;
            GetValueBounds(out minValue, out maxValue);
            double rawSpan = Math.Max(1d, (double)maxValue - minValue);
            double padValue = rawSpan * 0.08d;
            double low = Math.Max(0d, (double)minValue - padValue);
            double high = (double)maxValue + padValue;
            if (high <= low) high = low + 1024d * 1024d * 1024d;

            // Disk values are naturally expressed in binary units. Keep the
            // step on a whole-GiB boundary so axis labels stay round.
            double unit = 1024d * 1024d * 1024d;
            double stepUnits = NiceStep(((high - low) / 4d) / unit);
            if (stepUnits < 1d) stepUnits = 1d;
            double step = stepUnits * unit;
            double axisMin = Math.Floor(low / step) * step;
            double axisMax = Math.Ceiling(high / step) * step;
            if (axisMax <= axisMin) axisMax = axisMin + step;
            if ((axisMax - axisMin) / step > 8d) axisMax = axisMin + step * 8d;
            StoreAxis(axisMin, axisMax);

            // Reserve room for the widest Y label.
            Font tickFont = Theme.NumberFont(7.7f, FontStyle.Regular);
            float labelWidth = 0f;
            for (double v = axisMin; v <= axisMax + step * 0.1d; v += step)
            {
                int w = TextRenderer.MeasureText(Ui.FormatBytes((long)Math.Max(0d, v)), tickFont,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                if (w > labelWidth) labelWidth = w;
            }
            float plotLeft = pad + labelWidth + 14f * s;
            float plotRight = Width - pad;
            if (plotRight - plotLeft < 120f * s) plotLeft = Math.Max(pad + 30f * s, plotRight - 120f * s);
            _plot = new RectangleF(plotLeft, plotTop, Math.Max(20f, plotRight - plotLeft), Math.Max(20f, plotBottom - plotTop));
            _xPadding = Ui.S(this, 7f);

            DrawGridAndAxis(g, _plot, axisMin, axisMax, step, tickFont, s);
            DrawXAxis(g, _plot, s);

            g.SetClip(_plot);
            if (_series.Count == 1) DrawArea(g, _plot, _series[0], s);
            foreach (ChartSeries series in _series)
            {
                DrawSeries(g, _plot, series, s);
            }
            g.ResetClip();

            if (CountPoints() == 1)
            {
                DrawSinglePointHint(g, _plot, s);
            }

            if (_hoverDate.HasValue) DrawHover(g, _plot, s);
        }

        private int CountPoints()
        {
            int count = 0;
            foreach (ChartSeries series in _series)
            {
                if (series.Points != null) count += series.Points.Count;
            }
            return count;
        }

        private void DrawSinglePointHint(Graphics g, RectangleF plot, float s)
        {
            string text = "第一天记录已保存 · 明天就能看到趋势";
            Font font = Theme.Font(8.4f, FontStyle.Regular);
            Size size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
            float width = size.Width + 28f * s;
            float height = 29f * s;
            RectangleF rect = new RectangleF(plot.Left + (plot.Width - width) / 2f, plot.Top + 14f * s, width, height);
            Ui.FillRound(g, rect, 8f * s, Theme.Alpha(Theme.CardRaised, 220));
            Ui.DrawRound(g, rect, 8f * s, Theme.Alpha(Theme.Accent, 70), 1f * s);
            Ui.DrawText(g, text, font, Theme.TextSecondary,
                new RectangleF(rect.X + 14f * s, rect.Y, rect.Width - 28f * s, rect.Height),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        private void GetValueBounds(out long minValue, out long maxValue)
        {
            minValue = long.MaxValue;
            maxValue = long.MinValue;
            foreach (ChartSeries series in _series)
            {
                foreach (ChartPoint point in series.Points)
                {
                    if (point.Used < minValue) minValue = point.Used;
                    if (point.Used > maxValue) maxValue = point.Used;
                }
            }
            if (minValue == long.MaxValue)
            {
                minValue = 0;
                maxValue = 1024L * 1024L * 1024L;
            }
            if (maxValue <= minValue) maxValue = minValue + 1024L * 1024L * 1024L;
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw)) return 1d;
            double exponent = Math.Floor(Math.Log10(raw));
            double fraction = raw / Math.Pow(10d, exponent);
            double nice;
            if (fraction <= 1d) nice = 1d;
            else if (fraction <= 2d) nice = 2d;
            else if (fraction <= 5d) nice = 5d;
            else nice = 10d;
            return nice * Math.Pow(10d, exponent);
        }

        private void DrawGridAndAxis(Graphics g, RectangleF plot, double axisMin, double axisMax, double step,
            Font tickFont, float s)
        {
            int ticks = (int)Math.Round((axisMax - axisMin) / step);
            if (ticks < 1) ticks = 1;
            if (ticks > 8) ticks = 8;
            using (Pen grid = new Pen(Theme.Grid, 1f * s))
            {
                grid.DashStyle = DashStyle.Dot;
                for (int i = 0; i <= ticks; i++)
                {
                    double value = axisMin + step * i;
                    float y = ValueToY(plot, value, axisMin, axisMax);
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    string text = Ui.FormatBytes((long)Math.Max(0d, value));
                    Ui.DrawText(g, text, tickFont, Theme.Faint,
                        new RectangleF(plot.Left - 14f * s - 100f * s, y - 9f * s, 100f * s, 18f * s),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
            }

            using (Pen axis = new Pen(Theme.Alpha(Theme.Border, 170), 1f * s))
            {
                g.DrawLine(axis, plot.Left, plot.Top, plot.Left, plot.Bottom);
            }
        }

        private void DrawXAxis(Graphics g, RectangleF plot, float s)
        {
            double spanDays = Math.Max(1d, (_end - _start).TotalDays);
            int interval = ChooseDayInterval(spanDays, plot.Width);
            Font font = Theme.NumberFont(7.6f, FontStyle.Regular);
            float lastLabelX = float.MinValue;
            DateTime first = _start;
            for (DateTime date = first; date <= _end.AddDays(0.1); date = date.AddDays(interval))
            {
                float x = DateToX(plot, date);
                if (x < plot.Left - 1f || x > plot.Right + 1f) continue;
                if (x - lastLabelX < 48f * s) continue;

                using (Pen tick = new Pen(Theme.Alpha(Theme.Border, 170), 1f * s))
                {
                    g.DrawLine(tick, x, plot.Bottom, x, plot.Bottom + 4f * s);
                }

                string label;
                if (spanDays > 730d) label = date.ToString("yyyy/M", CultureInfo.InvariantCulture);
                else if (interval >= 30) label = date.ToString("yyyy/M", CultureInfo.InvariantCulture);
                else if (date.Year != _end.Year) label = date.ToString("yy/M/d", CultureInfo.InvariantCulture);
                else label = date.ToString("M/d", CultureInfo.InvariantCulture);

                Size size = TextRenderer.MeasureText(label, font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding);
                float tx = x - size.Width / 2f;
                if (tx < plot.Left) tx = plot.Left;
                if (tx + size.Width > plot.Right) tx = plot.Right - size.Width;
                Ui.DrawText(g, label, font, Theme.Muted,
                    new RectangleF(tx, plot.Bottom + 8f * s, size.Width + 2f * s, 18f * s),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                lastLabelX = x;
            }
        }

        private static int ChooseDayInterval(double spanDays, float plotWidth)
        {
            int[] candidates = new int[] { 1, 2, 3, 5, 7, 14, 21, 30, 60, 90, 120, 180, 365, 730, 1460, 2920 };
            foreach (int value in candidates)
            {
                if (plotWidth * value / Math.Max(1d, spanDays) >= 66d) return value;
            }
            return 3650;
        }

        private void DrawArea(Graphics g, RectangleF plot, ChartSeries series, float s)
        {
            List<ChartPoint> points = series.Points;
            if (points == null || points.Count < 2) return;
            List<PointF> pixels = new List<PointF>();
            foreach (ChartPoint point in points)
            {
                pixels.Add(new PointF(DateToX(plot, point.Date), ValueToY(plot, point.Used, _axisMinCache, _axisMaxCache)));
            }

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddLines(pixels.ToArray());
                path.AddLine(pixels[pixels.Count - 1].X, plot.Bottom, pixels[0].X, plot.Bottom);
                path.CloseFigure();
                RectangleF area = new RectangleF(plot.Left, plot.Top, plot.Width, plot.Height);
                if (area.Height <= 0) return;
                using (LinearGradientBrush brush = new LinearGradientBrush(area,
                    Theme.Alpha(series.Color, 64), Theme.Alpha(series.Color, 2), 90f))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        private double _axisMinCache;
        private double _axisMaxCache;

        private void DrawSeries(Graphics g, RectangleF plot, ChartSeries series, float s)
        {
            List<ChartPoint> points = series.Points;
            if (points == null || points.Count == 0) return;

            // The axis bounds are cached during OnPaint before this is called.
            List<PointF> pixels = new List<PointF>();
            foreach (ChartPoint point in points)
            {
                pixels.Add(new PointF(DateToX(plot, point.Date), ValueToY(plot, point.Used, _axisMinCache, _axisMaxCache)));
            }

            if (pixels.Count >= 2)
            {
                using (Pen glow = new Pen(Theme.Alpha(series.Color, 34), 6f * s))
                {
                    glow.LineJoin = LineJoin.Round;
                    glow.StartCap = LineCap.Round;
                    glow.EndCap = LineCap.Round;
                    g.DrawLines(glow, pixels.ToArray());
                }
                using (Pen pen = new Pen(series.Color, 2.1f * s))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, pixels.ToArray());
                }
            }

            bool showDots = pixels.Count <= 62;
            for (int i = 0; i < pixels.Count; i++)
            {
                if (!showDots && i != pixels.Count - 1) continue;
                float radius = (i == pixels.Count - 1 ? 3.4f : 2.6f) * s;
                using (SolidBrush halo = new SolidBrush(Theme.Alpha(series.Color, 42)))
                {
                    if (i == pixels.Count - 1)
                    {
                        g.FillEllipse(halo, pixels[i].X - radius * 2.4f, pixels[i].Y - radius * 2.4f,
                            radius * 4.8f, radius * 4.8f);
                    }
                }
                using (SolidBrush fill = new SolidBrush(i == pixels.Count - 1 ? series.Color : Theme.Card))
                using (Pen border = new Pen(series.Color, 1.6f * s))
                {
                    g.FillEllipse(fill, pixels[i].X - radius, pixels[i].Y - radius, radius * 2f, radius * 2f);
                    g.DrawEllipse(border, pixels[i].X - radius, pixels[i].Y - radius, radius * 2f, radius * 2f);
                }
            }
        }

        private void DrawLegend(Graphics g, RectangleF card, float s)
        {
            if (_series.Count == 0) return;
            Font font = Theme.Font(8f, FontStyle.Regular);
            int maxItems = 4;
            List<ChartSeries> items = new List<ChartSeries>();
            for (int i = 0; i < _series.Count && i < maxItems; i++) items.Add(_series[i]);
            int extra = Math.Max(0, _series.Count - maxItems);

            float[] widths = new float[items.Count];
            float total = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                widths[i] = 13f * s + TextRenderer.MeasureText(items[i].Name, font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding).Width + 14f * s;
                total += widths[i];
            }
            if (extra > 0)
            {
                total += 30f * s;
            }

            float startX = card.Right - 24f * s - total;
            if (startX < card.Left + 360f * s)
            {
                // Too many drives for the top row: compact the legend and clip at the edge.
                startX = card.Left + 360f * s;
            }

            float y = 25f * s;
            for (int i = 0; i < items.Count; i++)
            {
                using (SolidBrush dot = new SolidBrush(items[i].Color))
                {
                    g.FillEllipse(dot, startX, y + 5f * s, 7f * s, 7f * s);
                }
                Ui.DrawText(g, items[i].Name, font, Theme.TextSecondary,
                    new RectangleF(startX + 11f * s, y, widths[i] - 11f * s, 18f * s),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                startX += widths[i];
            }
            if (extra > 0)
            {
                Ui.DrawText(g, "+" + extra, font, Theme.Muted,
                    new RectangleF(startX, y, 30f * s, 18f * s),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        private void DrawEmpty(Graphics g, RectangleF card, float s)
        {
            float cx = card.Left + card.Width / 2f;
            float cy = card.Top + card.Height / 2f - 8f * s;
            float r = 27f * s;
            using (SolidBrush halo = new SolidBrush(Theme.Alpha(Theme.Accent, 18)))
            {
                g.FillEllipse(halo, cx - r, cy - r, r * 2f, r * 2f);
            }
            using (Pen pen = new Pen(Theme.Alpha(Theme.AccentLight, 190), 1.8f * s))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                RectangleF body = new RectangleF(cx - 13f * s, cy - 8f * s, 26f * s, 16f * s);
                using (GraphicsPath path = Ui.RoundRect(body, 4f * s))
                {
                    g.DrawPath(pen, path);
                }
                g.DrawLine(pen, cx - 11f * s, cy + 1f * s, cx + 11f * s, cy + 1f * s);
                using (SolidBrush dot = new SolidBrush(Theme.AccentLight))
                {
                    g.FillEllipse(dot, cx - 9f * s, cy + 4.5f * s, 2.5f * s, 2.5f * s);
                }
            }

            Font titleFont = Theme.Font(11f, FontStyle.Bold);
            Font subFont = Theme.Font(8.6f, FontStyle.Regular);
            Ui.DrawText(g, _emptyTitle, titleFont, Theme.Text,
                new RectangleF(cx - 300f * s, cy + r + 8f * s, 600f * s, 24f * s),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Ui.DrawText(g, _emptySub, subFont, Theme.Muted,
                new RectangleF(cx - 320f * s, cy + r + 32f * s, 640f * s, 20f * s),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawHover(Graphics g, RectangleF plot, float s)
        {
            DateTime date = _hoverDate.Value.Date;
            float x = DateToX(plot, date);
            if (x < plot.Left || x > plot.Right) return;

            using (Pen line = new Pen(Theme.Alpha(Theme.AccentLight, 105), 1f * s))
            {
                line.DashStyle = DashStyle.Dash;
                g.DrawLine(line, x, plot.Top, x, plot.Bottom);
            }

            List<ChartSeries> rows = new List<ChartSeries>();
            List<ChartPoint> rowPoints = new List<ChartPoint>();
            foreach (ChartSeries series in _series)
            {
                foreach (ChartPoint point in series.Points)
                {
                    if (point.Date.Date == date)
                    {
                        rows.Add(series);
                        rowPoints.Add(point);
                        float px = DateToX(plot, point.Date);
                        float py = ValueToY(plot, point.Used, _axisMinCache, _axisMaxCache);
                        using (SolidBrush halo = new SolidBrush(Theme.Alpha(series.Color, 65)))
                        {
                            g.FillEllipse(halo, px - 7f * s, py - 7f * s, 14f * s, 14f * s);
                        }
                        using (SolidBrush fill = new SolidBrush(series.Color))
                        using (Pen border = new Pen(Theme.Base, 1.5f * s))
                        {
                            g.FillEllipse(fill, px - 4f * s, py - 4f * s, 8f * s, 8f * s);
                            g.DrawEllipse(border, px - 4f * s, py - 4f * s, 8f * s, 8f * s);
                        }
                        break;
                    }
                }
            }

            if (rows.Count == 0) return;

            float width = 228f * s;
            float rowHeight = 21f * s;
            float height = 40f * s + rows.Count * rowHeight + (rows.Count == 1 ? 21f * s : 8f * s);
            float tx = x + 16f * s;
            if (tx + width > plot.Right - 2f * s) tx = x - width - 16f * s;
            if (tx < plot.Left + 2f * s) tx = plot.Left + 2f * s;
            float ty = plot.Top + 14f * s;
            if (ty + height > plot.Bottom - 2f * s) ty = plot.Bottom - height - 2f * s;

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            {
                Ui.FillRound(g, new RectangleF(tx + 3f * s, ty + 4f * s, width, height), 10f * s, shadow);
            }
            Ui.FillRound(g, new RectangleF(tx, ty, width, height), 10f * s, Color.FromArgb(15, 23, 39));
            Ui.DrawRound(g, new RectangleF(tx, ty, width, height), 10f * s, Color.FromArgb(45, 65, 96), 1f * s);

            string[] weekdays = new string[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            string dateText = date.ToString("yyyy年M月d日", CultureInfo.InvariantCulture) + " " + weekdays[(int)date.DayOfWeek];
            Ui.DrawText(g, dateText, Theme.Font(8.8f, FontStyle.Bold), Theme.Text,
                new RectangleF(tx + 14f * s, ty + 9f * s, width - 28f * s, 20f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            using (Pen divider = new Pen(Theme.Alpha(Theme.Border, 170), 1f * s))
            {
                g.DrawLine(divider, tx + 14f * s, ty + 34f * s, tx + width - 14f * s, ty + 34f * s);
            }

            float rowY = ty + 40f * s;
            for (int i = 0; i < rows.Count; i++)
            {
                using (SolidBrush dot = new SolidBrush(rows[i].Color))
                {
                    g.FillEllipse(dot, tx + 14f * s, rowY + 7f * s, 6f * s, 6f * s);
                }
                Ui.DrawText(g, rows[i].Name, Theme.Font(8.2f, FontStyle.Regular), Theme.TextSecondary,
                    new RectangleF(tx + 26f * s, rowY, width - 110f * s, rowHeight),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                Ui.DrawText(g, Ui.FormatBytes(rowPoints[i].Used), Theme.NumberFont(8.6f, FontStyle.Bold), Theme.Text,
                    new RectangleF(tx + width - 105f * s, rowY, 91f * s, rowHeight),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                rowY += rowHeight;
            }

            if (rows.Count == 1)
            {
                Ui.DrawText(g, "可用 " + Ui.FormatBytes(Math.Max(0, rowPoints[0].Total - rowPoints[0].Used)),
                    Theme.NumberFont(7.8f, FontStyle.Regular), Theme.Muted,
                    new RectangleF(tx + 14f * s, rowY + 1f * s, width - 28f * s, 18f * s),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        private float DateToX(RectangleF plot, DateTime date)
        {
            double span = Math.Max(1d, (_end - _start).TotalDays);
            double offset = (date.Date - _start.Date).TotalDays;
            float width = Math.Max(1f, plot.Width - _xPadding * 2f);
            return plot.Left + _xPadding + (float)(offset / span * width);
        }

        private static float ValueToY(RectangleF plot, double value, double axisMin, double axisMax)
        {
            double span = Math.Max(1d, axisMax - axisMin);
            double ratio = (value - axisMin) / span;
            if (ratio < 0d) ratio = 0d;
            if (ratio > 1d) ratio = 1d;
            return plot.Bottom - (float)(ratio * plot.Height);
        }

        // Set the cached axis bounds before drawing series/area.
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Background is painted in OnPaint.
        }

        private void StoreAxis(double min, double max)
        {
            _axisMinCache = min;
            _axisMaxCache = max;
        }
    }
}
