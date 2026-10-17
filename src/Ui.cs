using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DiskPulse
{
    internal static class Ui
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public static float S(Control control, float value)
        {
            float dpi = 0f;
            if (control != null)
            {
                try
                {
                    if (control.IsHandleCreated) dpi = GetDpiForWindow(control.Handle);
                }
                catch
                {
                }
                if (dpi <= 0f)
                {
                    try
                    {
                        using (Graphics graphics = control.CreateGraphics()) dpi = graphics.DpiX;
                    }
                    catch
                    {
                    }
                }
                if (dpi <= 0f)
                {
                    try { dpi = control.DeviceDpi; }
                    catch { }
                }
            }
            if (dpi <= 0f) dpi = 96f;
            return value * (dpi / 96f);
        }

        public static GraphicsPath RoundRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0.1f)
            {
                path.AddRectangle(rect);
                return path;
            }

            float d = radius * 2f;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            float r = d / 2f;

            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, RectangleF rect, float radius, Color color)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void FillRound(Graphics g, RectangleF rect, float radius, Brush brush)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRound(Graphics g, RectangleF rect, float radius, Color color, float width)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            using (Pen pen = new Pen(color, width))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, path);
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(text)) return;
            Rectangle r = Rectangle.Round(rect);
            TextRenderer.DrawText(g, text, font, r, color, flags);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, float x, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextRenderer.DrawText(g, text, font, new Point((int)Math.Round(x), (int)Math.Round(y)), color,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        public static void DrawTextRight(Graphics g, string text, Font font, Color color, RectangleF rect)
        {
            DrawText(g, text, font, color, rect,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            double value = bytes;
            string unit = "B";
            if (value >= 1024d * 1024d * 1024d * 1024d)
            {
                value /= 1024d * 1024d * 1024d * 1024d;
                unit = "TB";
            }
            else if (value >= 1024d * 1024d * 1024d)
            {
                value /= 1024d * 1024d * 1024d;
                unit = "GB";
            }
            else if (value >= 1024d * 1024d)
            {
                value /= 1024d * 1024d;
                unit = "MB";
            }
            else if (value >= 1024d)
            {
                value /= 1024d;
                unit = "KB";
            }

            string number;
            if (value >= 100d) number = value.ToString("0");
            else if (value >= 10d) number = value.ToString("0.0");
            else number = value.ToString("0.00");
            return number + " " + unit;
        }

        public static string FormatBytesShort(long bytes)
        {
            if (bytes < 0) bytes = 0;
            double value = bytes;
            string unit = "B";
            if (value >= 1024d * 1024d * 1024d * 1024d)
            {
                value /= 1024d * 1024d * 1024d * 1024d;
                unit = "TB";
            }
            else if (value >= 1024d * 1024d * 1024d)
            {
                value /= 1024d * 1024d * 1024d;
                unit = "GB";
            }
            else if (value >= 1024d * 1024d)
            {
                value /= 1024d * 1024d;
                unit = "MB";
            }
            else if (value >= 1024d)
            {
                value /= 1024d;
                unit = "KB";
            }

            return value.ToString(value >= 10d ? "0.0" : "0.00") + " " + unit;
        }

        public static string FormatSignedBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            return (bytes > 0 ? "+" : "-") + FormatBytes(Math.Abs(bytes));
        }

        public static string FormatPercent(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio)) return "--";
            return (ratio * 100d).ToString("0.0") + "%";
        }

        public static string ShortPath(string path, int maxChars)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.Length <= maxChars) return path;
            string root = Path.GetPathRoot(path);
            if (root == null) root = "";
            string name = Path.GetFileName(path);
            int keep = maxChars - root.Length - name.Length - 3;
            if (keep < 2) return path.Substring(0, Math.Max(0, maxChars - 3)) + "...";
            string middle = path.Substring(root.Length, Math.Min(keep, path.Length - root.Length));
            return root + middle + "..." + name;
        }

        public static Color StatusColor(long delta)
        {
            if (delta > 0) return Theme.Red;
            if (delta < 0) return Theme.Green;
            return Theme.TextSecondary;
        }
    }
}
