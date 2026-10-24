using System;
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
                VerifyValuesAndActions(popupType);
                VerifyRepeatedLayoutAndPainting(popupType);
                VerifySettingsNavigationAndActions(popupType);
                VerifyPlacement(popupType);
                VerifyDismissalAndReopening(popupType);
                VerifyTrayApplicationLifecycle();
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
                    using (Bitmap bitmap = new Bitmap(popup.ClientSize.Width, popup.ClientSize.Height))
                    {
                        popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    }
                    Click(popup, "SettingsButton");
                    VerifyLabelPainting(popup);
                    using (Bitmap bitmap = new Bitmap(popup.ClientSize.Width, popup.ClientSize.Height))
                    {
                        popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    }
                    Click(popup, "SettingsBackButton");
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
                Assert(!popup.ShowInTaskbar && popup.FormBorderStyle == FormBorderStyle.None,
                    "flyout should not create a taskbar button or window frame");
                Assert(Find(popup, "FiveHourValue").Text == "--" &&
                    Find(popup, "SevenDayValue").Text == "--", "missing values must stay unknown");
                Assert(Find(popup, "FiveHourProgress") is ProgressBar &&
                    !Find(popup, "FiveHourProgress").TabStop, "usage bars must be accessible and read-only");

                DateTime successfulUpdate = DateTime.Now.AddMinutes(-7);
                long reset = DateTimeOffset.Now.AddHours(2).ToUnixTimeSeconds();
                RateLimitWindow five = new RateLimitWindow("synthetic", "primary", 78, 300, reset);
                RateLimitWindow seven = new RateLimitWindow("synthetic", "secondary", 64, 10080, reset);
                Invoke(popup, "UpdateUsage", five, seven, IconState.Normal, "", successfulUpdate);
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
                Assert(dashboard == 1 && !popup.Visible, "dashboard action must dismiss the flyout");
                Invoke(popup, "ShowAt", new Rectangle(workArea.Right - 28, workArea.Bottom, 24, 24));
                IntPtr handle = popup.Handle;
                Rectangle bounds = popup.Bounds;
                int hidden = 0;
                popup.VisibleChanged += delegate { if (!popup.Visible) hidden++; };
                Click(popup, "SettingsButton");
                Assert(popup.Visible && popup.Handle == handle && popup.Bounds == bounds && hidden == 0,
                    "settings must keep the same popup window visible without a transient hide");
                Assert(settings == 1 && popup.Visible && Find(popup, "SettingsPage").Visible,
                    "settings action must change pages inside the visible flyout");
            }
        }

        private static void VerifySettingsNavigationAndActions(Type popupType)
        {
            using (Form popup = CreatePopup(popupType))
            {
                int settings = 0, refresh = 0, dashboard = 0, autoStart = 0;
                int update = 0, about = 0, exit = 0, displayRequests = 0, hidden = 0;
                UsageDisplayMode requestedMode = UsageDisplayMode.FiveHours;
                Subscribe(popup, "SettingsRequested", delegate { settings++; });
                Subscribe(popup, "RefreshRequested", delegate { refresh++; });
                Subscribe(popup, "DashboardRequested", delegate { dashboard++; });
                Subscribe(popup, "AutoStartRequested", delegate { autoStart++; });
                Subscribe(popup, "UpdateRequested", delegate { update++; });
                Subscribe(popup, "AboutRequested", delegate { about++; });
                Subscribe(popup, "ExitRequested", delegate { exit++; });
                popupType.GetEvent("DisplayModeRequested").AddEventHandler(popup,
                    new Action<UsageDisplayMode>(delegate(UsageDisplayMode mode)
                    {
                        displayRequests++;
                        requestedMode = mode;
                    }));

                DateTime lastUpdated = DateTime.Now.AddMinutes(-7);
                long reset = DateTimeOffset.Now.AddHours(2).ToUnixTimeSeconds();
                RateLimitWindow fiveWindow = new RateLimitWindow("synthetic", "primary", 78, 300, reset);
                RateLimitWindow sevenWindow = new RateLimitWindow("synthetic", "secondary", 64, 10080, reset);
                Invoke(popup, "UpdateUsage", fiveWindow, sevenWindow, IconState.Normal, "", lastUpdated);
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                Rectangle anchor = new Rectangle(workArea.Right - 28, workArea.Bottom, 24, 24);
                Invoke(popup, "ShowAt", anchor);
                Application.DoEvents();
                IntPtr handle = popup.Handle;
                Rectangle bounds = popup.Bounds;
                int formCount = Application.OpenForms.Count;
                popup.VisibleChanged += delegate { if (!popup.Visible) hidden++; };
                Click(popup, "SettingsButton");
                Control settingsPage = Find(popup, "SettingsPage");
                Control usagePage = Find(popup, "UsagePage");
                Assert(settings == 1 && settingsPage.Visible && !usagePage.Visible &&
                    settingsPage.Parent == usagePage.Parent,
                    "settings must replace usage content within the same scroll host");
                Assert(popup.Visible && popup.Handle == handle && popup.Bounds == bounds &&
                    hidden == 0 && Application.OpenForms.Count == formCount,
                    "settings navigation must preserve the original window without a transient hide");

                Invoke(popup, "ApplyPalette", false);
                Assert(settingsPage.BackColor == usagePage.BackColor &&
                    settingsPage.BackColor == Color.FromArgb(238, 238, 239),
                    "settings and usage pages must share the light Windows surface");
                SavePreview(popup, "usage-popup-settings-synthetic.png");
                Invoke(popup, "ApplyPalette", true);
                Assert(settingsPage.BackColor == usagePage.BackColor &&
                    settingsPage.BackColor == Color.FromArgb(41, 41, 41),
                    "settings and usage pages must share the dark Windows surface");
                SavePreview(popup, "usage-popup-settings-dark-synthetic.png");

                Invoke(popup, "SetDisplayMode", UsageDisplayMode.FiveHours);
                Control five = Find(popup, "FiveHourDisplayButton");
                Control seven = Find(popup, "SevenDayDisplayButton");
                Assert(IsAccessibilityChecked(five) && !IsAccessibilityChecked(seven),
                    "persisted display selection must expose the actual checked accessibility state");
                Color selected = five.BackColor, unselected = seven.BackColor;
                Assert(selected != unselected, "the persisted display selection must be visible");
                Click(popup, "SevenDayDisplayButton");
                Assert(displayRequests == 1 && requestedMode == UsageDisplayMode.SevenDays &&
                    five.BackColor == selected && seven.BackColor == unselected &&
                    IsAccessibilityChecked(five) && !IsAccessibilityChecked(seven),
                    "display selection must wait for successful persistence");
                Invoke(popup, "SetDisplayMode", UsageDisplayMode.SevenDays);
                Assert(seven.BackColor == selected && five.BackColor == unselected,
                    "display selection must reflect the persisted mode");
                Assert(IsAccessibilityChecked(seven) && !IsAccessibilityChecked(five),
                    "persisted display change must move the checked accessibility state");
                Click(popup, "FiveHourDisplayButton");
                Assert(displayRequests == 2 && requestedMode == UsageDisplayMode.FiveHours &&
                    seven.BackColor == selected && five.BackColor == unselected &&
                    IsAccessibilityChecked(seven) && !IsAccessibilityChecked(five),
                    "both display options must request rather than assume persistence");

                Click(popup, "SettingsRefreshButton");
                Invoke(popup, "SetRefreshing", true);
                Click(popup, "SettingsRefreshButton");
                Assert(refresh == 1 && !Find(popup, "SettingsRefreshButton").Enabled &&
                    !Find(popup, "RefreshButton").Enabled,
                    "pending refresh must disable refresh actions on both pages");
                Invoke(popup, "SetRefreshing", false);
                Assert(Find(popup, "SettingsRefreshButton").Enabled && Find(popup, "RefreshButton").Enabled,
                    "completed refresh must enable refresh actions on both pages");

                Invoke(popup, "SetAutoStart", false);
                Control settingsAutoStart = Find(popup, "SettingsAutoStartButton");
                Assert(!IsAccessibilityChecked(settingsAutoStart),
                    "disabled persisted auto-start must expose an unchecked accessibility state");
                Color autoStartOff = settingsAutoStart.BackColor;
                Click(popup, "SettingsAutoStartButton");
                Assert(autoStart == 1 && Find(popup, "SettingsAutoStartButton").BackColor == autoStartOff &&
                    Find(popup, "AutoStartLabel").Text.Contains("끔") && !IsAccessibilityChecked(settingsAutoStart),
                    "settings auto-start must wait for successful persistence");
                Invoke(popup, "SetAutoStart", true);
                Assert(Find(popup, "SettingsAutoStartButton").AccessibleDescription.Contains("켬") &&
                    Find(popup, "AutoStartLabel").Text.Contains("켬"),
                    "persisted auto-start must update both pages");
                Assert(IsAccessibilityChecked(settingsAutoStart),
                    "enabled persisted auto-start must expose a checked accessibility state");

                Label settingsStatus = Find(popup, "SettingsStatusLabel") as Label;
                Assert(settingsStatus != null && settingsStatus.Visible,
                    "inline settings must visibly include the current usage status");
                Invoke(popup, "UpdateUsage", fiveWindow, sevenWindow, IconState.Stale,
                    "synthetic failure", lastUpdated);
                Assert(settingsStatus.Text.Contains("실패") &&
                    Find(popup, "SettingsFiveSummary").Text.Contains("78%") &&
                    Find(popup, "SettingsSevenSummary").Text.Contains("64%"),
                    "settings must show stale failure while retaining cached summary values");
                Invoke(popup, "UpdateUsage", null, null, IconState.Error, "synthetic failure", null);
                Assert(settingsStatus.Text.Contains("실패"),
                    "settings must show a first-load usage failure without a cached snapshot");
                Invoke(popup, "UpdateUsage", fiveWindow, sevenWindow, IconState.Normal, "", lastUpdated);
                Assert(!settingsStatus.Text.Contains("실패") && settingsStatus.Text.Contains("분 전"),
                    "successful usage must restore settings freshness and remove the old failure");
                string successfulStatus = settingsStatus.Text;
                Invoke(popup, "SetRefreshing", true);
                Assert(settingsStatus.Text.Contains("갱신"),
                    "settings status must reflect a pending usage refresh");
                Invoke(popup, "SetRefreshing", false);
                Assert(settingsStatus.Text == successfulStatus,
                    "settings status must restore freshness when usage refresh completes");

                Click(popup, "SettingsUpdateButton");
                Invoke(popup, "SetUpdateStatus", true, "업데이트 확인 중...");
                Click(popup, "SettingsUpdateButton");
                Assert(update == 1 && !Find(popup, "SettingsUpdateButton").Enabled,
                    "pending update must disable repeated update requests");
                Invoke(popup, "SetUpdateStatus", false, "업데이트 확인");
                Assert(Find(popup, "SettingsUpdateButton").Enabled,
                    "completed update check must restore the update action");
                Click(popup, "SettingsAboutButton");
                Assert(about == 1 && popup.Visible, "settings about must invoke the application action");

                Click(popup, "SettingsBackButton");
                Assert(popup.Visible && popup.Handle == handle && popup.Bounds == bounds &&
                    hidden == 0 && Application.OpenForms.Count == formCount &&
                    usagePage.Visible && !settingsPage.Visible,
                    "back navigation must preserve the original window without a transient hide");
                Assert(Find(popup, "FiveHourValue").Text == "78%" &&
                    Find(popup, "SevenDayValue").Text == "64%" &&
                    Find(popup, "AutoStartLabel").Text.Contains("켬"),
                    "returning to usage must preserve cached values and persisted settings");
                Invoke(popup, "ShowSettings");
                Click(popup, "SettingsDashboardButton");
                Assert(dashboard == 1 && !popup.Visible, "settings dashboard action must dismiss the flyout");
                Invoke(popup, "ShowSettingsAt", anchor);
                Assert(popup.Visible && popup.Handle == handle && settingsPage.Visible && !usagePage.Visible,
                    "opening settings directly must reuse the same flyout window");
                Click(popup, "SettingsExitButton");
                Assert(exit == 1, "settings exit must invoke the application exit action");
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
                        foreach (string name in new[] { "SettingsBackButton", "SettingsDashboardButton",
                            "SettingsRefreshButton", "FiveHourDisplayButton", "SevenDayDisplayButton",
                            "SettingsAutoStartButton", "SettingsUpdateButton", "SettingsAboutButton", "SettingsExitButton" })
                        {
                            Control action = Find(popup, name);
                            Assert(action.Width > 0 && action.Left >= 0 && action.Right <= action.Parent.ClientSize.Width,
                                "scaled settings actions must fit narrow content");
                        }
                    }
                }
            }
        }

        private static void VerifyDismissalAndReopening(Type popupType)
        {
            using (Form popup = CreatePopup(popupType))
            {
                int keyboardDismissals = 0;
                Subscribe(popup, "KeyboardDismissed", delegate { keyboardDismissals++; });
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                Rectangle anchor = new Rectangle(workArea.Right - 28, workArea.Bottom, 24, 24);
                Invoke(popup, "ToggleAt", anchor);
                Application.DoEvents();
                Assert(popup.Visible && workArea.Contains(popup.Bounds), "first tray click must open flyout");
                Invoke(popup, "ToggleAt", anchor);
                Assert(!popup.Visible, "second tray click must close flyout");
                Invoke(popup, "ToggleAt", anchor);
                Invoke(popup, "DismissForDeactivation", new Point(anchor.Left + 2, anchor.Top + 2));
                Assert(!popup.Visible, "deactivation must hide flyout");
                Invoke(popup, "ToggleAt", anchor);
                Assert(!popup.Visible, "tray callback following deactivation must not reopen flyout");
                Invoke(popup, "ToggleAt", anchor);
                Assert(popup.Visible, "subsequent click must reopen flyout");
                using (Form nativeTrayWindow = new Form())
                {
                    SendMessage(popup.Handle, 0x0006, IntPtr.Zero, nativeTrayWindow.Handle);
                    Assert(!popup.Visible, "native tray window activation must hide flyout");
                    Invoke(popup, "ToggleAt", anchor);
                    Assert(!popup.Visible, "keyboard tray callback must not reopen the deactivated flyout");
                    Invoke(popup, "ToggleAt", anchor);
                    Assert(popup.Visible, "keyboard tray selection must reopen on the next action");
                }
                Message key = Message.Create(popup.Handle, 0x0100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                object[] arguments = new object[] { key, Keys.Escape };
                popupType.GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(popup, arguments);
                Assert(!popup.Visible && keyboardDismissals == 1,
                    "Escape must dismiss flyout and request keyboard focus restoration");
                Invoke(popup, "ShowSettingsAt", anchor);
                popupType.GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(popup, arguments);
                Assert(!popup.Visible && keyboardDismissals == 2,
                    "Escape must dismiss inline settings and request keyboard focus restoration");
                Invoke(popup, "ShowAt", anchor);
                Invoke(popup, "DismissForDeactivation", new Point(workArea.Left, workArea.Top));
                Invoke(popup, "ToggleAt", anchor);
                Assert(popup.Visible, "outside-click dismissal must allow immediate reopening");
                Click(popup, "SettingsButton");
                Invoke(popup, "DismissForDeactivation", new Point(workArea.Left, workArea.Top));
                Assert(!popup.Visible, "outside-click deactivation must dismiss inline settings");
                Invoke(popup, "ToggleAt", anchor);
                Assert(popup.Visible && Find(popup, "UsagePage").Visible,
                    "tray reopening after settings dismissal must return to usage");
                popup.Close();
                Assert(!popup.IsDisposed && !popup.Visible, "user dismissal must preserve the reusable flyout");
            }
        }

        private static void VerifyTrayApplicationLifecycle()
        {
            using (TrayApplicationContext context = new TrayApplicationContext())
            {
                bool exited = false;
                context.ThreadExit += delegate { exited = true; };
                Type contextType = context.GetType();
                Form popup = (Form)contextType.GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(context);
                Assert(!popup.ShowInTaskbar && !popup.Visible,
                    "startup must not open a normal taskbar window");
                FieldInfo iconField = contextType.GetField("_trayIcon", BindingFlags.Instance | BindingFlags.NonPublic);
                NativeWindow icon = iconField == null ? null : (NativeWindow)iconField.GetValue(context);
                Assert(icon != null, "application must provide its notification-area icon");
                NotifyIconIdentifier identifier = new NotifyIconIdentifier();
                identifier.Size = (uint)Marshal.SizeOf(typeof(NotifyIconIdentifier));
                identifier.Window = icon.Handle;
                identifier.Id = 1;
                NativeRectangle rectangle;
                Assert(Shell_NotifyIconGetRect(ref identifier, out rectangle) == 0,
                    "application icon must be registered in the notification area");
                SendMessage(icon.Handle, 0x8001, IntPtr.Zero, new IntPtr((1 << 16) | 0x0400));
                Assert(popup.Visible && !popup.ShowInTaskbar,
                    "notification-area activation must open the popup without a taskbar app button");
                IntPtr popupHandle = popup.Handle;
                Rectangle popupBounds = popup.Bounds;
                int formCount = Application.OpenForms.Count, hidden = 0;
                popup.VisibleChanged += delegate { if (!popup.Visible) hidden++; };
                Click(popup, "SettingsButton");
                Assert(popup.Visible && popup.Handle == popupHandle && popup.Bounds == popupBounds &&
                    hidden == 0 && Application.OpenForms.Count == formCount && Find(popup, "SettingsPage").Visible,
                    "application settings gear must navigate within the same popup window");
                popup.Close();
                Assert(!popup.IsDisposed && !popup.Visible && !exited,
                    "closing the popup must keep the tray application running");
                SendMessage(icon.Handle, 0x8001, IntPtr.Zero, new IntPtr((1 << 16) | 0x007B));
                Assert(popup.Visible && popup.Handle == popupHandle && !popup.ShowInTaskbar &&
                    Find(popup, "SettingsPage").Visible && Application.OpenForms.Count == formCount,
                    "tray context action must open settings inside the same popup window");
                ((Button)Find(popup, "SettingsExitButton")).PerformClick();
                Assert(exited && popup.IsDisposed && Shell_NotifyIconGetRect(ref identifier, out rectangle) != 0,
                    "settings exit must stop the application and remove its notification-area icon");
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

        private static bool IsAccessibilityChecked(Control control)
        {
            return (control.AccessibilityObject.State & AccessibleStates.Checked) != AccessibleStates.None;
        }

        private static void SavePreview(Form popup, string fileName)
        {
            string directory = Environment.GetEnvironmentVariable("CODEX_USAGE_POPUP_PREVIEW_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            using (Bitmap bitmap = new Bitmap(popup.ClientSize.Width, popup.ClientSize.Height))
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
            public int Left, Top, Right, Bottom;
        }

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier,
            out NativeRectangle rectangle);
    }
}
