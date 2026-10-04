using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexUsageTray
{
    internal sealed class NumericTrayIcon : NativeWindow, IDisposable
    {
        private const uint IconId = 1;
        private const int CallbackMessage = 0x8001;
        private const uint NimAdd = 0;
        private const uint NimModify = 1;
        private const uint NimDelete = 2;
        private const uint NimSetFocus = 3;
        private const uint NimSetVersion = 4;
        private const uint NotifyIconVersion4 = 4;
        private const uint NifMessage = 0x01;
        private const uint NifIcon = 0x02;
        private const uint NifTip = 0x04;
        private const uint NifShowTip = 0x80;
        private const int NinSelect = 0x0400;
        private const int NinKeySelect = 0x0401;
        private const int WmContextMenu = 0x007B;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonUp = 0x0205;
        private const int WmDisplayChange = 0x007E;
        private const int WmSettingChange = 0x001A;
        private const int WmThemeChanged = 0x031A;
        private const int WmDpiChanged = 0x02E0;
        private const int SmCxSmallIcon = 49;

        private readonly int _taskbarCreatedMessage;
        private UsageDisplayMode _displayMode;
        private int? _fiveHourRemaining;
        private int? _sevenDayRemaining;
        private IconState _state = IconState.Loading;
        private IntPtr _iconHandle;
        private bool _added;
        private bool _version4;
        private bool _disposed;

        public event Action<Rectangle> LeftClick;
        public event Action<Rectangle> RightClick;

        public NumericTrayIcon(UsageDisplayMode displayMode)
        {
            _displayMode = NormalizeDisplayMode(displayMode);
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            // A hidden top-level window receives the Explorer recreation broadcast.
            // A message-only window would miss that broadcast.
            CreateHandle(new CreateParams
            {
                Caption = "Codex Usage Tray",
                Style = unchecked((int)0x80000000),
                ExStyle = 0x00000080,
                X = -32000,
                Y = -32000,
                Width = 1,
                Height = 1
            });
            RenderAndPublish();
        }

        public void Update(int? fiveHourRemaining, int? sevenDayRemaining, IconState state)
        {
            if (_disposed) return;
            _fiveHourRemaining = fiveHourRemaining;
            _sevenDayRemaining = sevenDayRemaining;
            _state = state;
            RenderAndPublish();
        }

        public void SetDisplayMode(UsageDisplayMode displayMode)
        {
            if (_disposed) return;
            _displayMode = NormalizeDisplayMode(displayMode);
            RenderAndPublish();
        }

        public void RestoreFocus()
        {
            if (_disposed || !_added) return;
            NotifyIconData data = CreateIconData(_iconHandle);
            Shell_NotifyIcon(NimSetFocus, ref data);
        }

        private void RenderAndPublish()
        {
            if (_disposed || Handle == IntPtr.Zero) return;

            IntPtr newIcon;
            using (Bitmap bitmap = RenderBitmap(GetIconSize(), _fiveHourRemaining,
                _sevenDayRemaining, _displayMode, _state, IsLightTaskbar()))
            {
                newIcon = bitmap.GetHicon();
            }

            IntPtr previousIcon = _iconHandle;
            try
            {
                NotifyIconData data = CreateIconData(newIcon);
                if (_added && !Shell_NotifyIcon(NimModify, ref data))
                {
                    _added = false;
                }
                if (!_added)
                {
                    _added = Shell_NotifyIcon(NimAdd, ref data);
                    if (_added)
                    {
                        data.Version = NotifyIconVersion4;
                        _version4 = Shell_NotifyIcon(NimSetVersion, ref data);
                    }
                }
                _iconHandle = newIcon;
                newIcon = IntPtr.Zero;
            }
            finally
            {
                if (newIcon != IntPtr.Zero) DestroyIcon(newIcon);
                if (previousIcon != IntPtr.Zero && previousIcon != _iconHandle) DestroyIcon(previousIcon);
            }
        }

        private NotifyIconData CreateIconData(IntPtr icon)
        {
            return new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf(typeof(NotifyIconData)),
                Window = Handle,
                Id = IconId,
                Flags = NifMessage | NifIcon | NifTip | NifShowTip,
                Callback = CallbackMessage,
                Icon = icon,
                Tip = BuildTooltip(_fiveHourRemaining, _sevenDayRemaining, _displayMode, _state),
                Info = string.Empty,
                InfoTitle = string.Empty
            };
        }

        internal static string FormatIconText(int? fiveHours, int? sevenDays,
            UsageDisplayMode mode, IconState state)
        {
            if (state == IconState.Error) return "!";
            if (state == IconState.Loading) return "…";
            int? remaining = NormalizeDisplayMode(mode) == UsageDisplayMode.SevenDays ? sevenDays : fiveHours;
            return remaining.HasValue ? Clamp(remaining.Value).ToString(CultureInfo.InvariantCulture) : "-";
        }

        internal static string BuildTooltip(int? fiveHours, int? sevenDays,
            UsageDisplayMode mode, IconState state)
        {
            string status = state == IconState.Loading ? "불러오는 중"
                : state == IconState.Error ? "갱신 실패"
                : state == IconState.Stale ? "갱신 실패 · 이전 값" : "최신 값";
            string tooltip = "Codex 남은 사용량\n5시간: " + FormatTooltipValue(fiveHours, state)
                + " · 7일: " + FormatTooltipValue(sevenDays, state)
                + "\n아이콘: " + (NormalizeDisplayMode(mode) == UsageDisplayMode.SevenDays ? "7일" : "5시간")
                + "\n" + status;
            return tooltip.Length <= 127 ? tooltip : tooltip.Substring(0, 127);
        }

        private static string FormatTooltipValue(int? remaining, IconState state)
        {
            if (state == IconState.Loading) return "불러오는 중";
            if (state == IconState.Error || !remaining.HasValue) return "정보 없음";
            return Clamp(remaining.Value).ToString(CultureInfo.InvariantCulture) + "% 남음";
        }

        private static UsageDisplayMode NormalizeDisplayMode(UsageDisplayMode mode)
        {
            return mode == UsageDisplayMode.SevenDays ? UsageDisplayMode.SevenDays : UsageDisplayMode.FiveHours;
        }

        private static int Clamp(int remaining)
        {
            return Math.Max(0, Math.Min(100, remaining));
        }

        internal static Bitmap RenderBitmap(int size, int? fiveHours, int? sevenDays,
            UsageDisplayMode mode, IconState state, bool lightTaskbar)
        {
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (FontFamily family = new FontFamily("Segoe UI"))
                using (GraphicsPath path = new GraphicsPath())
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    string text = FormatIconText(fiveHours, sevenDays, mode, state);
                    path.AddString(text, family, (int)FontStyle.Regular, size, PointF.Empty,
                        StringFormat.GenericTypographic);
                    RectangleF bounds = path.GetBounds();
                    float padding = size >= 24 ? 2f : 1f;
                    float contentHeight = size - padding * 2f - (state == IconState.Stale ? 2f : 0f);
                    float scale = Math.Min((size - padding * 2f) / bounds.Width, contentHeight / bounds.Height);
                    using (Matrix transform = new Matrix())
                    {
                        transform.Translate(-bounds.Left, -bounds.Top);
                        transform.Scale(scale, scale, MatrixOrder.Append);
                        transform.Translate((size - bounds.Width * scale) / 2f,
                            padding + (contentHeight - bounds.Height * scale) / 2f, MatrixOrder.Append);
                        path.Transform(transform);
                    }
                    Color color = lightTaskbar ? Color.FromArgb(24, 24, 24) : Color.White;
                    if (state == IconState.Error)
                    {
                        color = lightTaskbar ? Color.FromArgb(153, 27, 27) : Color.FromArgb(255, 150, 150);
                    }
                    using (SolidBrush brush = new SolidBrush(color))
                    {
                        graphics.FillPath(brush, path);
                    }
                    if (state == IconState.Stale)
                    {
                        using (SolidBrush warning = new SolidBrush(lightTaskbar
                            ? Color.FromArgb(133, 77, 14) : Color.FromArgb(255, 200, 87)))
                        {
                            graphics.FillRectangle(warning, padding, size - padding - 1f,
                                size - padding * 2f, 1f);
                        }
                    }
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        internal static bool IsLightTaskbar()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    return value is int && (int)value != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static int GetIconSize()
        {
            try
            {
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                uint dpi = taskbar == IntPtr.Zero ? 0 : GetDpiForWindow(taskbar);
                if (dpi > 0)
                {
                    return Math.Max(16, Math.Min(64, GetSystemMetricsForDpi(SmCxSmallIcon, dpi)));
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            return Math.Max(16, Math.Min(64, GetSystemMetrics(SmCxSmallIcon)));
        }

        private Rectangle GetAnchorRectangle(IntPtr coordinates, int notification)
        {
            NotifyIconIdentifier identifier = new NotifyIconIdentifier
            {
                Size = (uint)Marshal.SizeOf(typeof(NotifyIconIdentifier)),
                Window = Handle,
                Id = IconId
            };
            NativeRectangle bounds;
            if (Shell_NotifyIconGetRect(ref identifier, out bounds) == 0 &&
                bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
            {
                return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            }
            // Version 4 defines wParam coordinates for selection and mouse messages.
            // WM_CONTEXTMENU has undefined wParam, so use the cursor in that fallback.
            Point point = Cursor.Position;
            if (_version4 && (notification == NinSelect || notification == NinKeySelect ||
                (notification >= 0x0200 && notification <= 0x020E)))
            {
                long value = coordinates.ToInt64();
                point = new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff));
            }
            return new Rectangle(point, new Size(1, 1));
        }

        private void RaiseClick(Action<Rectangle> click, IntPtr coordinates, int notification)
        {
            if (click == null) return;
            Rectangle anchor = GetAnchorRectangle(coordinates, notification);
            SetForegroundWindow(Handle);
            try
            {
                click(anchor);
            }
            finally
            {
                if (Handle != IntPtr.Zero) PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (!_disposed && _taskbarCreatedMessage != 0 && message.Msg == _taskbarCreatedMessage)
            {
                _added = false;
                _version4 = false;
                RenderAndPublish();
                return;
            }
            if (!_disposed && message.Msg == CallbackMessage)
            {
                long payload = message.LParam.ToInt64();
                uint id = _version4 ? (uint)((payload >> 16) & 0xffff) : (uint)message.WParam.ToInt64();
                if (id != IconId) return;
                int notification = _version4 ? (int)(payload & 0xffff) : (int)payload;
                if ((_version4 && (notification == NinSelect || notification == NinKeySelect)) ||
                    (!_version4 && notification == WmLButtonUp))
                {
                    RaiseClick(LeftClick, message.WParam, notification);
                }
                else if ((_version4 && notification == WmContextMenu) ||
                    (!_version4 && notification == WmRButtonUp))
                {
                    RaiseClick(RightClick, message.WParam, notification);
                }
                return;
            }
            if (!_disposed && (message.Msg == WmSettingChange || message.Msg == WmDisplayChange ||
                message.Msg == WmThemeChanged || message.Msg == WmDpiChanged))
            {
                RenderAndPublish();
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_added && Handle != IntPtr.Zero)
            {
                NotifyIconData data = CreateIconData(_iconHandle);
                Shell_NotifyIcon(NimDelete, ref data);
            }
            _added = false;
            if (_iconHandle != IntPtr.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
            if (Handle != IntPtr.Zero) DestroyHandle();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id;
            public uint Flags;
            public int Callback;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State;
            public uint StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NotifyIconIdentifier
        {
            public uint Size;
            public IntPtr Window;
            public uint Id;
            public Guid Guid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier,
            out NativeRectangle rectangle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int index, uint dpi);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
