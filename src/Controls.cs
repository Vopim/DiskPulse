using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DiskPulse
{
    internal enum CardIconKind
    {
        Disk,
        Trend,
        Free,
        Clock
    }

    internal sealed class StatCard : Control
    {
        public string Title = "";
        public string Value = "--";
        public string SubText = "";
        public Color ValueColor = Theme.Text;
        public Color Accent = Theme.Accent;
        public CardIconKind Icon = CardIconKind.Disk;
        public int DeltaDirection;

        public StatCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = Ui.S(this, 1f);
            RectangleF rect = new RectangleF(0.5f * s, 0.5f * s, Width - 1.2f * s, Height - 1.2f * s);
            float radius = 14f * s;

            using (LinearGradientBrush bg = new LinearGradientBrush(rect.Height <= 0 ? new RectangleF(rect.X, rect.Y, rect.Width, 1) : rect,
                Theme.CardRaised, Theme.Card, 90f))
            {
                Ui.FillRound(g, rect, radius, bg);
            }
            Ui.DrawRound(g, rect, radius, Theme.BorderSoft, 1f * s);

            // Accent wash in the far corner.
            RectangleF glow = new RectangleF(rect.Right - 86f * s, rect.Top - 22f * s, 120f * s, 120f * s);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(glow);
                using (PathGradientBrush brush = new PathGradientBrush(gp))
                {
                    brush.CenterColor = Theme.Alpha(Accent, 26);
                    brush.SurroundColors = new Color[] { Theme.Alpha(Accent, 0) };
                    g.FillEllipse(brush, glow);
                }
            }

            float padX = 18f * s;
            float iconSize = 34f * s;
            RectangleF iconRect = new RectangleF(rect.Right - padX - iconSize, rect.Top + 17f * s, iconSize, iconSize);
            Ui.FillRound(g, iconRect, 10f * s, Theme.Alpha(Accent, 20));
            DrawIcon(g, RectangleF.Inflate(iconRect, -9f * s, -9f * s), Accent, s);

            Ui.DrawText(g, Title, Theme.Font(8.8f, FontStyle.Regular), Theme.TextSecondary,
                new RectangleF(padX, rect.Top + 15f * s, rect.Width - padX - iconSize - 26f * s, 20f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            Font valueFont = Theme.NumberFont(20.5f, FontStyle.Bold);
            int valueWidth = TextRenderer.MeasureText(Value, valueFont, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width;
            float available = rect.Width - padX * 2f - (DeltaDirection != 0 ? 14f * s : 0f);
            if (valueWidth > available && valueWidth > 0)
            {
                float ratio = available / valueWidth;
                float newSize = Math.Max(13f, 20.5f * ratio);
                valueFont = Theme.NumberFont(newSize, FontStyle.Bold);
            }

            float valueX = padX;
            if (DeltaDirection != 0)
            {
                DrawDeltaArrow(g, new RectangleF(padX, rect.Top + 48f * s, 8f * s, 16f * s), DeltaDirection, ValueColor, s);
                valueX += 13f * s;
            }

            Ui.DrawText(g, Value, valueFont, ValueColor,
                new RectangleF(valueX, rect.Top + 39f * s, rect.Width - valueX - padX, 31f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            Ui.DrawText(g, SubText, Theme.Font(8.2f, FontStyle.Regular), Theme.Muted,
                new RectangleF(padX, rect.Top + 74f * s, rect.Width - padX * 2f, 18f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private static void DrawDeltaArrow(Graphics g, RectangleF rect, int direction, Color color, float s)
        {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            PointF[] points;
            if (direction > 0)
            {
                points = new PointF[]
                {
                    new PointF(cx, cy - 5f * s),
                    new PointF(cx + 4f * s, cy + 3f * s),
                    new PointF(cx - 4f * s, cy + 3f * s)
                };
            }
            else
            {
                points = new PointF[]
                {
                    new PointF(cx, cy + 5f * s),
                    new PointF(cx + 4f * s, cy - 3f * s),
                    new PointF(cx - 4f * s, cy - 3f * s)
                };
            }
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPolygon(brush, points);
            }
        }

        private void DrawIcon(Graphics g, RectangleF r, Color color, float s)
        {
            using (Pen pen = new Pen(Theme.Alpha(color, 220), 1.6f * s))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (Icon)
                {
                    case CardIconKind.Disk:
                        {
                            RectangleF body = new RectangleF(r.X, r.Y + 1.5f * s, r.Width, r.Height - 3f * s);
                            using (GraphicsPath path = Ui.RoundRect(body, 3f * s))
                            {
                                g.DrawPath(pen, path);
                            }
                            g.DrawLine(pen, r.X + 2f * s, r.Y + r.Height * 0.52f, r.Right - 2f * s, r.Y + r.Height * 0.52f);
                            using (SolidBrush dot = new SolidBrush(Theme.Alpha(color, 220)))
                            {
                                g.FillEllipse(dot, r.X + 3f * s, r.Y + r.Height * 0.68f, 2.2f * s, 2.2f * s);
                            }
                            break;
                        }
                    case CardIconKind.Trend:
                        {
                            g.DrawLines(pen, new PointF[]
                            {
                                new PointF(r.X + 1f * s, r.Bottom - 3f * s),
                                new PointF(r.X + r.Width * 0.34f, r.Y + r.Height * 0.50f),
                                new PointF(r.X + r.Width * 0.58f, r.Y + r.Height * 0.66f),
                                new PointF(r.Right - 2f * s, r.Y + 2f * s)
                            });
                            g.DrawLine(pen, r.Right - 2f * s, r.Y + 2f * s, r.Right - 6f * s, r.Y + 2.5f * s);
                            g.DrawLine(pen, r.Right - 2f * s, r.Y + 2f * s, r.Right - 2.5f * s, r.Y + 6f * s);
                            break;
                        }
                    case CardIconKind.Free:
                        {
                            g.DrawArc(pen, r.X + 1f * s, r.Y + 1f * s, r.Width - 2f * s, r.Height - 2f * s, -80f, 300f);
                            using (SolidBrush dot = new SolidBrush(Theme.Alpha(color, 220)))
                            {
                                g.FillEllipse(dot, r.X + r.Width * 0.54f, r.Y + r.Height * 0.54f, 3f * s, 3f * s);
                            }
                            break;
                        }
                    case CardIconKind.Clock:
                        {
                            g.DrawEllipse(pen, r.X + 1f * s, r.Y + 1f * s, r.Width - 2f * s, r.Height - 2f * s);
                            g.DrawLine(pen, r.X + r.Width / 2f, r.Y + r.Height / 2f, r.X + r.Width / 2f, r.Y + 3.5f * s);
                            g.DrawLine(pen, r.X + r.Width / 2f, r.Y + r.Height / 2f, r.X + r.Width * 0.72f, r.Y + r.Height * 0.58f);
                            break;
                        }
                }
            }
        }
    }

    internal sealed class PillButton : Control
    {
        public bool Primary;
        public Color Accent = Theme.Accent;

        private bool _hover;
        private bool _pressed;

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            Height = 36;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = Ui.S(this, 1f);
            RectangleF rect = new RectangleF(1f * s, 1f * s, Width - 2f * s, Height - 2f * s);
            float radius = 9f * s;

            Color fill;
            Color border;
            Color text;
            if (!Enabled)
            {
                fill = Theme.Card;
                border = Theme.BorderSoft;
                text = Theme.Faint;
            }
            else if (Primary)
            {
                fill = _hover ? Theme.Mix(Accent, Color.White, 0.10f) : Accent;
                if (_pressed) fill = Theme.Mix(Accent, Color.Black, 0.12f);
                border = Theme.Alpha(Theme.AccentLight, 120);
                text = Color.White;
            }
            else
            {
                fill = _hover ? Theme.CardHover : Theme.CardRaised;
                border = _hover ? Theme.Border : Theme.BorderSoft;
                text = _hover ? Theme.Text : Theme.TextSecondary;
            }

            if (Primary)
            {
                RectangleF r = rect;
                using (LinearGradientBrush brush = new LinearGradientBrush(r, fill, Theme.Mix(fill, Theme.Cyan, 0.16f), 0f))
                {
                    Ui.FillRound(g, r, radius, brush);
                }
            }
            else
            {
                Ui.FillRound(g, rect, radius, fill);
            }
            Ui.DrawRound(g, rect, radius, border, 1f * s);

            Ui.DrawText(g, Text, Theme.Font(9f, FontStyle.Regular), text,
                new RectangleF(4f * s, 0f, Width - 8f * s, Height),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal enum CaptionKind
    {
        Minimize,
        Maximize,
        Close
    }

    internal sealed class CaptionButton : Control
    {
        public CaptionKind Kind;
        public bool IsMaximized;
        private bool _hover;

        public CaptionButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Base;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Ui.S(this, 1f);
            bool close = Kind == CaptionKind.Close;
            if (_hover)
            {
                Color fill = close ? Color.FromArgb(232, 17, 35) : Theme.CardHover;
                Ui.FillRound(g, new RectangleF(0, 0, Width, Height), 7f * s, fill);
            }

            Color color = _hover && close ? Color.White : (_hover ? Theme.Text : Theme.TextSecondary);
            using (Pen pen = new Pen(color, 1.15f * s))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float cx = Width / 2f;
                float cy = Height / 2f;
                if (Kind == CaptionKind.Minimize)
                {
                    g.DrawLine(pen, cx - 5f * s, cy + 4f * s, cx + 5f * s, cy + 4f * s);
                }
                else if (Kind == CaptionKind.Maximize)
                {
                    if (IsMaximized)
                    {
                        RectangleF r1 = new RectangleF(cx - 5f * s, cy - 5f * s, 8f * s, 8f * s);
                        RectangleF r2 = new RectangleF(cx - 2f * s, cy - 2f * s, 8f * s, 8f * s);
                        g.DrawRectangle(pen, r1.X, r1.Y, r1.Width, r1.Height);
                        g.DrawRectangle(pen, r2.X, r2.Y, r2.Width, r2.Height);
                    }
                    else
                    {
                        g.DrawRectangle(pen, cx - 5f * s, cy - 5f * s, 10f * s, 10f * s);
                    }
                }
                else
                {
                    g.DrawLine(pen, cx - 5f * s, cy - 5f * s, cx + 5f * s, cy + 5f * s);
                    g.DrawLine(pen, cx + 5f * s, cy - 5f * s, cx - 5f * s, cy + 5f * s);
                }
            }
        }
    }

    internal sealed class SegmentedControl : Control
    {
        public string[] Items = new string[0];
        public int SelectedIndex;
        public event EventHandler SelectionChanged;

        private int _hoverIndex = -1;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            Height = 38;
        }

        public void SetSelectedSilently(int index)
        {
            if (index < 0 || index >= Items.Length) return;
            SelectedIndex = index;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = HitTest(e.X);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int index = HitTest(e.X);
            if (index < 0 || index == SelectedIndex) return;
            SelectedIndex = index;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private int HitTest(int x)
        {
            if (Items.Length == 0) return -1;
            float s = Ui.S(this, 1f);
            float pad = 3f * s;
            float itemWidth = (Width - pad * 2f) / Items.Length;
            if (x < pad || x > Width - pad) return -1;
            int index = (int)((x - pad) / itemWidth);
            if (index < 0) index = 0;
            if (index >= Items.Length) index = Items.Length - 1;
            return index;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = Ui.S(this, 1f);
            RectangleF rect = new RectangleF(0.5f * s, 0.5f * s, Width - 1f * s, Height - 1f * s);
            Ui.FillRound(g, rect, 10f * s, Theme.Card);
            Ui.DrawRound(g, rect, 10f * s, Theme.BorderSoft, 1f * s);

            if (Items.Length == 0) return;
            float pad = 3f * s;
            float itemWidth = (Width - pad * 2f) / Items.Length;
            Font font = Theme.Font(8.8f, FontStyle.Regular);
            for (int i = 0; i < Items.Length; i++)
            {
                RectangleF item = new RectangleF(pad + i * itemWidth, pad, itemWidth, Height - pad * 2f);
                bool selected = i == SelectedIndex;
                if (selected)
                {
                    RectangleF pill = new RectangleF(item.X + 1f * s, item.Y, item.Width - 2f * s, item.Height);
                    Ui.FillRound(g, pill, 7.5f * s, Theme.Alpha(Theme.Accent, 34));
                    Ui.DrawRound(g, pill, 7.5f * s, Theme.Alpha(Theme.AccentLight, 95), 1f * s);
                }
                else if (i == _hoverIndex)
                {
                    Ui.FillRound(g, new RectangleF(item.X + 2f * s, item.Y + 1f * s, item.Width - 4f * s, item.Height - 2f * s),
                        7f * s, Theme.Alpha(Theme.CardHover, 150));
                }

                Color color = selected ? Theme.AccentLight : (i == _hoverIndex ? Theme.Text : Theme.TextSecondary);
                Ui.DrawText(g, Items[i], font, color, item,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }

    internal sealed class ToggleSwitch : Control
    {
        public event EventHandler CheckedChanged;
        private bool _checked;
        private float _anim;
        private readonly Timer _timer;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Base;
            Cursor = Cursors.Hand;
            Width = 40;
            Height = 23;
            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += delegate
            {
                float target = _checked ? 1f : 0f;
                _anim += (target - _anim) * 0.35f;
                if (Math.Abs(target - _anim) < 0.03f)
                {
                    _anim = target;
                    _timer.Stop();
                }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _timer.Start();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public void SetCheckedSilently(bool value)
        {
            _checked = value;
            _anim = value ? 1f : 0f;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Ui.S(this, 1f);
            float h = Height - 2f * s;
            RectangleF track = new RectangleF(1f * s, 1f * s, Width - 2f * s, h);
            Color off = Color.FromArgb(38, 51, 76);
            Color on = Theme.Mix(Theme.Accent, Theme.Cyan, 0.30f);
            Color fill = Theme.Mix(off, on, _anim);
            Ui.FillRound(g, track, h / 2f, fill);
            Ui.DrawRound(g, track, h / 2f, Theme.Mix(Theme.Border, Theme.Accent, _anim * 0.6f), 1f * s);

            float knob = h - 6f * s;
            float x = track.X + 3f * s + (track.Width - knob - 6f * s) * _anim;
            float y = track.Y + 3f * s;
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
            {
                g.FillEllipse(shadow, x + 0.5f * s, y + 1f * s, knob, knob);
            }
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                g.FillEllipse(brush, x, y, knob, knob);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class DriveChip : Control
    {
        public string DriveName = "";
        public string Caption = "";
        public long Used;
        public long Total;
        public Color DotColor = Theme.Accent;
        public bool Selected;
        public bool IsTotal;
        public bool Hovered;

        public DriveChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Base;
            Cursor = Cursors.Hand;
            Height = 58;
            Width = 166;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s = Ui.S(this, 1f);
            RectangleF rect = new RectangleF(0.5f * s, 0.5f * s, Width - 1f * s, Height - 1f * s);
            float radius = 11f * s;

            Color fill = Selected ? Theme.Mix(Theme.Card, DotColor, 0.08f) : (Hovered ? Theme.CardHover : Theme.Card);
            Ui.FillRound(g, rect, radius, fill);
            Ui.DrawRound(g, rect, radius, Selected ? Theme.Alpha(DotColor, 135) : Theme.BorderSoft, 1f * s);

            if (Selected)
            {
                Ui.FillRound(g, new RectangleF(rect.X + 1f * s, rect.Y + 12f * s, 2.5f * s, rect.Height - 24f * s),
                    1.2f * s, DotColor);
            }

            using (SolidBrush dot = new SolidBrush(DotColor))
            {
                g.FillEllipse(dot, rect.X + 14f * s, rect.Y + 17f * s, 7f * s, 7f * s);
            }
            if (Selected)
            {
                using (Pen glow = new Pen(Theme.Alpha(DotColor, 90), 2f * s))
                {
                    g.DrawEllipse(glow, rect.X + 16f * s, rect.Y + 19f * s, 3f * s, 3f * s);
                }
            }

            float left = rect.X + 27f * s;
            float rightEdge = rect.Right - 13f * s;
            float usedWidth = 60f * s;
            float nameWidth = Math.Max(24f * s, rightEdge - left - usedWidth - 6f * s);
            Ui.DrawText(g, DriveName, Theme.Font(9.2f, FontStyle.Bold), Selected ? Theme.Text : Theme.TextSecondary,
                new RectangleF(left, rect.Y + 10f * s, nameWidth, 20f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            Ui.DrawText(g, Caption, Theme.Font(7.4f, FontStyle.Regular), Theme.Muted,
                new RectangleF(left, rect.Y + 28f * s, nameWidth, 16f * s),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            string usedText = Ui.FormatBytes(Used);
            Font usedFont = Theme.NumberFont(8.8f, FontStyle.Bold);
            Ui.DrawText(g, usedText, usedFont, Selected ? Theme.Text : Theme.TextSecondary,
                new RectangleF(rightEdge - usedWidth, rect.Y + 10f * s, usedWidth, 20f * s),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            Ui.DrawText(g, "共 " + Ui.FormatBytes(Total), Theme.NumberFont(7.2f, FontStyle.Regular), Theme.Muted,
                new RectangleF(rightEdge - usedWidth, rect.Y + 28f * s, usedWidth, 16f * s),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            float barX = rect.X + 14f * s;
            float barY = rect.Bottom - 9f * s;
            float barW = rect.Width - 28f * s;
            Ui.FillRound(g, new RectangleF(barX, barY, barW, 3f * s), 1.5f * s, Color.FromArgb(35, 49, 73));
            double ratio = Total > 0 ? (double)Used / (double)Total : 0;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            if (ratio > 0)
            {
                Ui.FillRound(g, new RectangleF(barX, barY, (float)(barW * ratio), 3f * s), 1.5f * s,
                    Selected ? DotColor : Theme.Mix(DotColor, Theme.Base, 0.28f));
            }
        }
    }
}
