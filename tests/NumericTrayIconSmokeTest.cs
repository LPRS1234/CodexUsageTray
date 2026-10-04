using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsageTray.Tests
{
    internal static class NumericTrayIconSmokeTest
    {
        private static Type _iconType;

        [STAThread]
        public static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                _iconType = typeof(UsageDisplayMode).Assembly.GetType("CodexUsageTray.NumericTrayIcon");
                Assert(_iconType != null, "native numerical tray icon is missing");
                VerifyDisplayedValues();
                VerifyTooltip();
                VerifyRendering();
                VerifyNativeLifecycleAndCallbacks();
                Console.WriteLine("Numeric tray icon smoke test passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception is TargetInvocationException
                    ? exception.InnerException.Message : exception.Message);
                return 1;
            }
        }

        private static void VerifyDisplayedValues()
        {
            AssertText(0, 100, UsageDisplayMode.FiveHours, IconState.Normal, "0", "zero remaining");
            AssertText(0, 100, UsageDisplayMode.SevenDays, IconState.Normal, "100", "selected weekly value");
            AssertText(0, 100, UsageDisplayMode.Both, IconState.Normal, "0", "legacy both selects five hours");
            AssertText(-1, 101, UsageDisplayMode.FiveHours, IconState.Normal, "0", "lower boundary clamps");
            AssertText(-1, 101, UsageDisplayMode.SevenDays, IconState.Normal, "100", "upper boundary clamps");
            AssertText(null, 100, UsageDisplayMode.FiveHours, IconState.Normal, "-", "missing selected period");
            AssertText(0, 100, UsageDisplayMode.FiveHours, IconState.Loading, "…", "loading hides old value");
            AssertText(0, 100, UsageDisplayMode.SevenDays, IconState.Error, "!", "error marker");
            AssertText(0, 100, UsageDisplayMode.SevenDays, IconState.Stale, "100", "stale retains cached value");
        }

        private static void AssertText(int? fiveHours, int? sevenDays, UsageDisplayMode mode,
            IconState state, string expected, string description)
        {
            string actual = (string)InvokeStatic("FormatIconText", fiveHours, sevenDays, mode, state);
            Assert(actual == expected, description);
        }

        private static void VerifyTooltip()
        {
            string normal = Tooltip(0, 100, UsageDisplayMode.SevenDays, IconState.Normal);
            Assert(normal.Contains("5시간: 0% 남음") && normal.Contains("7일: 100% 남음"),
                "tooltip includes both remaining periods");
            Assert(normal.Contains("아이콘: 7일"), "tooltip identifies selected period");
            Assert(Tooltip(0, 100, UsageDisplayMode.Both, IconState.Normal).Contains("아이콘: 5시간"),
                "tooltip normalizes legacy both mode");
            Assert(Tooltip(null, 100, UsageDisplayMode.FiveHours, IconState.Normal).Contains("5시간: 정보 없음"),
                "tooltip marks missing period");
            Assert(Tooltip(0, 100, UsageDisplayMode.FiveHours, IconState.Loading).Contains("불러오는 중"),
                "tooltip marks loading");
            string stale = Tooltip(0, 100, UsageDisplayMode.FiveHours, IconState.Stale);
            Assert(stale.Contains("갱신 실패") && stale.Contains("이전 값") && stale.Contains("7일: 100% 남음"),
                "tooltip distinguishes cached data after refresh failure");
            Assert(Tooltip(null, null, UsageDisplayMode.FiveHours, IconState.Error).Contains("갱신 실패"),
                "tooltip marks error");
            foreach (IconState state in Enum.GetValues(typeof(IconState)))
            {
                Assert(Tooltip(int.MinValue, int.MaxValue, UsageDisplayMode.SevenDays, state).Length <= 127,
                    "tooltip stays within native buffer");
            }
        }

        private static string Tooltip(int? fiveHours, int? sevenDays, UsageDisplayMode mode, IconState state)
        {
            return (string)InvokeStatic("BuildTooltip", fiveHours, sevenDays, mode, state);
        }

        private static void VerifyRendering()
        {
            foreach (int size in new[] { 16, 24, 32 })
            {
                using (Bitmap zero = Render(size, 0, IconState.Normal, false))
                using (Bitmap full = Render(size, 100, IconState.Normal, false))
                using (Bitmap missing = Render(size, null, IconState.Normal, false))
                using (Bitmap loading = Render(size, null, IconState.Loading, false))
                using (Bitmap error = Render(size, null, IconState.Error, false))
                using (Bitmap light = Render(size, 100, IconState.Normal, true))
                using (Bitmap stale = Render(size, 100, IconState.Stale, false))
                {
                    Assert(HasVisiblePixels(full) && HasVisiblePixels(missing) && HasVisiblePixels(loading) &&
                        HasVisiblePixels(error), "all value states render visible glyphs");
                    Assert(!SamePixels(zero, full) && !SamePixels(missing, loading) && !SamePixels(loading, error),
                        "rendered value and status glyphs differ");
                    Assert(!SamePixels(full, light), "light taskbar uses contrasting text");
                    Assert(!SamePixels(full, stale), "stale value has a visible warning");
                    Assert(full.GetPixel(0, 0).A == 0 && full.GetPixel(size - 1, size - 1).A == 0,
                        "icon preserves transparent taskbar background");
                }
            }
        }

        private static Bitmap Render(int size, int? remaining, IconState state, bool lightTaskbar)
        {
            return (Bitmap)InvokeStatic("RenderBitmap", size, remaining, (int?)100,
                UsageDisplayMode.FiveHours, state, lightTaskbar);
        }

        private static bool HasVisiblePixels(Bitmap bitmap)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).A > 0) return true;
                }
            }
            return false;
        }

        private static bool SamePixels(Bitmap first, Bitmap second)
        {
            for (int y = 0; y < first.Height; y++)
            {
                for (int x = 0; x < first.Width; x++)
                {
                    if (first.GetPixel(x, y) != second.GetPixel(x, y)) return false;
                }
            }
            return true;
        }

        private static void VerifyNativeLifecycleAndCallbacks()
        {
            object instance = Activator.CreateInstance(_iconType, BindingFlags.Instance | BindingFlags.Public,
                null, new object[] { UsageDisplayMode.Both }, null);
            NativeWindow window = (NativeWindow)instance;
            IDisposable disposable = (IDisposable)instance;
            IntPtr originalHandle = window.Handle;
            int leftClicks = 0;
            int rightClicks = 0;
            Rectangle lastAnchor = Rectangle.Empty;
            Action<Rectangle> left = delegate(Rectangle anchor) { leftClicks++; lastAnchor = anchor; };
            Action<Rectangle> right = delegate(Rectangle anchor) { rightClicks++; lastAnchor = anchor; };
            _iconType.GetEvent("LeftClick").AddEventHandler(instance, left);
            _iconType.GetEvent("RightClick").AddEventHandler(instance, right);
            try
            {
                uint iconId = (uint)_iconType.GetField("IconId", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetRawConstantValue();
                int callback = (int)_iconType.GetField("CallbackMessage", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetRawConstantValue();
                NotifyIconIdentifier identifier = new NotifyIconIdentifier();
                identifier.Size = (uint)Marshal.SizeOf(typeof(NotifyIconIdentifier));
                identifier.Window = window.Handle;
                identifier.Id = iconId;
                NativeRectangle rectangle;
                Assert(Shell_NotifyIconGetRect(ref identifier, out rectangle) == 0,
                    "icon is registered in the real notification area");
                Rectangle expectedAnchor = Rectangle.FromLTRB(rectangle.Left, rectangle.Top,
                    rectangle.Right, rectangle.Bottom);

                SendCallback(originalHandle, callback, iconId + 1, 0x0400);
                Assert(leftClicks == 0, "callback ignores another icon identity");
                SendCallback(originalHandle, callback, iconId, 0x0400);
                SendCallback(originalHandle, callback, iconId, 0x0401);
                SendCallback(originalHandle, callback, iconId, 0x007B);
                SendCallback(originalHandle, callback, iconId, 0x0202);
                SendCallback(originalHandle, callback, iconId, 0x0205);
                Assert(leftClicks == 2 && rightClicks == 1, "mouse and keyboard activation fire once");
                Assert(lastAnchor == expectedAnchor, "popup anchor uses the shell icon bounds");

                InvokeInstance(instance, "Update", (int?)0, (int?)100, IconState.Normal);
                InvokeInstance(instance, "SetDisplayMode", UsageDisplayMode.SevenDays);
                IntPtr currentIcon = (IntPtr)_iconType.GetField("_iconHandle", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(instance);
                uint baseline = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                for (int i = 0; i < 80; i++)
                {
                    InvokeInstance(instance, "Update", (int?)(i % 2 == 0 ? 0 : 100), (int?)100, IconState.Stale);
                }
                Assert(GetGuiResources(Process.GetCurrentProcess().Handle, 1) <= baseline + 4,
                    "repeated icon updates release drawing resources");
                IconInfo unused;
                Assert(!GetIconInfo(currentIcon, out unused), "replaced native icon handle is released");
            }
            finally
            {
                disposable.Dispose();
                disposable.Dispose();
            }
            Assert(window.Handle == IntPtr.Zero && !IsWindow(originalHandle), "dispose destroys hidden callback window");
            InvokeInstance(instance, "Update", null, null, IconState.Error);
            InvokeInstance(instance, "SetDisplayMode", UsageDisplayMode.FiveHours);
            Assert(window.Handle == IntPtr.Zero, "disposed icon is not recreated");
        }

        private static void SendCallback(IntPtr handle, int callback, uint iconId, int notification)
        {
            // Signed virtual-screen coordinates exercise the version 4 payload layout.
            int coordinates = unchecked((int)((uint)(ushort)-120 | ((uint)(ushort)-80 << 16)));
            int payload = unchecked((int)((iconId << 16) | (uint)notification));
            SendMessage(handle, callback, new IntPtr(coordinates), new IntPtr(payload));
        }

        private static object InvokeStatic(string name, params object[] arguments)
        {
            return _iconType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
        }

        private static void InvokeInstance(object instance, string name, params object[] arguments)
        {
            _iconType.GetMethod(name).Invoke(instance, arguments);
        }

        private static void Assert(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Smoke test failed: " + description);
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

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            public int IsIcon;
            public uint HotspotX;
            public uint HotspotY;
            public IntPtr MaskBitmap;
            public IntPtr ColorBitmap;
        }

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier,
            out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr process, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr window);
    }
}
