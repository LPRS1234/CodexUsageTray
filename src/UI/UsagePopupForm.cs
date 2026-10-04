using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexUsageTray
{
    internal sealed class UsagePopupForm : Form
    {
        private const int WmDpiChanged = 0x02E0;
        private const int WmSettingChange = 0x001A;
        private const int WmDisplayChange = 0x007E;
        private const int WmThemeChanged = 0x031A;
        private const int WmActivate = 0x0006;
        private readonly Panel _scrollHost;
        private readonly Panel _content;
        private readonly Panel _footer;
        private readonly Label _title;
        private readonly GlyphPicture _appGlyph;
        private readonly QuickActionButton _refresh;
        private readonly QuickActionButton _dashboard;
        private readonly QuickActionButton _autoStart;
        private readonly QuickActionButton _settings;
        private readonly Label _refreshLabel;
        private readonly Label _dashboardLabel;
        private readonly Label _autoStartLabel;
        private readonly Label _fiveLabel;
        private readonly Label _fiveValue;
        private readonly Label _fiveReset;
        private readonly UsageProgressBar _fiveProgress;
        private readonly Label _sevenLabel;
        private readonly Label _sevenValue;
        private readonly Label _sevenReset;
        private readonly UsageProgressBar _sevenProgress;
        private readonly Label _status;
        private readonly System.Windows.Forms.Timer _freshnessTimer;
        private Font _bodyFont;
        private Font _captionFont;
        private Font _valueFont;
        private Font _titleFont;
        private Rectangle _anchor;
        private float _scale = 1f;
        private float _layoutScale = 1f;
        private int _usageTop;
        private IconState _state = IconState.Loading;
        private DateTime? _lastUpdated;
        private bool _refreshing;
        private bool _autoStartEnabled;
        private bool _positioning;
        private bool _dark;
        private bool _dismissedOnAnchor;
        private bool _deactivatedToOwnWindow;
        private int _dismissedTick;
        private Color _surfaceColor;
        private Color _footerColor;
        private Color _textColor;
        private Color _secondaryColor;
        private Color _borderColor;
        private Color _accentColor;
        private Color _accentInk;
        private Color _warningColor;

        public event EventHandler RefreshRequested;
        public event EventHandler DashboardRequested;
        public event EventHandler AutoStartRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler KeyboardDismissed;

        public UsagePopupForm()
        {
            Text = "Codex 사용량";
            Name = "UsagePopup";
            AccessibleName = Text;
            AccessibleRole = AccessibleRole.Dialog;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            MinimumSize = Size.Empty;

            _scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabStop = false };
            _content = new Panel { TabStop = false };
            _footer = new Panel { TabStop = false };
            Controls.Add(_scrollHost);
            _scrollHost.Controls.Add(_content);
            _content.Controls.Add(_footer);
            _content.Paint += PaintContent;
            _footer.Paint += PaintFooter;
            _appGlyph = new GlyphPicture { AccessibleName = "Codex", AccessibleRole = AccessibleRole.Graphic };
            _content.Controls.Add(_appGlyph);
            _title = AddLabel(_content, "HeaderLabel", "Codex 사용량");
            _refresh = AddButton(_content, "RefreshButton", "사용량 새로고침", ActionGlyph.Refresh, 0);
            _dashboard = AddButton(_content, "DashboardButton", "대시보드 열기", ActionGlyph.Dashboard, 1);
            _autoStart = AddButton(_content, "AutoStartButton", "Windows 시작 시 자동 실행 켜기", ActionGlyph.Power, 2);
            _settings = AddButton(_footer, "SettingsButton", "Codex 설정", ActionGlyph.Settings, 3);
            _refreshLabel = AddLabel(_content, "RefreshLabel", "새로고침");
            _dashboardLabel = AddLabel(_content, "DashboardLabel", "대시보드");
            _autoStartLabel = AddLabel(_content, "AutoStartLabel", "자동 실행 끔");
            _refreshLabel.TextAlign = _dashboardLabel.TextAlign = _autoStartLabel.TextAlign = ContentAlignment.MiddleCenter;
            _fiveLabel = AddLabel(_content, "FiveHourLabel", "5시간");
            _fiveValue = AddLabel(_content, "FiveHourValue", "--");
            _fiveReset = AddLabel(_content, "FiveHourReset", "초기화 시각 확인 중");
            _fiveProgress = AddProgress(_content, "FiveHourProgress", "5시간 남은 사용량");
            _sevenLabel = AddLabel(_content, "SevenDayLabel", "7일");
            _sevenValue = AddLabel(_content, "SevenDayValue", "--");
            _sevenReset = AddLabel(_content, "SevenDayReset", "초기화 시각 확인 중");
            _sevenProgress = AddProgress(_content, "SevenDayProgress", "7일 남은 사용량");
            _fiveValue.TextAlign = _sevenValue.TextAlign = ContentAlignment.MiddleRight;
            _status = AddLabel(_footer, "StatusLabel", "사용량 확인 중…");
            _status.AccessibleRole = AccessibleRole.StaticText;

            _refresh.Click += delegate { Raise(RefreshRequested); };
            _dashboard.Click += delegate { Hide(); Raise(DashboardRequested); };
            _autoStart.Click += delegate { Raise(AutoStartRequested); };
            _settings.Click += delegate { Hide(); Raise(SettingsRequested); };
            ApplyTheme();
            ApplyLayout(1f, new Size(360, 400));
            SetAutoStart(false);
            UpdateStatus();
            _freshnessTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            _freshnessTimer.Tick += delegate { UpdateStatus(); };
        }

        public void UpdateUsage(RateLimitWindow fiveHourWindow, RateLimitWindow sevenDayWindow,
            IconState state, string message, DateTime? lastUpdated)
        {
            _state = state;
            _lastUpdated = lastUpdated;
            // Server errors can contain account details. Use local, actionable status copy.
            UpdateWindow(fiveHourWindow, _fiveValue, _fiveProgress, _fiveReset);
            UpdateWindow(sevenDayWindow, _sevenValue, _sevenProgress, _sevenReset);
            UpdateStatus();
        }

        public void SetAutoStart(bool enabled)
        {
            _autoStartEnabled = enabled;
            _autoStartLabel.Text = "자동 실행 " + (enabled ? "켬" : "끔");
            _autoStart.AccessibleName = "Windows 시작 시 자동 실행 " + (enabled ? "끄기" : "켜기");
            _autoStart.AccessibleDescription = "자동 실행 " + (enabled ? "켬" : "끔");
            ApplyButtonColors();
        }

        public void SetRefreshing(bool refreshing)
        {
            _refreshing = refreshing;
            _refresh.Enabled = !refreshing;
            _refreshLabel.Text = refreshing ? "갱신 중…" : "새로고침";
            UpdateStatus();
        }

        public void ShowAt(Rectangle anchor)
        {
            if (IsDisposed) return;
            _dismissedOnAnchor = false;
            _anchor = anchor;
            Screen screen = Screen.FromRectangle(anchor);
            _positioning = true;
            try
            {
                ApplyTheme();
                ApplyLayout(GetMonitorScale(anchor), screen.WorkingArea.Size);
                Bounds = CalculateBounds(anchor, Size, screen.WorkingArea, Scale(12));
                Show();
                Activate();
                SetForegroundWindow(Handle);
                _refresh.Focus();
            }
            finally
            {
                _positioning = false;
            }
        }

        public void ToggleAt(Rectangle anchor)
        {
            if (Visible)
            {
                _dismissedOnAnchor = false;
                Hide();
                return;
            }
            int elapsed = unchecked(Environment.TickCount - _dismissedTick);
            bool sameClick = _dismissedOnAnchor && anchor.IntersectsWith(_anchor) &&
                elapsed >= 0 && elapsed <= SystemInformation.DoubleClickTime;
            _dismissedOnAnchor = false;
            if (!sameClick) ShowAt(anchor);
        }

        internal void DismissForDeactivation(Point cursor)
        {
            DismissFromActivation(cursor, false);
        }

        private void DismissFromActivation(Point cursor, bool ownWindowActivated)
        {
            if (!Visible) return;
            _dismissedOnAnchor = _anchor.Contains(cursor) || ownWindowActivated;
            _dismissedTick = Environment.TickCount;
            Hide();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            DismissFromActivation(Cursor.Position, _deactivatedToOwnWindow);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (_freshnessTimer == null) return;
            _freshnessTimer.Enabled = Visible;
            if (Visible) UpdateStatus();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                _dismissedOnAnchor = false;
                Hide();
                Raise(KeyboardDismissed);
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW fallback
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowAppearance();
        }

        protected override void WndProc(ref Message message)
        {
            int kind = message.Msg;
            if (kind == WmActivate && (message.WParam.ToInt64() & 0xffff) == 0)
            {
                uint processId;
                GetWindowThreadProcessId(message.LParam, out processId);
                _deactivatedToOwnWindow = processId != 0 && processId == GetCurrentProcessId();
            }
            base.WndProc(ref message);
            if (kind == WmActivate) _deactivatedToOwnWindow = false;
            if (_content == null || _positioning) return;
            if (kind == WmDpiChanged && Visible)
            {
                _positioning = true;
                try
                {
                    Rectangle workArea = Screen.FromRectangle(_anchor).WorkingArea;
                    int dpi = (int)(message.WParam.ToInt64() & 0xffff);
                    ApplyLayout(dpi > 0 ? dpi / 96f : GetMonitorScale(_anchor), workArea.Size);
                    Bounds = CalculateBounds(_anchor, Size, workArea, Scale(12));
                }
                finally { _positioning = false; }
            }
            else if (kind == WmSettingChange || kind == WmThemeChanged || kind == WmDisplayChange)
            {
                ApplyTheme();
                if (Visible)
                {
                    Rectangle workArea = Screen.FromRectangle(_anchor).WorkingArea;
                    ApplyLayout(GetMonitorScale(_anchor), workArea.Size);
                    Bounds = CalculateBounds(_anchor, Size, workArea, Scale(12));
                }
            }
        }

        internal static Rectangle CalculateBounds(Rectangle anchor, Size desiredSize,
            Rectangle workArea, int gap)
        {
            int width = Math.Max(1, Math.Min(desiredSize.Width, workArea.Width));
            int height = Math.Max(1, Math.Min(desiredSize.Height, workArea.Height));
            int x = anchor.Left + (anchor.Width - width) / 2;
            int y = anchor.Top - gap - height;
            if (anchor.Bottom <= workArea.Top || y < workArea.Top)
            {
                y = anchor.Bottom + gap;
            }
            x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - width));
            y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        internal void ApplyLayout(float scale, Size availableSize)
        {
            _scale = Math.Max(0.75f, Math.Min(4f, scale));
            int width = Math.Max(1, Math.Min(Scale(360), availableSize.Width));
            int height = Math.Max(1, Math.Min(Scale(400), availableSize.Height));
            // Small working areas use a compact layout and vertical scrolling instead of clipping.
            float layoutScale = Math.Min(_scale, Math.Max(0.5f, (width - 2f) / 280f));
            int contentHeight = (int)Math.Round(398f * layoutScale);
            bool scroll = contentHeight + 2 > height;
            int contentWidth = Math.Max(1, width - 2 - (scroll ? SystemInformation.VerticalScrollBarWidth : 0));
            layoutScale = Math.Min(layoutScale, Math.Max(0.5f, contentWidth / 280f));
            _layoutScale = layoutScale;
            contentHeight = LayoutScale(398);
            SuspendLayout();
            _scrollHost.SuspendLayout();
            _content.SuspendLayout();
            ClientSize = new Size(width, height);
            _content.Location = new Point(1, 1);
            _content.Size = new Size(contentWidth, contentHeight);
            _scrollHost.AutoScrollMinSize = new Size(0, contentHeight + 2);
            _scrollHost.AutoScrollPosition = Point.Empty;
            UpdateFonts();
            int margin = LayoutScale(22);
            int usable = Math.Max(1, contentWidth - margin * 2);
            _appGlyph.Bounds = new Rectangle(margin, LayoutScale(19), LayoutScale(19), LayoutScale(19));
            _appGlyph.ScaleFactor = _layoutScale;
            _title.Bounds = new Rectangle(margin + LayoutScale(28), LayoutScale(16),
                Math.Max(1, usable - LayoutScale(28)), LayoutScale(25));
            int tileGap = LayoutScale(12);
            int tileWidth = Math.Max(1, (usable - tileGap * 2) / 3);
            QuickActionButton[] buttons = new[] { _refresh, _dashboard, _autoStart };
            Label[] captions = new[] { _refreshLabel, _dashboardLabel, _autoStartLabel };
            for (int index = 0; index < buttons.Length; index++)
            {
                int x = margin + index * (tileWidth + tileGap);
                int currentWidth = index == 2 ? contentWidth - margin - x : tileWidth;
                buttons[index].Bounds = new Rectangle(x, LayoutScale(54), currentWidth, LayoutScale(48));
                buttons[index].ScaleFactor = _layoutScale;
                captions[index].Bounds = new Rectangle(x - LayoutScale(2), LayoutScale(108),
                    currentWidth + LayoutScale(4), LayoutScale(22));
            }
            _usageTop = LayoutScale(151);
            LayoutWindow(_fiveLabel, _fiveValue, _fiveProgress, _fiveReset, margin, usable, 172);
            LayoutWindow(_sevenLabel, _sevenValue, _sevenProgress, _sevenReset, margin, usable, 257);
            _footer.Bounds = new Rectangle(0, LayoutScale(340), contentWidth, LayoutScale(58));
            _settings.Bounds = new Rectangle(contentWidth - margin - LayoutScale(32), LayoutScale(13),
                LayoutScale(32), LayoutScale(32));
            _settings.ScaleFactor = _layoutScale;
            _status.Bounds = new Rectangle(margin, LayoutScale(9), Math.Max(1, _settings.Left - margin - LayoutScale(8)),
                LayoutScale(40));
            _content.ResumeLayout(false);
            _scrollHost.ResumeLayout(false);
            ResumeLayout(false);
            _content.Invalidate();
        }

        private void LayoutWindow(Label label, Label value, UsageProgressBar progress,
            Label reset, int left, int width, int top)
        {
            int valueWidth = LayoutScale(88);
            label.Bounds = new Rectangle(left, LayoutScale(top + 4), Math.Max(1, width - valueWidth), LayoutScale(26));
            value.Bounds = new Rectangle(left + width - valueWidth, LayoutScale(top), valueWidth, LayoutScale(31));
            progress.Bounds = new Rectangle(left, LayoutScale(top + 38), width, Math.Max(3, LayoutScale(4)));
            reset.Bounds = new Rectangle(left, LayoutScale(top + 49), width, LayoutScale(22));
        }

        private void UpdateFonts()
        {
            Font oldBody = _bodyFont, oldCaption = _captionFont, oldValue = _valueFont, oldTitle = _titleFont;
            _bodyFont = new Font("Segoe UI", Math.Max(10f, 13f * _layoutScale), FontStyle.Regular, GraphicsUnit.Pixel);
            _captionFont = new Font("Segoe UI", Math.Max(9f, 11f * _layoutScale), FontStyle.Regular, GraphicsUnit.Pixel);
            _valueFont = new Font("Segoe UI Semibold", Math.Max(15f, 21f * _layoutScale), FontStyle.Regular, GraphicsUnit.Pixel);
            _titleFont = new Font("Segoe UI Semibold", Math.Max(10f, 13f * _layoutScale), FontStyle.Regular, GraphicsUnit.Pixel);
            Font = _bodyFont;
            _title.Font = _titleFont;
            _fiveValue.Font = _sevenValue.Font = _valueFont;
            _refreshLabel.Font = _dashboardLabel.Font = _autoStartLabel.Font = _captionFont;
            _fiveReset.Font = _sevenReset.Font = _status.Font = _captionFont;
            if (oldBody != null) oldBody.Dispose();
            if (oldCaption != null) oldCaption.Dispose();
            if (oldValue != null) oldValue.Dispose();
            if (oldTitle != null) oldTitle.Dispose();
        }

        private void UpdateWindow(RateLimitWindow window, Label value, UsageProgressBar progress, Label reset)
        {
            int? remaining = window == null ? (int?)null : Math.Max(0, Math.Min(100, window.RemainingPercent));
            value.Text = remaining.HasValue ? remaining.Value.ToString(CultureInfo.InvariantCulture) + "%" : "--";
            value.AccessibleName = (progress.Name == "FiveHourProgress" ? "5시간" : "7일") + " " +
                (remaining.HasValue ? value.Text + " 남음" : "사용량 확인 중");
            progress.Value = remaining ?? 0;
            progress.AccessibleDescription = remaining.HasValue ? value.Text + " 남음" : "확인되지 않은 사용량";
            progress.ForeColor = remaining.HasValue && remaining.Value <= 15 ? _warningColor : _accentColor;
            reset.Text = ResetText(window);
            progress.Invalidate();
        }

        private static string ResetText(RateLimitWindow window)
        {
            if (window == null || window.ResetsAtUnixSeconds <= 0) return "초기화 시각 확인 중";
            try
            {
                DateTime local = DateTimeOffset.FromUnixTimeSeconds(window.ResetsAtUnixSeconds).LocalDateTime;
                return (local.Date == DateTime.Today ? "오늘 " : local.ToString("M월 d일 ", CultureInfo.CurrentCulture)) +
                    local.ToString("tt h:mm", CultureInfo.GetCultureInfo("ko-KR")) + " 초기화";
            }
            catch (ArgumentOutOfRangeException) { return "초기화 시각 확인 중"; }
        }

        private void UpdateStatus()
        {
            string freshness = FreshnessText();
            if (_refreshing || _state == IconState.Loading)
            {
                _status.Text = _lastUpdated.HasValue ? "갱신 중… · " + freshness : "사용량 확인 중…";
            }
            else if (_state == IconState.Stale || _state == IconState.Error)
            {
                _status.Text = _lastUpdated.HasValue ? "갱신 실패 · 마지막 확인 " + freshness : "갱신 실패 · 새로고침해 주세요";
            }
            else
            {
                _status.Text = freshness;
            }
            _status.ForeColor = (_state == IconState.Stale || _state == IconState.Error) ? _warningColor : _secondaryColor;
            _status.AccessibleDescription = _status.Text + (_lastUpdated.HasValue ? " · 마지막 성공 " +
                _lastUpdated.Value.ToLocalTime().ToString("M/d HH:mm", CultureInfo.CurrentCulture) : "");
        }

        private string FreshnessText()
        {
            if (!_lastUpdated.HasValue) return "아직 갱신되지 않음";
            TimeSpan age = DateTime.Now - _lastUpdated.Value.ToLocalTime();
            if (age.TotalMinutes < 1) return "방금 갱신됨";
            if (age.TotalMinutes < 60) return ((int)age.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "분 전";
            if (age.TotalHours < 24) return ((int)age.TotalHours).ToString(CultureInfo.InvariantCulture) + "시간 전";
            return _lastUpdated.Value.ToLocalTime().ToString("M/d HH:mm", CultureInfo.CurrentCulture);
        }

        private void ApplyTheme()
        {
            _dark = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    _dark = value is int && (int)value == 0;
                }
            }
            catch { }
            ApplyPalette(_dark);
        }

        private void ApplyPalette(bool dark)
        {
            _dark = dark;
            _surfaceColor = _dark ? Color.FromArgb(41, 41, 41) : Color.FromArgb(238, 238, 239);
            _footerColor = _dark ? Color.FromArgb(37, 37, 37) : Color.FromArgb(231, 231, 235);
            _textColor = _dark ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
            _secondaryColor = _dark ? Color.FromArgb(190, 190, 190) : Color.FromArgb(96, 96, 96);
            _borderColor = _dark ? Color.FromArgb(69, 69, 69) : Color.FromArgb(222, 222, 225);
            _accentColor = _dark ? Color.FromArgb(117, 187, 255) : Color.FromArgb(0, 103, 192);
            _accentInk = _dark ? Color.FromArgb(16, 40, 64) : Color.White;
            _warningColor = _dark ? Color.FromArgb(244, 189, 107) : Color.FromArgb(146, 86, 0);
            if (SystemInformation.HighContrast)
            {
                _surfaceColor = _footerColor = SystemColors.Window;
                _textColor = _secondaryColor = _warningColor = SystemColors.WindowText;
                _borderColor = SystemColors.WindowText;
                _accentColor = SystemColors.Highlight;
                _accentInk = SystemColors.HighlightText;
            }
            BackColor = _borderColor;
            ForeColor = _textColor;
            _scrollHost.BackColor = _surfaceColor;
            _content.BackColor = _surfaceColor;
            _footer.BackColor = _footerColor;
            _appGlyph.ForeColor = _textColor;
            Label[] secondary = new[] { _fiveReset, _sevenReset, _autoStartLabel };
            foreach (Label label in secondary) label.ForeColor = _secondaryColor;
            _fiveProgress.BackColor = _sevenProgress.BackColor = _dark ? Color.FromArgb(72, 72, 72) : _borderColor;
            _fiveProgress.ForeColor = _fiveProgress.Value <= 15 ? _warningColor : _accentColor;
            _sevenProgress.ForeColor = _sevenProgress.Value <= 15 ? _warningColor : _accentColor;
            ApplyButtonColors();
            UpdateStatus();
            if (IsHandleCreated) ApplyWindowAppearance();
            Invalidate(true);
        }

        private void ApplyButtonColors()
        {
            Color tileColor = _dark ? Color.FromArgb(41, 41, 41) : Color.FromArgb(250, 250, 250);
            Color hover = _dark ? Color.FromArgb(59, 59, 59) : Color.FromArgb(233, 233, 236);
            QuickActionButton[] buttons = new[] { _refresh, _dashboard, _autoStart, _settings };
            foreach (QuickActionButton button in buttons)
            {
                button.BackColor = button == _settings ? _footerColor : tileColor;
                button.ForeColor = _textColor;
                button.HoverColor = hover;
                button.BorderColor = _borderColor;
            }
            if (_autoStartEnabled)
            {
                _autoStart.BackColor = _accentColor;
                _autoStart.BorderColor = _accentColor;
                _autoStart.ForeColor = _accentInk;
                _autoStart.HoverColor = _accentColor;
            }
            foreach (QuickActionButton button in buttons) button.Invalidate();
        }

        private void ApplyWindowAppearance()
        {
            try
            {
                int rounded = 2;
                DwmSetWindowAttribute(Handle, 33, ref rounded, 4);
                int dark = _dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            }
            catch { }
        }

        private void PaintContent(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(_borderColor))
            {
                e.Graphics.DrawLine(pen, 0, _usageTop, _content.Width, _usageTop);
            }
        }

        private void PaintFooter(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(_borderColor)) e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        }

        private int Scale(int value) { return (int)Math.Round(value * _scale); }
        private int LayoutScale(int value) { return (int)Math.Round(value * _layoutScale); }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static Label AddLabel(Control parent, string name, string text)
        {
            Label label = new Label { Name = name, Text = text, AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft, TabStop = false, UseMnemonic = false };
            parent.Controls.Add(label);
            return label;
        }

        private static QuickActionButton AddButton(Control parent, string name, string accessibleName,
            ActionGlyph glyph, int tabIndex)
        {
            QuickActionButton button = new QuickActionButton(glyph)
            {
                Name = name, AccessibleName = accessibleName, TabIndex = tabIndex, TabStop = true,
                AccessibleRole = AccessibleRole.PushButton
            };
            parent.Controls.Add(button);
            return button;
        }

        private static UsageProgressBar AddProgress(Control parent, string name, string accessibleName)
        {
            UsageProgressBar progress = new UsageProgressBar { Name = name, AccessibleName = accessibleName,
                Minimum = 0, Maximum = 100, Value = 0, TabStop = false };
            parent.Controls.Add(progress);
            return progress;
        }

        private static float GetMonitorScale(Rectangle anchor)
        {
            try
            {
                NativeRectangle rectangle = new NativeRectangle(anchor);
                IntPtr monitor = MonitorFromRect(ref rectangle, 2);
                uint dpiX, dpiY;
                if (GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0 && dpiX > 0) return dpiX / 96f;
            }
            catch { }
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) return graphics.DpiX / 96f;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_freshnessTimer != null) _freshnessTimer.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                if (_bodyFont != null) _bodyFont.Dispose();
                if (_captionFont != null) _captionFont.Dispose();
                if (_valueFont != null) _valueFont.Dispose();
                if (_titleFont != null) _titleFont.Dispose();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left, Top, Right, Bottom;
            public NativeRectangle(Rectangle rectangle)
            {
                Left = rectangle.Left; Top = rectangle.Top; Right = rectangle.Right; Bottom = rectangle.Bottom;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref NativeRectangle rectangle, uint flags);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private enum ActionGlyph { Refresh, Dashboard, Power, Settings, Terminal }

        private sealed class UsageProgressBar : ProgressBar
        {
            public UsageProgressBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer, true);
                AccessibleRole = AccessibleRole.ProgressBar;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Rounded(new RectangleF(0, 0, Width, Height), Height / 2f))
                using (SolidBrush track = new SolidBrush(BackColor)) e.Graphics.FillPath(track, path);
                float filled = Width * Value / 100f;
                if (filled <= 0) return;
                using (GraphicsPath path = Rounded(new RectangleF(0, 0, filled, Height), Math.Min(Height, filled) / 2f))
                using (SolidBrush fill = new SolidBrush(ForeColor)) e.Graphics.FillPath(fill, path);
            }
        }

        private sealed class GlyphPicture : Control
        {
            public float ScaleFactor = 1f;
            public GlyphPicture()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer, true);
                TabStop = false;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                DrawGlyph(e.Graphics, ActionGlyph.Terminal, ClientRectangle, ForeColor, ScaleFactor);
            }
        }

        private sealed class QuickActionButton : Button
        {
            private readonly ActionGlyph _glyph;
            private bool _hovered;
            public float ScaleFactor = 1f;
            public Color HoverColor;
            public Color BorderColor;

            public QuickActionButton(ActionGlyph glyph)
            {
                _glyph = glyph;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnMouseEnter(EventArgs e) { _hovered = true; base.OnMouseEnter(e); Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { _hovered = false; base.OnMouseLeave(e); Invalidate(); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Cursor = Enabled ? Cursors.Hand : Cursors.Default;
                if (!Enabled) _hovered = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Parent.BackColor);
                RectangleF tile = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                using (GraphicsPath path = Rounded(tile, 5f * ScaleFactor))
                using (SolidBrush brush = new SolidBrush(_hovered && Enabled ? HoverColor : BackColor))
                using (Pen border = new Pen(BorderColor))
                {
                    e.Graphics.FillPath(brush, path);
                    if (_glyph != ActionGlyph.Settings || (_hovered && Enabled))
                        e.Graphics.DrawPath(border, path);
                }
                DrawGlyph(e.Graphics, _glyph, ClientRectangle, Enabled ? ForeColor : SystemColors.GrayText, ScaleFactor);
                if (Focused && ShowFocusCues)
                {
                    Rectangle focus = ClientRectangle;
                    focus.Inflate(-4, -4);
                    ControlPaint.DrawFocusRectangle(e.Graphics, focus, ForeColor, BackColor);
                }
            }
        }

        private static GraphicsPath Rounded(RectangleF rectangle, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(0.1f, Math.Min(radius * 2f, Math.Min(rectangle.Width, rectangle.Height)));
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawGlyph(Graphics graphics, ActionGlyph glyph, Rectangle bounds, Color color, float scale)
        {
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TranslateTransform(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
                graphics.ScaleTransform(scale, scale);
                using (Pen pen = new Pen(color, 1.65f))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    if (glyph == ActionGlyph.Refresh)
                    {
                        graphics.DrawArc(pen, -7, -7, 14, 14, 35, 260);
                        graphics.DrawLines(pen, new[] { new PointF(3, -8), new PointF(7, -7), new PointF(6, -3) });
                    }
                    else if (glyph == ActionGlyph.Dashboard)
                    {
                        graphics.DrawLines(pen, new[] { new PointF(0, -6), new PointF(-7, -6), new PointF(-7, 7), new PointF(6, 7), new PointF(6, 0) });
                        graphics.DrawLines(pen, new[] { new PointF(2, -8), new PointF(8, -8), new PointF(8, -2) });
                        graphics.DrawLine(pen, 0, 0, 8, -8);
                    }
                    else if (glyph == ActionGlyph.Power)
                    {
                        graphics.DrawArc(pen, -7, -7, 14, 14, -55, 290);
                        graphics.DrawLine(pen, 0, -9, 0, -1);
                    }
                    else if (glyph == ActionGlyph.Settings)
                    {
                        PointF[] points = new PointF[32];
                        for (int index = 0; index < points.Length; index++)
                        {
                            double angle = index * Math.PI * 2 / points.Length;
                            float radius = index % 4 == 0 || index % 4 == 3 ? 8f : 6.4f;
                            points[index] = new PointF((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                        }
                        graphics.DrawPolygon(pen, points);
                        graphics.DrawEllipse(pen, -2.5f, -2.5f, 5, 5);
                    }
                    else
                    {
                        graphics.DrawRectangle(pen, -8, -7, 16, 14);
                        graphics.DrawLines(pen, new[] { new PointF(-4, -3), new PointF(-1, 0), new PointF(-4, 3) });
                        graphics.DrawLine(pen, 1, 3, 5, 3);
                    }
                }
            }
            finally { graphics.Restore(state); }
        }
    }
}
