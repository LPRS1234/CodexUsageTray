using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsageTray
{
    internal sealed class CornerUsageCard : NativeWindow, IDisposable
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExLayered = 0x00080000;
        private const int SwpNoActivate = 0x0010;
        private const int SwpShowWindow = 0x0040;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonUp = 0x0205;
        private const int WmDisplayChange = 0x007E;
        private const int WmSettingChange = 0x001A;
        private const int WmDpiChanged = 0x02E0;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;
        private const int UlwAlpha = 0x00000002;

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        private readonly ContextMenuStrip _menu;
        private readonly System.Windows.Forms.Timer _positionTimer;
        private Image _logo;
        private int? _fiveHourRemaining;
        private int? _sevenDayRemaining;
        private IconState _state;
        private CardCorner _corner;
        private UsageDisplayMode _displayMode;
        private bool _disposed;

        public CornerUsageCard(ContextMenuStrip menu, string logoPath, CardCorner corner,
            UsageDisplayMode displayMode)
        {
            _menu = menu;
            _corner = corner;
            _displayMode = displayMode;
            if (File.Exists(logoPath))
            {
                using (Image source = Image.FromFile(logoPath))
                {
                    _logo = new Bitmap(source);
                }
            }

            CreateParams parameters = new CreateParams
            {
                Caption = "Codex Usage Card",
                Style = WsPopup,
                ExStyle = WsExToolWindow | WsExNoActivate | WsExLayered,
                X = -32000,
                Y = -32000,
                Width = 1,
                Height = 1
            };
            CreateHandle(parameters);

            _positionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _positionTimer.Tick += delegate { RenderAndPosition(); };
            _positionTimer.Start();
        }

        public void Update(int? fiveHourRemaining, int? sevenDayRemaining, IconState state)
        {
            _fiveHourRemaining = fiveHourRemaining;
            _sevenDayRemaining = sevenDayRemaining;
            _state = state;
            RenderAndPosition();
        }

        public void SetCorner(CardCorner corner)
        {
            _corner = corner;
            RenderAndPosition();
        }

        public void SetDisplayMode(UsageDisplayMode displayMode)
        {
            _displayMode = displayMode;
            RenderAndPosition();
        }

        private void RenderAndPosition()
        {
            if (_disposed || Handle == IntPtr.Zero)
            {
                return;
            }

            Screen screen = Screen.PrimaryScreen;
            if (screen == null)
            {
                return;
            }

            float scale = GetScale();
            int baseWidth = _displayMode == UsageDisplayMode.Both ? 188 : 136;
            int width = (int)Math.Round(baseWidth * scale);
            int height = (int)Math.Round(46f * scale);
            int margin = (int)Math.Round(10f * scale);
            Rectangle workArea = screen.WorkingArea;
            bool alignLeft = _corner == CardCorner.TopLeft || _corner == CardCorner.BottomLeft;
            bool alignTop = _corner == CardCorner.TopLeft || _corner == CardCorner.TopRight;
            int x = alignLeft ? workArea.Left + margin : workArea.Right - width - margin;
            int y = alignTop ? workArea.Top + margin : workArea.Bottom - height - margin;

            using (Bitmap bitmap = RenderBitmap(width, height, scale))
            {
                UpdateLayeredBitmap(bitmap, x, y);
            }
        }

        private Bitmap RenderBitmap(int width, int height, float scale)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                float outerInset = (float)Math.Round(2f * scale);
                RectangleF shadowBounds = new RectangleF(
                    outerInset,
                    outerInset + (float)Math.Round(scale),
                    width - outerInset * 2f,
                    height - outerInset * 2f - (float)Math.Round(scale));
                float radius = (float)Math.Round(11f * scale);
                using (GraphicsPath shadowPath = CreateRoundedRectangle(shadowBounds, radius))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(44, 0, 0, 0)))
                {
                    graphics.FillPath(shadowBrush, shadowPath);
                }

                RectangleF cardBounds = new RectangleF(
                    outerInset,
                    outerInset,
                    width - outerInset * 2f,
                    height - outerInset * 2f - (float)Math.Round(scale));
                using (GraphicsPath cardPath = CreateRoundedRectangle(cardBounds, radius))
                using (SolidBrush cardBrush = new SolidBrush(Color.FromArgb(255, 26, 24, 36)))
                using (Pen borderPen = new Pen(Color.FromArgb(62, 255, 255, 255),
                    Math.Max(1f, (float)Math.Round(scale))))
                {
                    graphics.FillPath(cardBrush, cardPath);
                    graphics.DrawPath(borderPen, cardPath);
                }

                int logoSize = (int)Math.Round(28f * scale);
                int logoX = (int)Math.Round(10f * scale);
                int logoY = (height - logoSize) / 2 - (int)Math.Round(scale);
                if (_logo != null)
                {
                    graphics.DrawImage(_logo, new Rectangle(logoX, logoY, logoSize, logoSize));
                }
                else
                {
                    using (SolidBrush fallback = new SolidBrush(Color.FromArgb(91, 91, 255)))
                    {
                        graphics.FillEllipse(fallback, logoX, logoY, logoSize, logoSize);
                    }
                }

                float textX = (float)Math.Round(47f * scale);
                using (Font labelFont = new Font("Segoe UI Semibold",
                    Math.Max(9f, (float)Math.Round(9f * scale)), FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font valueFont = new Font("Segoe UI Semibold",
                    Math.Max(18f, (float)Math.Round(18f * scale)), FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(228, 225, 224, 235)))
                using (SolidBrush valueBrush = new SolidBrush(Color.White))
                {
                    float rightPadding = (float)Math.Round(10f * scale);
                    float contentWidth = width - textX - rightPadding;
                    if (_state == IconState.Error)
                    {
                        DrawMetric(graphics, labelFont, valueFont, labelBrush, valueBrush,
                            "CODEX 갱신 실패", "!", null, textX, contentWidth, height, scale);
                    }
                    else if (_displayMode == UsageDisplayMode.Both)
                    {
                        float gap = (float)Math.Round(10f * scale);
                        float columnWidth = (float)Math.Floor((contentWidth - gap) / 2f);
                        string fiveHourText = ToPercentText(_fiveHourRemaining, _state);
                        string sevenDayText = ToPercentText(_sevenDayRemaining, _state);
                        DrawMetric(graphics, labelFont, valueFont, labelBrush, valueBrush,
                            "5시간", fiveHourText, _fiveHourRemaining,
                            textX, columnWidth, height, scale);
                        DrawMetric(graphics, labelFont, valueFont, labelBrush, valueBrush,
                            "7일", sevenDayText, _sevenDayRemaining,
                            textX + columnWidth + gap, columnWidth, height, scale);
                    }
                    else
                    {
                        bool showFiveHours = _displayMode == UsageDisplayMode.FiveHours;
                        int? remaining = showFiveHours ? _fiveHourRemaining : _sevenDayRemaining;
                        DrawMetric(graphics, labelFont, valueFont, labelBrush, valueBrush,
                            showFiveHours ? "5시간" : "7일",
                            ToPercentText(remaining, _state), remaining,
                            textX, contentWidth, height, scale);
                    }
                }

                if (_state == IconState.Stale)
                {
                    float dotSize = 5f * scale;
                    using (SolidBrush warningBrush = new SolidBrush(Color.FromArgb(245, 158, 11)))
                    {
                        graphics.FillEllipse(warningBrush,
                            width - 12f * scale,
                            8f * scale,
                            dotSize,
                            dotSize);
                    }
                }
            }

            return bitmap;
        }

        private static string ToPercentText(int? remainingPercent, IconState state)
        {
            if (state == IconState.Loading || !remainingPercent.HasValue)
            {
                return "--";
            }

            return remainingPercent.Value.ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static void DrawMetric(Graphics graphics, Font labelFont, Font valueFont,
            Brush labelBrush, Brush valueBrush, string labelText, string valueText,
            int? remainingPercent, float x, float width, int height, float scale)
        {
            float labelY = (float)Math.Round(6f * scale);
            float valueY = (float)Math.Round(16f * scale);
            graphics.DrawString(labelText, labelFont, labelBrush,
                new PointF(x, labelY), StringFormat.GenericTypographic);
            graphics.DrawString(valueText, valueFont, valueBrush,
                new PointF(x, valueY), StringFormat.GenericTypographic);

            float trackY = (float)Math.Round(height - 7f * scale);
            float trackHeight = Math.Max(2f, (float)Math.Round(2f * scale));
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
            {
                graphics.FillRectangle(trackBrush, x, trackY, width, trackHeight);
            }

            if (!remainingPercent.HasValue)
            {
                return;
            }

            float progressWidth = Math.Max(trackHeight,
                width * Math.Max(0, Math.Min(100, remainingPercent.Value)) / 100f);
            Color progressStart = remainingPercent.Value <= 20
                ? Color.FromArgb(255, 244, 114, 94)
                : Color.FromArgb(255, 147, 139, 255);
            Color progressEnd = remainingPercent.Value <= 20
                ? Color.FromArgb(255, 255, 178, 80)
                : Color.FromArgb(255, 68, 96, 246);
            using (LinearGradientBrush progressBrush = new LinearGradientBrush(
                new PointF(x, trackY),
                new PointF(x + Math.Max(1f, progressWidth), trackY),
                progressStart,
                progressEnd))
            {
                graphics.FillRectangle(progressBrush, x, trackY, progressWidth, trackHeight);
            }
        }

        private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = Math.Max(1f, radius * 2f);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private void UpdateLayeredBitmap(Bitmap bitmap, int x, int y)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memoryDc, bitmapHandle);
                NativePoint destination = new NativePoint(x, y);
                NativeSize size = new NativeSize(bitmap.Width, bitmap.Height);
                NativePoint source = new NativePoint(0, 0);
                BlendFunction blend = new BlendFunction
                {
                    BlendOp = AcSrcOver,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AcSrcAlpha
                };

                UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc,
                    ref source, 0, ref blend, UlwAlpha);
                SetWindowPos(Handle, HwndTopmost, x, y, bitmap.Width, bitmap.Height,
                    SwpNoActivate | SwpShowWindow);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldBitmap);
                }
                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static float GetScale()
        {
            try
            {
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                uint dpi = taskbar == IntPtr.Zero ? 0 : GetDpiForWindow(taskbar);
                if (dpi > 0)
                {
                    return dpi / 96f;
                }
            }
            catch
            {
            }

            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                return graphics.DpiX / 96f;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmLButtonUp || message.Msg == WmRButtonUp)
            {
                SetForegroundWindow(Handle);
                _menu.Show(Cursor.Position);
                return;
            }

            if (message.Msg == WmDisplayChange || message.Msg == WmSettingChange || message.Msg == WmDpiChanged)
            {
                RenderAndPosition();
            }

            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _positionTimer.Stop();
            _positionTimer.Dispose();
            if (_logo != null)
            {
                _logo.Dispose();
                _logo = null;
            }
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
            public NativePoint(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
            public NativeSize(int width, int height) { Width = width; Height = height; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr graphicsObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr graphicsObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc,
            ref NativePoint destination, ref NativeSize size, IntPtr sourceDc, ref NativePoint source,
            int colorKey, ref BlendFunction blend, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
            int width, int height, int flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
    }

}
