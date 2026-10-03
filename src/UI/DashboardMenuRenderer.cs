using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexUsageTray
{
    internal sealed class DashboardMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Ink = Color.FromArgb(243, 244, 249);
        private static readonly Color InkSoft = Color.FromArgb(176, 182, 200);
        private static readonly Color Paper = Color.FromArgb(34, 35, 45);
        private static readonly Color Line = Color.FromArgb(85, 84, 102);
        private static readonly Color Blue = Color.FromArgb(82, 123, 255);
        private static readonly Color BlueSoft = Color.FromArgb(44, 51, 73);
        private static readonly Color Mint = Color.FromArgb(105, 196, 175);
        private static readonly Color Orange = Color.FromArgb(224, 160, 115);
        private static readonly Color Red = Color.FromArgb(235, 143, 154);
        private AutoStartToggleAnimation _autoStartToggle;

        public DashboardMenuRenderer()
            : base(new DashboardMenuColorTable())
        {
            RoundedEdges = false;
        }

        public void ApplyTo(ToolStripDropDownMenu menu, int minimumWidth)
        {
            float scale;
            using (Graphics graphics = menu.CreateGraphics())
            {
                scale = graphics.DpiX / 96f;
            }

            bool mainMenu = IsMainMenu(menu);
            int width = Scale(minimumWidth, scale);
            int padding = Scale(7, scale);
            Font regularFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f);
            Font headingFont = new Font(regularFont.FontFamily, 11f, FontStyle.Bold);
            Font detailFont = new Font(regularFont.FontFamily, 9f);
            Font actionFont = new Font(regularFont, FontStyle.Bold);
            menu.Renderer = this;
            menu.BackColor = Paper;
            menu.ForeColor = Ink;
            menu.Font = regularFont;
            menu.Padding = new Padding(padding);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = !mainMenu;
            menu.DropShadowEnabled = true;
            menu.MinimumSize = new Size(width, 0);
            menu.SizeChanged -= UpdateMenuRegion;
            menu.SizeChanged += UpdateMenuRegion;
            menu.Disposed += delegate
            {
                regularFont.Dispose();
                headingFont.Dispose();
                detailFont.Dispose();
                actionFont.Dispose();
            };

            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem == null)
                {
                    item.Margin = Padding.Empty;
                    item.Padding = Padding.Empty;
                    item.Height = Scale(7, scale);
                    continue;
                }

                int height = mainMenu ? 36 : 32;
                menuItem.Font = regularFont;
                if (string.Equals(menuItem.Name, "SettingsHeader", StringComparison.Ordinal))
                {
                    menuItem.Font = headingFont;
                    height = 38;
                }
                else if (string.Equals(menuItem.Name, "UsageHeader", StringComparison.Ordinal))
                {
                    menuItem.Font = detailFont;
                    height = 24;
                }
                else if (string.Equals(menuItem.Name, "UsageDetail", StringComparison.Ordinal))
                {
                    menuItem.Font = detailFont;
                    height = 20;
                }
                else if (string.Equals(menuItem.Name, "DashboardItem", StringComparison.Ordinal))
                {
                    menuItem.Font = actionFont;
                    height = 40;
                }

                menuItem.AutoSize = false;
                menuItem.Size = new Size(width - padding * 2, Scale(height, scale));
                menuItem.Margin = Padding.Empty;
                menuItem.Padding = Padding.Empty;
                if (mainMenu && string.Equals(menuItem.Name, "AutoStartItem", StringComparison.Ordinal))
                {
                    if (_autoStartToggle != null)
                    {
                        _autoStartToggle.Dispose();
                    }
                    _autoStartToggle = new AutoStartToggleAnimation(menu, menuItem);
                }
                if (menuItem.HasDropDownItems)
                {
                    ToolStripDropDownMenu childMenu = menuItem.DropDown as ToolStripDropDownMenu;
                    if (childMenu != null)
                    {
                        ApplyTo(childMenu, 200);
                    }
                }
            }

            menu.PerformLayout();
            UpdateMenuRegion(menu, EventArgs.Empty);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Paper))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96f;
            Rectangle bounds = e.ToolStrip.ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            SmoothingMode previousMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = CreateRoundedRectangle(bounds, Scale(12, scale)))
            using (Pen pen = new Pen(Line))
            {
                e.Graphics.DrawPath(pen, path);
            }
            e.Graphics.SmoothingMode = previousMode;
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96f;
            Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);
            string itemName = e.Item.Name;
            bool dashboard = string.Equals(itemName, "DashboardItem", StringComparison.Ordinal);
            if (dashboard || (e.Item.Enabled && e.Item.Selected))
            {
                Color selectionColor = dashboard
                    ? (e.Item.Selected ? Color.FromArgb(46, 65, 109) : Color.FromArgb(41, 44, 59))
                    : (string.Equals(itemName, "ExitItem", StringComparison.Ordinal)
                        ? Color.FromArgb(63, 42, 52) : BlueSoft);
                Rectangle selectionBounds = Inset(bounds, Scale(3, scale), Scale(2, scale));
                FillRoundedRectangle(e.Graphics, selectionBounds, Scale(8, scale), selectionColor);
            }

            if (string.Equals(itemName, "UsageHeader", StringComparison.Ordinal))
            {
                DrawStatusDot(e.Graphics, bounds, scale, IsErrorStatus(e.Item.Text) ? Orange : Mint);
            }
            else if (IsMainMenu(e.Item.Owner))
            {
                DrawIcon(e.Graphics, itemName, bounds, scale, ItemColor(e.Item));
                if (string.Equals(itemName, "AutoStartItem", StringComparison.Ordinal))
                {
                    DrawToggle(e.Graphics, (ToolStripMenuItem)e.Item, scale);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96f;
            Rectangle textBounds = e.TextRectangle;
            textBounds.Y = 0;
            textBounds.Height = e.Item.Height;
            if (IsMainMenu(e.Item.Owner))
            {
                // WinForms also requests a text pass for ShortcutKeyDisplayString.
                if (!string.Equals(e.Text, e.Item.Text, StringComparison.Ordinal))
                {
                    return;
                }

                string name = e.Item.Name;
                int left = Scale(40, scale);
                int right = e.Item.Width - Scale(12, scale);
                if (string.Equals(name, "SettingsHeader", StringComparison.Ordinal) ||
                    string.Equals(name, "UsageDetail", StringComparison.Ordinal))
                {
                    left = Scale(14, scale);
                }
                else if (string.Equals(name, "UsageHeader", StringComparison.Ordinal))
                {
                    left = Scale(34, scale);
                }
                else if (string.Equals(name, "AutoStartItem", StringComparison.Ordinal))
                {
                    right -= Scale(60, scale);
                }

                ToolStripMenuItem menuItem = e.Item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems)
                {
                    right -= Scale(18, scale);
                    string value = menuItem.ShortcutKeyDisplayString;
                    if (!string.IsNullOrEmpty(value))
                    {
                        int valueWidth = Math.Min(Scale(125, scale), TextRenderer.MeasureText(
                            e.Graphics, value, e.TextFont, Size.Empty,
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width);
                        Rectangle valueBounds = new Rectangle(right - valueWidth, 0, valueWidth, e.Item.Height);
                        DrawText(e.Graphics, value, e.TextFont, valueBounds, InkSoft, TextFormatFlags.Right);
                        right = valueBounds.Left - Scale(12, scale);
                    }
                }
                textBounds = new Rectangle(left, 0, Math.Max(1, right - left), e.Item.Height);
            }

            DrawText(e.Graphics, e.Text, e.TextFont, textBounds, ItemColor(e.Item), TextFormatFlags.Left);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int inset = Scale(14, e.Graphics.DpiX / 96f);
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(Color.FromArgb(110, Line)))
            {
                e.Graphics.DrawLine(pen, inset, y, Math.Max(inset, e.Item.Width - inset), y);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (IsMainMenu(e.Item.Owner))
            {
                return;
            }

            Rectangle area = e.ImageRectangle;
            float scale = e.Graphics.DpiX / 96f;
            float x = area.Left + area.Width / 2f;
            float y = area.Top + area.Height / 2f;
            SmoothingMode previousMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Blue, 2f * scale))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(x - 5f * scale, y),
                    new PointF(x - 1f * scale, y + 4f * scale),
                    new PointF(x + 6f * scale, y - 4f * scale)
                });
            }
            e.Graphics.SmoothingMode = previousMode;
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? InkSoft : Color.FromArgb(105, 110, 126);
            if (IsMainMenu(e.Item.Owner))
            {
                float scale = e.Graphics.DpiX / 96f;
                float x = e.Item.Width - Scale(15, scale);
                float y = e.Item.Height / 2f;
                SmoothingMode previousMode = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(e.ArrowColor, 1.5f * scale))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new PointF(x - 3f * scale, y - 4f * scale),
                        new PointF(x + 1f * scale, y),
                        new PointF(x - 3f * scale, y + 4f * scale)
                    });
                }
                e.Graphics.SmoothingMode = previousMode;
                return;
            }
            base.OnRenderArrow(e);
        }

        private static bool IsMainMenu(ToolStrip menu)
        {
            return menu != null && menu.Items["SettingsHeader"] != null;
        }

        private static Color ItemColor(ToolStripItem item)
        {
            if (string.Equals(item.Name, "ExitItem", StringComparison.Ordinal))
            {
                return Red;
            }
            if (string.Equals(item.Name, "SettingsHeader", StringComparison.Ordinal))
            {
                return Ink;
            }
            if (string.Equals(item.Name, "UsageHeader", StringComparison.Ordinal))
            {
                return IsErrorStatus(item.Text) ? Orange : InkSoft;
            }
            if (string.Equals(item.Name, "UsageDetail", StringComparison.Ordinal))
            {
                return InkSoft;
            }
            return item.Enabled ? Ink : Color.FromArgb(105, 110, 126);
        }

        private static void DrawText(Graphics graphics, string text, Font font, Rectangle bounds,
            Color color, TextFormatFlags alignment)
        {
            TextRenderer.DrawText(graphics, text, font, bounds, color,
                alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        private void DrawToggle(Graphics graphics, ToolStripMenuItem item, float scale)
        {
            float progress = _autoStartToggle != null && _autoStartToggle.Item == item
                ? _autoStartToggle.Progress : (item.Checked ? 1f : 0f);
            int width = Scale(52, scale);
            int height = Scale(22, scale);
            Rectangle bounds = new Rectangle(item.Width - Scale(12, scale) - width,
                (item.Height - height) / 2, width, height);
            FillRoundedRectangle(graphics, bounds, height / 2,
                BlendColor(Color.FromArgb(62, 62, 77), Blue, progress));
            Rectangle labelBounds = item.Checked
                ? new Rectangle(bounds.Left + Scale(3, scale), bounds.Top, Scale(28, scale), bounds.Height)
                : new Rectangle(bounds.Left + Scale(20, scale), bounds.Top, Scale(29, scale), bounds.Height);
            DrawText(graphics, item.Checked ? "ON" : "OFF", item.Font, labelBounds,
                item.Checked ? Ink : InkSoft, TextFormatFlags.HorizontalCenter);
            int knobSize = Scale(16, scale);
            int inset = Scale(3, scale);
            float knobX = bounds.Left + inset + (bounds.Width - inset * 2 - knobSize) * progress;
            SmoothingMode previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(BlendColor(InkSoft, Ink, progress)))
            {
                graphics.FillEllipse(brush, knobX, bounds.Top + (bounds.Height - knobSize) / 2f, knobSize, knobSize);
            }
            graphics.SmoothingMode = previousMode;
        }

        private static Color BlendColor(Color from, Color to, float progress)
        {
            return Color.FromArgb((int)Math.Round(from.R + (to.R - from.R) * progress),
                (int)Math.Round(from.G + (to.G - from.G) * progress),
                (int)Math.Round(from.B + (to.B - from.B) * progress));
        }

        private static void DrawIcon(Graphics graphics, string name, Rectangle bounds, float scale, Color color)
        {
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(Scale(21, scale), bounds.Height / 2f);
            graphics.ScaleTransform(scale, scale);
            using (Pen pen = new Pen(string.Equals(name, "DashboardItem", StringComparison.Ordinal) ? Blue : color, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (name)
                {
                    case "DashboardItem":
                        graphics.DrawLines(pen, new[] { new PointF(0, -7), new PointF(-7, -7), new PointF(-7, 7), new PointF(7, 7), new PointF(7, 0) });
                        graphics.DrawLine(pen, 0, 0, 7, -7);
                        graphics.DrawLines(pen, new[] { new PointF(2, -7), new PointF(7, -7), new PointF(7, -2) });
                        break;
                    case "RefreshItem":
                        graphics.DrawArc(pen, -7, -7, 14, 14, 45, 285);
                        graphics.DrawLines(pen, new[] { new PointF(3, -7), new PointF(7, -4), new PointF(7, -9) });
                        break;
                    case "DisplayModeItem":
                        graphics.DrawLine(pen, -7, 7, 7, 7);
                        graphics.DrawLine(pen, -6, 3, -6, -2);
                        graphics.DrawLine(pen, 0, 3, 0, -7);
                        graphics.DrawLine(pen, 6, 3, 6, -4);
                        break;
                    case "PositionItem":
                        graphics.DrawRectangle(pen, -7, -6, 14, 12);
                        graphics.DrawRectangle(pen, 1, 0, 4, 4);
                        break;
                    case "AutoStartItem":
                        graphics.DrawEllipse(pen, -6, -6, 12, 12);
                        graphics.DrawLine(pen, 0, -9, 0, -5);
                        graphics.DrawLine(pen, 0, 5, 0, 9);
                        graphics.DrawLine(pen, -9, 0, -5, 0);
                        graphics.DrawLine(pen, 5, 0, 9, 0);
                        graphics.DrawEllipse(pen, -2, -2, 4, 4);
                        break;
                    case "UpdateItem":
                        graphics.DrawLine(pen, 0, 3, 0, -7);
                        graphics.DrawLines(pen, new[] { new PointF(-4, -3), new PointF(0, -7), new PointF(4, -3) });
                        graphics.DrawLines(pen, new[] { new PointF(-7, 2), new PointF(-7, 7), new PointF(7, 7), new PointF(7, 2) });
                        break;
                    case "AboutItem":
                        graphics.DrawEllipse(pen, -7, -7, 14, 14);
                        graphics.DrawLine(pen, 0, 0, 0, 4);
                        graphics.DrawLine(pen, 0, -4, 0, -3);
                        break;
                    case "ExitItem":
                        graphics.DrawArc(pen, -7, -7, 14, 14, -45, 270);
                        graphics.DrawLine(pen, 0, -9, 0, -1);
                        break;
                }
            }
            graphics.Restore(state);
        }

        private static void DrawStatusDot(Graphics graphics, Rectangle bounds, float scale, Color color)
        {
            int size = Scale(6, scale);
            SmoothingMode previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillEllipse(brush, Scale(18, scale), (bounds.Height - size) / 2, size, size);
            }
            graphics.SmoothingMode = previousMode;
        }

        private static void UpdateMenuRegion(object sender, EventArgs e)
        {
            ToolStripDropDownMenu menu = (ToolStripDropDownMenu)sender;
            if (menu.Width <= 0 || menu.Height <= 0)
            {
                return;
            }
            float scale;
            using (Graphics graphics = menu.CreateGraphics())
            {
                scale = graphics.DpiX / 96f;
            }
            using (GraphicsPath path = CreateRoundedRectangle(menu.ClientRectangle, Scale(12, scale)))
            {
                Region previousRegion = menu.Region;
                menu.Region = new Region(path);
                if (previousRegion != null)
                {
                    previousRegion.Dispose();
                }
            }
        }

        private static bool IsErrorStatus(string text)
        {
            return !string.IsNullOrEmpty(text) && text.StartsWith("갱신 실패", StringComparison.Ordinal);
        }

        private static int Scale(int value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        private static Rectangle Inset(Rectangle rectangle, int horizontal, int vertical)
        {
            return new Rectangle(rectangle.Left + horizontal, rectangle.Top + vertical,
                Math.Max(1, rectangle.Width - horizontal * 2), Math.Max(1, rectangle.Height - vertical * 2));
        }

        private static void FillRoundedRectangle(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            SmoothingMode previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = CreateRoundedRectangle(bounds, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillPath(brush, path);
            }
            graphics.SmoothingMode = previousMode;
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class AutoStartToggleAnimation : IDisposable
        {
            private readonly ToolStripDropDownMenu _menu;
            private readonly Timer _timer;
            private readonly Stopwatch _clock = new Stopwatch();
            private float _from;
            private float _target;
            private bool _disposed;

            public AutoStartToggleAnimation(ToolStripDropDownMenu menu, ToolStripMenuItem item)
            {
                _menu = menu;
                Item = item;
                _timer = new Timer { Interval = 15 };
                _timer.Tick += Advance;
                Item.CheckedChanged += CheckedChanged;
                Item.Disposed += Disposed;
                _menu.Opened += Snap;
                _menu.Closed += Snap;
                _menu.Disposed += Disposed;
                Snap(null, EventArgs.Empty);
            }

            public ToolStripMenuItem Item { get; private set; }
            public float Progress { get; private set; }

            private void CheckedChanged(object sender, EventArgs e)
            {
                if (!_menu.Visible || !SystemInformation.IsMenuAnimationEnabled)
                {
                    Snap(sender, e);
                    return;
                }

                _from = Progress;
                _target = Item.Checked ? 1f : 0f;
                _clock.Restart();
                _timer.Start();
                Item.Invalidate();
            }

            private void Advance(object sender, EventArgs e)
            {
                if (!_menu.Visible)
                {
                    Snap(sender, e);
                    return;
                }

                float elapsed = Math.Min(1f, (float)_clock.Elapsed.TotalMilliseconds / 170f);
                float remaining = 1f - elapsed;
                Progress = _from + (_target - _from) * (1f - remaining * remaining * remaining);
                if (elapsed >= 1f)
                {
                    Progress = _target;
                    _timer.Stop();
                    _clock.Stop();
                }
                Item.Invalidate();
            }

            private void Snap(object sender, EventArgs e)
            {
                _timer.Stop();
                _clock.Reset();
                Progress = Item.Checked ? 1f : 0f;
                Item.Invalidate();
            }

            private void Disposed(object sender, EventArgs e)
            {
                Dispose();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                Item.CheckedChanged -= CheckedChanged;
                Item.Disposed -= Disposed;
                _menu.Opened -= Snap;
                _menu.Closed -= Snap;
                _menu.Disposed -= Disposed;
                _timer.Tick -= Advance;
                _timer.Dispose();
                _clock.Stop();
            }
        }

        private sealed class DashboardMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Paper; } }
            public override Color ImageMarginGradientBegin { get { return Paper; } }
            public override Color ImageMarginGradientMiddle { get { return Paper; } }
            public override Color ImageMarginGradientEnd { get { return Paper; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return BlueSoft; } }
            public override Color MenuItemSelected { get { return BlueSoft; } }
            public override Color MenuItemSelectedGradientBegin { get { return BlueSoft; } }
            public override Color MenuItemSelectedGradientEnd { get { return BlueSoft; } }
            public override Color MenuItemPressedGradientBegin { get { return BlueSoft; } }
            public override Color MenuItemPressedGradientMiddle { get { return BlueSoft; } }
            public override Color MenuItemPressedGradientEnd { get { return BlueSoft; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Paper; } }
            public override Color CheckBackground { get { return Paper; } }
            public override Color CheckPressedBackground { get { return Paper; } }
            public override Color CheckSelectedBackground { get { return Paper; } }
        }
    }
}
