using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;

namespace DiskPulse
{
    internal static class Theme
    {
        // Core surfaces
        public static readonly Color Base = Color.FromArgb(9, 14, 26);
        public static readonly Color BaseSoft = Color.FromArgb(12, 18, 33);
        public static readonly Color Card = Color.FromArgb(17, 26, 43);
        public static readonly Color CardHover = Color.FromArgb(21, 32, 54);
        public static readonly Color CardRaised = Color.FromArgb(20, 30, 50);
        public static readonly Color Border = Color.FromArgb(30, 44, 70);
        public static readonly Color BorderSoft = Color.FromArgb(24, 36, 58);
        public static readonly Color Grid = Color.FromArgb(27, 39, 64);

        // Text
        public static readonly Color Text = Color.FromArgb(232, 238, 249);
        public static readonly Color TextSecondary = Color.FromArgb(150, 166, 196);
        public static readonly Color Muted = Color.FromArgb(100, 117, 143);
        public static readonly Color Faint = Color.FromArgb(72, 88, 112);

        // Accents
        public static readonly Color Accent = Color.FromArgb(79, 140, 255);
        public static readonly Color AccentLight = Color.FromArgb(120, 169, 255);
        public static readonly Color Cyan = Color.FromArgb(45, 212, 191);
        public static readonly Color Green = Color.FromArgb(52, 211, 153);
        public static readonly Color Red = Color.FromArgb(248, 113, 113);
        public static readonly Color Amber = Color.FromArgb(251, 191, 36);
        public static readonly Color Violet = Color.FromArgb(167, 139, 250);
        public static readonly Color Pink = Color.FromArgb(244, 114, 182);

        public static readonly Color[] SeriesColors = new Color[]
        {
            Color.FromArgb(79, 140, 255),
            Color.FromArgb(45, 212, 191),
            Color.FromArgb(167, 139, 250),
            Color.FromArgb(251, 191, 36),
            Color.FromArgb(244, 114, 182),
            Color.FromArgb(52, 211, 153),
            Color.FromArgb(96, 165, 250),
            Color.FromArgb(251, 146, 60)
        };

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
        private static readonly object FontLock = new object();
        private static string _family;
        private static string _numberFamily;

        public static string UiFamily
        {
            get
            {
                if (_family == null)
                {
                    _family = PickFamily(new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Arial" });
                }
                return _family;
            }
        }

        public static string NumberFamily
        {
            get
            {
                if (_numberFamily == null)
                {
                    _numberFamily = PickFamily(new string[] { "Segoe UI", "Microsoft YaHei UI", "Arial" });
                }
                return _numberFamily;
            }
        }

        private static string PickFamily(string[] candidates)
        {
            try
            {
                using (InstalledFontCollection fonts = new InstalledFontCollection())
                {
                    foreach (string candidate in candidates)
                    {
                        foreach (FontFamily family in fonts.Families)
                        {
                            if (string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase))
                            {
                                return family.Name;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return FontFamily.GenericSansSerif.Name;
        }

        public static Font Font(float points, FontStyle style)
        {
            return Font(UiFamily, points, style);
        }

        public static Font NumberFont(float points, FontStyle style)
        {
            return Font(NumberFamily, points, style);
        }

        private static Font Font(string family, float points, FontStyle style)
        {
            string key = family + "|" + points.ToString("0.##") + "|" + ((int)style).ToString();
            lock (FontLock)
            {
                Font font;
                if (!FontCache.TryGetValue(key, out font))
                {
                    font = new Font(family, points, style, GraphicsUnit.Point);
                    FontCache[key] = font;
                }
                return font;
            }
        }

        public static Color Alpha(Color color, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static Color Mix(Color a, Color b, float amount)
        {
            if (amount < 0f) amount = 0f;
            if (amount > 1f) amount = 1f;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * amount),
                (int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount),
                (int)(a.B + (b.B - a.B) * amount));
        }
    }
}
