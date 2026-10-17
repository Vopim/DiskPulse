using System;
using System.Drawing;
using System.Windows.Forms;

namespace DiskPulse
{
    internal static class AppIcon
    {
        public static Icon Load()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExecutablePath);
                if (icon != null) return icon;
            }
            catch
            {
            }
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(17, 26, 43); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(17, 26, 43); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(17, 26, 43); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(17, 26, 43); } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Alpha(Theme.Accent, 80); } }
        public override Color MenuItemSelected { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
        public override Color SeparatorDark { get { return Theme.BorderSoft; } }
        public override Color SeparatorLight { get { return Theme.BorderSoft; } }
        public override Color ToolStripBorder { get { return Theme.Border; } }
        public override Color ButtonSelectedHighlight { get { return Theme.CardHover; } }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Border, 1f))
            {
                Rectangle r = e.AffectedBounds;
                e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
        }
    }

    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly MainForm _form;

        public TrayApplicationContext(MainForm form)
        {
            _form = form;
            _form.FormClosed += delegate { ExitThread(); };
        }
    }
}
