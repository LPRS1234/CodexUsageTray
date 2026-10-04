using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsageTray.Tests
{
    internal static class TrayMenuSmokeTest
    {
        [STAThread]
        public static int Main()
        {
            try
            {
                DpiAwareness.Enable();
                Application.EnableVisualStyles();
                Run();
                VerifyOpenedMenu();
                VerifyPersistentMenu();
                VerifyKeyboardDismissal();
                Console.WriteLine("Tray menu smoke test passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static void Run()
        {
            Rectangle workArea = new Rectangle(0, 0, 1920, 1040);
            Size menuSize = new Size(360, 430);
            AssertLocation(new Rectangle(1722, 984, 188, 46), menuSize, workArea,
                CardCorner.BottomRight, 12, new Point(1550, 542), "bottom right above card");
            AssertLocation(new Rectangle(10, 984, 188, 46), menuSize, workArea,
                CardCorner.BottomLeft, 12, new Point(10, 542), "bottom left above card");
            AssertLocation(new Rectangle(1722, 10, 188, 46), menuSize, workArea,
                CardCorner.TopRight, 12, new Point(1550, 68), "top right below card");
            AssertLocation(new Rectangle(10, 10, 188, 46), menuSize, workArea,
                CardCorner.TopLeft, 12, new Point(10, 68), "top left below card");
            AssertLocation(new Rectangle(-198, 984, 188, 46), menuSize,
                new Rectangle(-1920, 100, 1920, 940), CardCorner.BottomRight, 12,
                new Point(-370, 542), "offset monitor coordinates");
            AssertLocation(new Rectangle(122, 624, 188, 46), new Size(300, 460),
                new Rectangle(0, 0, 320, 680), CardCorner.BottomRight, 12,
                new Point(10, 152), "narrow working area");
            AssertLocation(new Rectangle(1722, 948, 188, 46), new Size(540, 645),
                workArea, CardCorner.BottomRight, 18, new Point(1370, 285),
                "scaled menu keeps scaled gap");
        }

        private static void AssertLocation(Rectangle cardBounds, Size menuSize,
            Rectangle workArea, CardCorner corner, int gap, Point expected, string description)
        {
            Point actual = CornerUsageCard.GetMenuLocation(cardBounds, menuSize, workArea, corner, gap);
            Rectangle menuBounds = new Rectangle(actual, menuSize);
            if (actual != expected || menuBounds.IntersectsWith(cardBounds) ||
                !workArea.Contains(menuBounds))
            {
                throw new InvalidOperationException("Smoke test failed: " + description);
            }
        }

        private static void VerifyOpenedMenu()
        {
            using (ContextMenuStrip menu = new UsageOptionsMenu())
            {
                menu.Items.Add(new ToolStripMenuItem("Codex 설정")
                {
                    Name = "SettingsHeader", Enabled = false
                });
                ToolStripMenuItem status = new ToolStripMenuItem(new string('W', 150))
                {
                    Name = "UsageHeader", Enabled = false, Available = false
                };
                menu.Items.Add(status);
                ToolStripMenuItem detail = new ToolStripMenuItem("초기화 시각")
                {
                    Name = "UsageDetail", Enabled = false, Available = false
                };
                menu.Items.Add(detail);
                menu.Items.Add(new ToolStripMenuItem("대시보드 이동") { Name = "DashboardItem" });
                menu.Opening += delegate { detail.Available = true; };
                new DashboardMenuRenderer().ApplyTo(menu, 320);
                int configuredWidth = menu.MinimumSize.Width;

                using (CornerUsageCard card = new CornerUsageCard(menu, string.Empty,
                    CardCorner.BottomRight, UsageDisplayMode.Both))
                {
                    CardCorner[] corners = new[]
                    {
                        CardCorner.BottomRight, CardCorner.BottomLeft,
                        CardCorner.TopRight, CardCorner.TopLeft
                    };
                    foreach (CardCorner corner in corners)
                    {
                        card.SetCorner(corner);
                        detail.Available = false;
                        SendMessage(card.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                        Application.DoEvents();
                        NativeRectangle nativeBounds;
                        if (!GetWindowRect(card.Handle, out nativeBounds) || !menu.Visible)
                        {
                            throw new InvalidOperationException("Smoke test failed: menu did not open.");
                        }
                        Rectangle cardBounds = Rectangle.FromLTRB(nativeBounds.Left, nativeBounds.Top,
                            nativeBounds.Right, nativeBounds.Bottom);
                        Rectangle workArea = Screen.FromRectangle(cardBounds).WorkingArea;
                        Rectangle menuBounds = menu.Bounds;
                        if (menuBounds.Width != configuredWidth)
                        {
                            throw new InvalidOperationException("Smoke test failed: hidden status text expanded menu width.");
                        }
                        bool alignLeft = corner == CardCorner.TopLeft || corner == CardCorner.BottomLeft;
                        bool aligned = alignLeft ? menuBounds.Left == cardBounds.Left
                            : menuBounds.Right == cardBounds.Right;
                        int actualGap = menuBounds.Bottom <= cardBounds.Top
                            ? cardBounds.Top - menuBounds.Bottom : menuBounds.Top - cardBounds.Bottom;
                        int expectedGap;
                        using (Graphics graphics = Graphics.FromHwnd(card.Handle))
                        {
                            expectedGap = (int)Math.Round(12f * graphics.DpiX / 96f);
                        }
                        if (!aligned || menuBounds.IntersectsWith(cardBounds) ||
                            !workArea.Contains(menuBounds) || !detail.Available || actualGap != expectedGap)
                        {
                            throw new InvalidOperationException("Smoke test failed: opened menu placement.");
                        }
                        status.Available = true;
                        status.Text = new string('W', 200);
                        menu.PerformLayout();
                        Application.DoEvents();
                        if (menu.Width != configuredWidth)
                        {
                            throw new InvalidOperationException("Smoke test failed: status update expanded menu width.");
                        }
                        status.Available = false;
                        SendMessage(card.Handle, 0x0205, IntPtr.Zero, IntPtr.Zero);
                        Application.DoEvents();
                        if (menu.Visible)
                        {
                            throw new InvalidOperationException("Smoke test failed: second click did not close menu.");
                        }
                    }

                    card.SetCorner(CardCorner.BottomRight);
                    Rectangle primaryWorkArea = Screen.PrimaryScreen.WorkingArea;
                    menu.MinimumSize = new Size(primaryWorkArea.Width + 100, 0);
                    SendMessage(card.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                    Application.DoEvents();
                    if (!primaryWorkArea.Contains(menu.Bounds))
                    {
                        throw new InvalidOperationException("Smoke test failed: oversized minimum menu width.");
                    }
                    menu.Close();

                    SendMessage(card.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                    Application.DoEvents();
                    NativeRectangle cardRectangle;
                    GetWindowRect(card.Handle, out cardRectangle);
                    ((UsageOptionsMenu)menu).HandleMouseDown(new Point(cardRectangle.Left + 10,
                        cardRectangle.Top + 10));
                    Application.DoEvents();
                    SendMessage(card.Handle, 0x0201, IntPtr.Zero, IntPtr.Zero);
                    SendMessage(card.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                    Application.DoEvents();
                    if (menu.Visible)
                    {
                        throw new InvalidOperationException("Smoke test failed: outside card click reopened panel.");
                    }
                }
            }
        }

        private static void VerifyPersistentMenu()
        {
            using (UsageOptionsMenu menu = new UsageOptionsMenu())
            {
                bool actionCalled = false;
                ToolStripMenuItem action = new ToolStripMenuItem("지금 새로고침");
                action.Click += delegate { actionCalled = true; };
                ToolStripMenuItem selector = new ToolStripMenuItem("표시할 사용량");
                ToolStripMenuItem option = new ToolStripMenuItem("7일");
                option.Click += delegate { option.Checked = true; };
                selector.DropDownItems.Add(option);
                menu.Items.Add(action);
                menu.Items.Add(selector);
                menu.Show(new Point(Screen.PrimaryScreen.WorkingArea.Left + 50,
                    Screen.PrimaryScreen.WorkingArea.Top + 50));
                Application.DoEvents();
                action.PerformClick();
                menu.Close(ToolStripDropDownCloseReason.ItemClicked);
                Application.DoEvents();
                if (!actionCalled || !menu.Visible)
                {
                    throw new InvalidOperationException("Smoke test failed: menu action dismissed panel.");
                }

                selector.ShowDropDown();
                Application.DoEvents();
                menu.HandleMouseDown(new Point(selector.DropDown.Left + 10, selector.DropDown.Top + 10));
                Application.DoEvents();
                if (!menu.Visible || !selector.DropDown.Visible)
                {
                    throw new InvalidOperationException("Smoke test failed: click inside submenu dismissed panel.");
                }
                option.PerformClick();
                selector.DropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
                menu.Close(ToolStripDropDownCloseReason.AppFocusChange);
                Application.DoEvents();
                if (!option.Checked || !menu.Visible)
                {
                    throw new InvalidOperationException("Smoke test failed: submenu selection dismissed panel.");
                }
                menu.HandleMouseDown(new Point(menu.Left + 10, menu.Top + 10));
                Application.DoEvents();
                if (!menu.Visible)
                {
                    throw new InvalidOperationException("Smoke test failed: click inside panel dismissed it.");
                }
                menu.HandleMouseDown(new Point(menu.Left - 10, menu.Top - 10));
                Application.DoEvents();
                if (menu.Visible)
                {
                    throw new InvalidOperationException("Smoke test failed: outside click did not dismiss panel.");
                }
            }
        }

        private static void VerifyKeyboardDismissal()
        {
            using (UsageOptionsMenu menu = new UsageOptionsMenu())
            {
                int keyboardDismissals = 0;
                menu.KeyboardDismissed += delegate { keyboardDismissals++; };
                menu.Items.Add(new ToolStripMenuItem("지금 새로고침"));
                ToolStripMenuItem selector = new ToolStripMenuItem("표시할 사용량");
                ToolStripMenuItem option = new ToolStripMenuItem("7일");
                selector.DropDownItems.Add(option);
                menu.Items.Add(selector);
                new DashboardMenuRenderer().ApplyTo(menu, 320);
                using (CornerUsageCard card = new CornerUsageCard(menu, string.Empty,
                    CardCorner.BottomRight, UsageDisplayMode.Both))
                {
                    card.SetCorner(CardCorner.BottomRight);
                    foreach (bool selectOption in new[] { false, true })
                    {
                        SendMessage(card.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
                        Application.DoEvents();
                        if (selectOption)
                        {
                            selector.ShowDropDown();
                            Application.DoEvents();
                            option.PerformClick();
                            selector.DropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
                            Application.DoEvents();
                        }
                        IntPtr keyboardWindow = GetFocus();
                        if (keyboardWindow == IntPtr.Zero) keyboardWindow = GetActiveWindow();
                        if (keyboardWindow != card.Handle && keyboardWindow != menu.Handle)
                        {
                            throw new InvalidOperationException("Smoke test failed: panel keyboard target unavailable.");
                        }
                        if (!PostMessage(keyboardWindow, 0x0100, new IntPtr(27), new IntPtr(1)) ||
                            !PostMessage(keyboardWindow, 0x0101, new IntPtr(27), new IntPtr(unchecked((int)0xC0000001))))
                        {
                            throw new InvalidOperationException("Smoke test failed: unable to queue panel keyboard input.");
                        }
                        Application.DoEvents();
                        if (menu.Visible)
                        {
                            throw new InvalidOperationException("Smoke test failed: Escape did not dismiss opened panel.");
                        }
                        if (keyboardDismissals != (selectOption ? 2 : 1))
                        {
                            throw new InvalidOperationException("Smoke test failed: keyboard dismissal notification missing.");
                        }
                    }
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
