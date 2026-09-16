using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexUsageTray
{
    internal sealed class DashboardMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Ink = Color.FromArgb(19, 34, 56);
        private static readonly Color InkSoft = Color.FromArgb(82, 97, 118);
        private static readonly Color Paper = Color.White;
        private static readonly Color Line = Color.FromArgb(219, 228, 235);
        private static readonly Color Blue = Color.FromArgb(44, 116, 232);
        private static readonly Color BlueSoft = Color.FromArgb(234, 242, 255);
        private static readonly Color Mint = Color.FromArgb(46, 170, 128);
        private static readonly Color Orange = Color.FromArgb(241, 121, 71);
        private static readonly Color OrangeSoft = Color.FromArgb(255, 244, 237);

        public DashboardMenuRenderer()
            : base(new DashboardMenuColorTable())
        {
            RoundedEdges = true;
        }

        public void ApplyTo(ToolStripDropDownMenu menu, int minimumWidth)
        {
            menu.Renderer = this;
            menu.BackColor = Paper;
            menu.ForeColor = Ink;
            menu.Font = SystemFonts.MessageBoxFont;
            menu.Padding = new Padding(6);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.DropShadowEnabled = true;
            menu.MinimumSize = new Size(minimumWidth, 0);

            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem == null)
                {
                    continue;
                }

                if (string.Equals(menuItem.Name, "UsageHeader", StringComparison.Ordinal))
                {
                    menuItem.Padding = new Padding(7, 7, 7, 7);
                }
                else if (string.Equals(menuItem.Name, "UsageDetail", StringComparison.Ordinal))
                {
                    menuItem.Padding = new Padding(7, 2, 7, 2);
                }
                else
                {
                    menuItem.Padding = new Padding(5, 5, 7, 5);
                }

                if (menuItem.HasDropDownItems)
                {
                    ToolStripDropDownMenu childMenu = menuItem.DropDown as ToolStripDropDownMenu;
                    if (childMenu != null)
                    {
                        ApplyTo(childMenu, 220);
                    }
                }
            }
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
            Rectangle bounds = e.ToolStrip.ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            using (Pen pen = new Pen(Line))
            {
                e.Graphics.DrawRectangle(pen, bounds);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Paper))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);
            string itemName = e.Item.Name;

            if (string.Equals(itemName, "UsageHeader", StringComparison.Ordinal))
            {
                DrawStatusDot(e.Graphics, bounds, IsErrorStatus(e.Item.Text) ? Orange : Mint);
                return;
            }

            if (!e.Item.Enabled || !e.Item.Selected)
            {
                return;
            }

            Rectangle selectionBounds = Inset(bounds, 4, 2, 4, 2);
            Color selectionColor = string.Equals(itemName, "ExitItem", StringComparison.Ordinal)
                ? OrangeSoft
                : BlueSoft;
            FillRoundedRectangle(e.Graphics, selectionBounds, 8, selectionColor);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color textColor;
            if (string.Equals(e.Item.Name, "UsageHeader", StringComparison.Ordinal))
            {
                textColor = IsErrorStatus(e.Item.Text) ? Orange : InkSoft;
            }
            else if (string.Equals(e.Item.Name, "UsageDetail", StringComparison.Ordinal))
            {
                textColor = InkSoft;
            }
            else if (string.Equals(e.Item.Name, "ExitItem", StringComparison.Ordinal) && e.Item.Selected)
            {
                textColor = Color.FromArgb(182, 77, 41);
            }
            else if (!e.Item.Enabled)
            {
                textColor = Color.FromArgb(145, 157, 173);
            }
            else
            {
                textColor = e.Item.Selected ? Blue : Ink;
            }

            Rectangle textBounds = e.TextRectangle;
            textBounds.Y = 0;
            textBounds.Height = e.Item.Height;
            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                textBounds,
                textColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(Line))
            {
                e.Graphics.DrawLine(pen, 12, y, Math.Max(12, e.Item.Width - 12), y);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle area = e.ImageRectangle;
            int size = Math.Max(13, Math.Min(area.Width, area.Height) - 3);
            Rectangle checkBounds = new Rectangle(
                area.Left + (area.Width - size) / 2,
                area.Top + (area.Height - size) / 2,
                size,
                size);

            SmoothingMode previousMode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (SolidBrush brush = new SolidBrush(Mint))
                {
                    e.Graphics.FillEllipse(brush, checkBounds);
                }

                float left = checkBounds.Left + checkBounds.Width * .27f;
                float middleX = checkBounds.Left + checkBounds.Width * .44f;
                float right = checkBounds.Left + checkBounds.Width * .75f;
                float middleY = checkBounds.Top + checkBounds.Height * .58f;
                using (Pen pen = new Pen(Color.White, Math.Max(1.5f, size * .11f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new PointF(left, middleY),
                        new PointF(middleX, checkBounds.Top + checkBounds.Height * .72f),
                        new PointF(right, checkBounds.Top + checkBounds.Height * .35f)
                    });
                }
            }
            finally
            {
                e.Graphics.SmoothingMode = previousMode;
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Selected ? Blue : InkSoft;
            base.OnRenderArrow(e);
        }

        private static void DrawStatusDot(Graphics graphics, Rectangle bounds, Color color)
        {
            int centerX = bounds.Left + 13;
            int centerY = bounds.Top + bounds.Height / 2;
            SmoothingMode previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (SolidBrush halo = new SolidBrush(Color.FromArgb(42, color)))
                using (SolidBrush dot = new SolidBrush(color))
                {
                    graphics.FillEllipse(halo, centerX - 6, centerY - 6, 12, 12);
                    graphics.FillEllipse(dot, centerX - 3, centerY - 3, 6, 6);
                }
            }
            finally
            {
                graphics.SmoothingMode = previousMode;
            }
        }

        private static bool IsErrorStatus(string text)
        {
            return !string.IsNullOrEmpty(text) &&
                text.StartsWith("갱신 실패", StringComparison.Ordinal);
        }

        private static Rectangle Inset(Rectangle rectangle, int left, int top, int right, int bottom)
        {
            return new Rectangle(
                rectangle.Left + left,
                rectangle.Top + top,
                Math.Max(1, rectangle.Width - left - right),
                Math.Max(1, rectangle.Height - top - bottom));
        }

        private static void FillRoundedRectangle(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            SmoothingMode previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (GraphicsPath path = CreateRoundedRectangle(bounds, radius))
                using (SolidBrush brush = new SolidBrush(color))
                {
                    graphics.FillPath(brush, path);
                }
            }
            finally
            {
                graphics.SmoothingMode = previousMode;
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
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
            public override Color CheckBackground { get { return Mint; } }
            public override Color CheckPressedBackground { get { return Mint; } }
            public override Color CheckSelectedBackground { get { return Mint; } }
        }
    }
}
