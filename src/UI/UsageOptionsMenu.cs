using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsageTray
{
    internal sealed class UsageOptionsMenu : ContextMenuStrip
    {
        private readonly MouseHookProcedure _mouseHookProcedure;
        private IntPtr _mouseHook;
        private bool _closePending;

        internal event Action<Point> MouseDownOutside;

        public UsageOptionsMenu()
        {
            AutoClose = false;
            _mouseHookProcedure = OnMouseMessage;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _closePending = false;
            _mouseHook = SetWindowsHookEx(14, _mouseHookProcedure, GetModuleHandle(null), 0);
            // Keep native outside-click dismissal available if the hook is unavailable.
            AutoClose = _mouseHook == IntPtr.Zero;
            Focus();
        }

        protected override void OnClosing(ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
            {
                e.Cancel = true;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            StopMouseHook();
            base.OnClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private IntPtr OnMouseMessage(int code, IntPtr message, IntPtr data)
        {
            int mouseMessage = message.ToInt32();
            if (code >= 0 && (mouseMessage == 0x0201 || mouseMessage == 0x0204 ||
                mouseMessage == 0x0207 || mouseMessage == 0x020B))
            {
                NativePoint point = (NativePoint)Marshal.PtrToStructure(data, typeof(NativePoint));
                HandleMouseDown(new Point(point.X, point.Y));
            }
            return CallNextHookEx(_mouseHook, code, message, data);
        }

        internal void HandleMouseDown(Point position)
        {
            if (!Visible || _closePending || ContainsMenuPoint(this, position))
            {
                return;
            }
            _closePending = true;
            Action<Point> handler = MouseDownOutside;
            if (handler != null)
            {
                handler(position);
            }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    _closePending = false;
                    if (!IsDisposed && Visible)
                    {
                        Close();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                _closePending = false;
            }
        }

        private static bool ContainsMenuPoint(ToolStripDropDown menu, Point position)
        {
            if (!menu.Visible)
            {
                return false;
            }
            if (menu.Bounds.Contains(position))
            {
                return true;
            }
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems &&
                    ContainsMenuPoint(menuItem.DropDown, position))
                {
                    return true;
                }
            }
            return false;
        }

        private void StopMouseHook()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            _closePending = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopMouseHook();
            }
            base.Dispose(disposing);
        }

        private delegate IntPtr MouseHookProcedure(int code, IntPtr message, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hook, MouseHookProcedure callback,
            IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);
    }
}
