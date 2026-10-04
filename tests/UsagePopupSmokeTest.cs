using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsageTray.Tests
{
    internal static class UsagePopupSmokeTest
    {
        [STAThread]
        public static int Main()
        {
            try
            {
                DpiAwareness.Enable();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Application.EnableVisualStyles();
                Type popupType = typeof(RateLimitWindow).Assembly.GetType("CodexUsageTray.UsagePopupForm");
                Assert(popupType != null, "usage flyout is missing");
                VerifyRepeatedLayoutAndPainting(popupType);
                VerifyValuesAndActions(popupType);
                VerifyTaskbarIconResources(popupType);
                VerifyPlacement(popupType);
                VerifyTaskbarLifecycle(popupType);
                VerifyApplicationShutdown();
                Console.WriteLine("Usage popup smoke test passed.");
                return 0;
            }
            catch (Exception exception)
            {
                if (exception is TargetInvocationException && exception.InnerException != null)
                {
                    exception = exception.InnerException;
                }
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static void VerifyRepeatedLayoutAndPainting(Type popupType)
        {
            using (Form popup = CreatePopup(popupType))
            {
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                foreach (float scale in new[] { 1f, 1f, 0.75f, 0.8f, 1.25f, 1.25f, 1.5f, 1.5f, 2f, 2f, 1f })
                {
                    Invoke(popup, "ApplyLayout", scale, workArea.Size);
                    VerifyLabelPainting(popup);
                }
                Rectangle anchor = new Rectangle(workArea.Right - 28, workArea.Bottom, 24, 24);
                for (int index = 0; index < 5; index++)
                {
                    Invoke(popup, "ShowAt", anchor);
                    VerifyLabelPainting(popup);
                    using (Bitmap bitmap = new Bitmap(popup.Width, popup.Height))
                    {
                        popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    }
                    popup.Hide();
                }
            }
        }

        private static void VerifyLabelPainting(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                Label label = control as Label;
                if (label != null)
                {
                    Assert(TextRenderer.MeasureText(label.Text, label.Font).Height > 0,
                        "label text must remain drawable after repeated layout and reopening");
                }
                VerifyLabelPainting(control);
            }
        }

        private static void VerifyValuesAndActions(Type popupType)
        {
            using (Form popup = CreatePopup(popupType))
            {
                Assert(popup.ShowInTaskbar && popup.FormBorderStyle == FormBorderStyle.FixedSingle &&
                    popup.MinimizeBox && !popup.MaximizeBox && !popup.TopMost,
                    "usage window must provide a normal taskbar button and window controls");
                Assert(Find(popup, "FiveHourValue").Text == "--" &&
                    Find(popup, "SevenDayValue").Text == "--", "missing values must stay unknown");
                Assert(Find(popup, "FiveHourProgress") is ProgressBar &&
                    !Find(popup, "FiveHourProgress").TabStop, "usage bars must be accessible and read-only");

                DateTime successfulUpdate = DateTime.Now.AddMinutes(-7);
                long reset = DateTimeOffset.Now.AddHours(2).ToUnixTimeSeconds();
                RateLimitWindow five = new RateLimitWindow("synthetic", "primary", 78, 300, reset);
                RateLimitWindow seven = new RateLimitWindow("synthetic", "secondary", 64, 10080, reset);
                Invoke(popup, "UpdateUsage", five, seven, IconState.Normal, "", successfulUpdate);
                Assert(popup.Icon != null && popup.Text.Contains("5시간 78%"),
                    "taskbar icon and title must show the selected remaining usage");
                Invoke(popup, "SetDisplayMode", UsageDisplayMode.SevenDays);
                Assert(popup.Text.Contains("7일 64%"), "taskbar display must follow the selected period");
                Invoke(popup, "SetDisplayMode", UsageDisplayMode.FiveHours);
                Assert(Find(popup, "FiveHourValue").Text == "78%" &&
                    Find(popup, "SevenDayValue").Text == "64%", "both remaining usage windows must be visible");
                Assert(((ProgressBar)Find(popup, "FiveHourProgress")).Value == 78 &&
                    ((ProgressBar)Find(popup, "SevenDayProgress")).Value == 64, "bars must show remaining rather than used usage");
                Assert(Find(popup, "FiveHourReset").Text.Contains("초기화"), "reset date must be displayed");

                int refresh = 0, dashboard = 0, autoStart = 0, settings = 0;
                Subscribe(popup, "RefreshRequested", delegate { refresh++; });
                Subscribe(popup, "DashboardRequested", delegate { dashboard++; });
                Subscribe(popup, "AutoStartRequested", delegate { autoStart++; });
                Subscribe(popup, "SettingsRequested", delegate { settings++; });
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                Invoke(popup, "ShowAt", new Rectangle(workArea.Right - 28, workArea.Bottom, 24, 24));
                Application.DoEvents();
                Panel host = (Panel)popup.Controls[0];
                Assert(!host.HorizontalScroll.Visible && !host.VerticalScroll.Visible,
                    "normal DPI layout must not add scrollbars");
                Click(popup, "RefreshButton");
                Invoke(popup, "SetRefreshing", true);
                Click(popup, "RefreshButton");
                Assert(refresh == 1 && !Find(popup, "RefreshButton").Enabled,
                    "refresh must be disabled while a refresh is pending");
                Invoke(popup, "SetRefreshing", false);
                Assert(Find(popup, "RefreshButton").Enabled, "refresh must become available after completion");
                Click(popup, "AutoStartButton");
                Assert(autoStart == 1 && Find(popup, "AutoStartLabel").Text.Contains("끔"),
                    "auto-start must wait for the persisted setting");
                Invoke(popup, "SetAutoStart", true);
                Assert(Find(popup, "AutoStartLabel").Text.Contains("켬") &&
                    Find(popup, "AutoStartButton").AccessibleDescription.Contains("켬"),
                    "auto-start state must be visible and accessible");

                Invoke(popup, "UpdateUsage", five, seven, IconState.Stale, "synthetic failure", successfulUpdate);
                Assert(Find(popup, "FiveHourValue").Text == "78%" &&
                    Find(popup, "StatusLabel").Text.Contains("마지막") &&
                    Find(popup, "StatusLabel").AccessibleDescription.Contains(successfulUpdate.ToString("M/d HH:mm")),
                    "failure must retain cached usage and the original successful timestamp");
                Invoke(popup, "UpdateUsage", null, null, IconState.Error, "synthetic failure", null);
                Assert(Find(popup, "FiveHourValue").Text == "--" &&
                    Find(popup, "StatusLabel").Text.Contains("실패"), "first-load failure must not invent usage");
                Invoke(popup, "UpdateUsage", five, seven, IconState.Normal, "", successfulUpdate);
                Invoke(popup, "ApplyPalette", false);
                Assert(Find(popup, "AutoStartButton").BackColor == Color.FromArgb(0, 103, 192),
                    "enabled auto-start tile must use the light Windows accent");
                SavePreview(popup, "usage-popup-synthetic.png");
                Invoke(popup, "ApplyPalette", true);
                Assert(Find(popup, "AutoStartButton").BackColor == Color.FromArgb(117, 187, 255) &&
                    Find(popup, "FiveHourProgress").ForeColor == Color.FromArgb(117, 187, 255),
                    "dark theme must use the approved accessible blue accent");
                SavePreview(popup, "usage-popup-dark-synthetic.png");
                Click(popup, "DashboardButton");
                Assert(dashboard == 1 && popup.Visible && popup.WindowState == FormWindowState.Minimized,
                    "dashboard action must minimize while preserving the taskbar button");
                Invoke(popup, "ShowAt", new Rectangle(workArea.Right - 28, workArea.Bottom, 24, 24));
                Click(popup, "SettingsButton");
                Assert(settings == 1 && popup.Visible && popup.WindowState == FormWindowState.Normal,
                    "settings action must preserve the usage window");
            }
        }

        private static void VerifyTaskbarIconResources(Type popupType)
        {
            using (Form popup = CreatePopup(popupType))
            {
                RateLimitWindow five = new RateLimitWindow("synthetic", "primary", 78, 300, 0);
                RateLimitWindow seven = new RateLimitWindow("synthetic", "secondary", 64, 10080, 0);
                Invoke(popup, "UpdateUsage", five, seven, IconState.Normal, "", DateTime.Now);
                uint baselineUser = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                uint baselineGdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                for (int index = 0; index < 80; index++)
                {
                    Invoke(popup, "SetDisplayMode", index % 2 == 0 ? UsageDisplayMode.FiveHours : UsageDisplayMode.SevenDays);
                    Invoke(popup, "UpdateUsage", five, seven, IconState.Stale, "synthetic failure", DateTime.Now);
                }
                Assert(GetGuiResources(Process.GetCurrentProcess().Handle, 1) <= baselineUser + 6 &&
                    GetGuiResources(Process.GetCurrentProcess().Handle, 0) <= baselineGdi + 6,
                    "repeated taskbar icon changes must release native icon and drawing resources");
            }
        }

        private static void VerifyPlacement(Type popupType)
        {
            MethodInfo calculate = popupType.GetMethod("CalculateBounds", BindingFlags.Static | BindingFlags.NonPublic);
            Rectangle area = new Rectangle(0, 0, 1920, 1040);
            Rectangle centered = (Rectangle)calculate.Invoke(null, new object[]
            {
                new Rectangle(948, 1040, 24, 24), new Size(360, 400), area, 12
            });
            Assert(centered.Left == 780 && centered.Bottom == 1028,
                "flyout must be centered directly above an interior tray icon");
            Rectangle leftEdge = (Rectangle)calculate.Invoke(null, new object[]
            {
                new Rectangle(0, 1040, 24, 24), new Size(360, 400), area, 12
            });
            Rectangle rightEdge = (Rectangle)calculate.Invoke(null, new object[]
            {
                new Rectangle(1896, 1040, 24, 24), new Size(360, 400), area, 12
            });
            Assert(leftEdge.Left == 0 && rightEdge.Right == 1920,
                "centered flyout must stay on screen at both horizontal edges");
            Rectangle offset = (Rectangle)calculate.Invoke(null, new object[]
            {
                new Rectangle(-972, 1040, 24, 24), new Size(360, 400), new Rectangle(-1920, 100, 1920, 940), 12
            });
            Assert(offset.Left == -1140, "flyout must center on negative-origin monitors");
            Rectangle[] workAreas = new[]
            {
                new Rectangle(0, 0, 1920, 1040),
                new Rectangle(-1920, 100, 1920, 940),
                new Rectangle(48, -1080, 1872, 1080),
                new Rectangle(-640, -480, 320, 440),
                new Rectangle(0, 48, 220, 250)
            };
            foreach (Rectangle workArea in workAreas)
            {
                Rectangle[] anchors = new[]
                {
                    new Rectangle(workArea.Right - 28, workArea.Bottom, 24, 24),
                    new Rectangle(workArea.Left - 24, workArea.Top + 8, 24, 24),
                    new Rectangle(workArea.Left + 8, workArea.Top - 24, 24, 24)
                };
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                {
                    using (Form popup = CreatePopup(popupType))
                    {
                        Invoke(popup, "ApplyLayout", scale, workArea.Size);
                        foreach (Rectangle anchor in anchors)
                        {
                            Rectangle bounds = (Rectangle)popupType.GetMethod("CalculateBounds",
                                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                                new object[] { anchor, popup.Size, workArea, (int)Math.Round(12f * scale) });
                            Assert(workArea.Contains(bounds) && !bounds.IntersectsWith(anchor),
                                "flyout placement must stay inside the selected monitor working area");
                        }
                        ProgressBar bar = (ProgressBar)Find(popup, "FiveHourProgress");
                        Assert(bar.Width > 0 && bar.Right <= bar.Parent.ClientSize.Width,
                            "scaled usage bar must fit the content width");
                        Control settings = Find(popup, "SettingsButton");
                        Assert(settings.Right <= settings.Parent.ClientSize.Width,
                            "scaled settings button must fit narrow content");
                    }
                }
            }
        }

        private static void VerifyTaskbarLifecycle(Type popupType)
        {
            using (Form popup = CreatePopup(popupType))
            {
                int keyboardDismissals = 0;
                Subscribe(popup, "KeyboardDismissed", delegate { keyboardDismissals++; });
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                Rectangle anchor = new Rectangle(workArea.Right - 28, workArea.Bottom, 24, 24);
                Invoke(popup, "ShowAt", anchor);
                Application.DoEvents();
                Assert(popup.Visible && workArea.Contains(popup.Bounds), "usage window must fit the working area");
                int extendedStyle = GetWindowLong(popup.Handle, -20);
                Assert((extendedStyle & 0x00000080) == 0 && (extendedStyle & 0x00040000) != 0,
                    "native window must be eligible for the taskbar and Alt+Tab");
                SendMessage(popup.Handle, 0x0006, IntPtr.Zero, IntPtr.Zero);
                Assert(popup.Visible && popup.WindowState == FormWindowState.Normal,
                    "deactivation must not remove the usage window or its taskbar button");
                Rectangle normalBounds = popup.Bounds;
                popup.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                SendMessage(popup.Handle, 0x031A, IntPtr.Zero, IntPtr.Zero);
                Assert(popup.Visible && popup.ShowInTaskbar && popup.WindowState == FormWindowState.Minimized,
                    "minimizing and theme changes must preserve the taskbar button");
                Assert(popup.RestoreBounds == normalBounds, "minimized theme changes must preserve restore bounds");
                popup.WindowState = FormWindowState.Normal;
                Application.DoEvents();
                VerifyLabelPainting(popup);
                Assert(popup.Visible && workArea.Contains(popup.Bounds), "restoring must reopen the usage window");
                Message key = Message.Create(popup.Handle, 0x0100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                object[] arguments = new object[] { key, Keys.Escape };
                popupType.GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(popup, arguments);
                Assert(popup.Visible && popup.WindowState == FormWindowState.Minimized && keyboardDismissals == 1,
                    "Escape must minimize while preserving the taskbar button");
                Invoke(popup, "ShowAt", anchor);
                popup.Close();
                Assert(popup.IsDisposed, "closing the usage window must dispose it");
            }
        }

        private static void VerifyApplicationShutdown()
        {
            using (TrayApplicationContext context = new TrayApplicationContext())
            {
                bool exited = false;
                context.ThreadExit += delegate { exited = true; };
                Form window = Application.OpenForms["UsagePopup"];
                Assert(window != null && window.ShowInTaskbar && window.Visible,
                    "application must expose its usage window on the taskbar at startup");
                window.Close();
                Assert(exited && window.IsDisposed,
                    "closing the usage window must stop the application context");
            }
        }

        private static Form CreatePopup(Type type)
        {
            return (Form)Activator.CreateInstance(type, true);
        }

        private static object Invoke(Form popup, string method, params object[] arguments)
        {
            return popup.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic).Invoke(popup, arguments);
        }

        private static void Subscribe(Form popup, string eventName, EventHandler handler)
        {
            popup.GetType().GetEvent(eventName).AddEventHandler(popup, handler);
        }

        private static Control Find(Control root, string name)
        {
            Control[] controls = root.Controls.Find(name, true);
            Assert(controls.Length == 1, "missing named flyout control: " + name);
            return controls[0];
        }

        private static void Click(Form popup, string name)
        {
            ((Button)Find(popup, name)).PerformClick();
            Application.DoEvents();
        }

        private static void SavePreview(Form popup, string fileName)
        {
            string directory = Environment.GetEnvironmentVariable("CODEX_USAGE_POPUP_PREVIEW_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            using (Bitmap bitmap = new Bitmap(popup.Width, popup.Height))
            {
                popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, fileName), ImageFormat.Png);
            }
        }

        private static void Assert(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Smoke test failed: " + description);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr process, uint flags);
    }
}
