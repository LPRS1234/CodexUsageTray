using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexUsageTray
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const int RefreshIntervalMilliseconds = 10000;

        private readonly NumericTrayIcon _trayIcon;
        private readonly UsagePopupForm _popup;
        private readonly ContextMenuStrip _menu;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly System.Windows.Forms.Timer _updateTimer;
        private readonly Control _dispatcher;
        private readonly AppSettingsStore _settings;
        private readonly AutoStartService _autoStart;
        private readonly UsageRefreshService _refreshService;
        private readonly AppUpdateService _updateService;
        private readonly ToolStripMenuItem _updateItem;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem[] _detailItems;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ToolStripMenuItem[] _displayModeItems;
        private readonly ToolStripMenuItem _displayModeMenu;
        private DashboardServer _dashboardServer;
        private UsageDisplayMode _displayMode;
        private DateTime? _lastSuccessfulUpdate;
        private volatile string _dashboardTheme;
        private bool _exiting;
        private bool _checkingForUpdate;

        public TrayApplicationContext()
        {
            _dispatcher = new Control();
            IntPtr ignored = _dispatcher.Handle;
            _settings = new AppSettingsStore();
            _dashboardTheme = _settings.LoadDashboardTheme();
            _autoStart = new AutoStartService(Application.ExecutablePath);
            _refreshService = new UsageRefreshService();
            _refreshService.RefreshRequested += OnRateLimitsChanged;
            _updateService = new AppUpdateService();

            _statusItem = new ToolStripMenuItem("사용량을 불러오는 중...")
            {
                Enabled = false,
                Name = "UsageHeader"
            };
            _detailItems = new[]
            {
                new ToolStripMenuItem { Enabled = false, Visible = false, Name = "UsageDetail" },
                new ToolStripMenuItem { Enabled = false, Visible = false, Name = "UsageDetail" },
                new ToolStripMenuItem { Enabled = false, Visible = false, Name = "UsageDetail" }
            };

            ToolStripMenuItem refreshItem = new ToolStripMenuItem("지금 새로고침");
            refreshItem.Name = "RefreshItem";
            refreshItem.Click += delegate { RefreshAsync(); };

            ToolStripMenuItem dashboardItem = new ToolStripMenuItem("대시보드 이동");
            dashboardItem.Name = "DashboardItem";
            dashboardItem.Font = new Font(dashboardItem.Font, FontStyle.Bold);
            dashboardItem.Click += OpenDashboard;

            _displayMode = _settings.LoadUsageDisplayMode();
            if (_displayMode != UsageDisplayMode.SevenDays)
            {
                _displayMode = UsageDisplayMode.FiveHours;
            }
            _displayModeItems = new[]
            {
                CreateDisplayModeItem("5시간", UsageDisplayMode.FiveHours),
                CreateDisplayModeItem("7일", UsageDisplayMode.SevenDays)
            };
            _displayModeMenu = new ToolStripMenuItem("아이콘에 표시할 사용량");
            _displayModeMenu.Name = "DisplayModeItem";
            _displayModeMenu.DropDownItems.AddRange(_displayModeItems);
            UpdateDisplayModeChecks();

            _autoStartItem = new ToolStripMenuItem("Windows 시작 시 자동 실행");
            _autoStartItem.Name = "AutoStartItem";
            _autoStartItem.Checked = _autoStart.IsEnabled();
            _autoStartItem.Click += ToggleAutoStart;

            ToolStripMenuItem aboutItem = new ToolStripMenuItem("정보");
            aboutItem.Name = "AboutItem";
            aboutItem.Click += ShowAbout;

            _updateItem = new ToolStripMenuItem("업데이트 확인");
            _updateItem.Name = "UpdateItem";
            _updateItem.Click += delegate { CheckForUpdatesAsync(true); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("종료");
            exitItem.Name = "ExitItem";
            exitItem.Click += delegate { ExitApplication(); };

            UsageOptionsMenu menu = new UsageOptionsMenu();
            menu.Items.Add(new ToolStripMenuItem("Codex 설정")
            {
                Enabled = false,
                Name = "SettingsHeader"
            });
            menu.Items.Add(_statusItem);
            foreach (ToolStripMenuItem item in _detailItems)
            {
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(dashboardItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_displayModeMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(_updateItem);
            menu.Items.Add(aboutItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            DashboardMenuRenderer menuRenderer = new DashboardMenuRenderer();
            menuRenderer.ApplyTo(menu, 320);
            menu.Opening += delegate
            {
                _autoStartItem.Checked = _autoStart.IsEnabled();
                UpdateDisplayModeChecks();
            };
            _menu = menu;
            menu.KeyboardDismissed += delegate { _trayIcon.RestoreFocus(); };

            _popup = new UsagePopupForm();
            _popup.RefreshRequested += delegate { RefreshAsync(); };
            _popup.DashboardRequested += OpenDashboard;
            _popup.AutoStartRequested += ToggleAutoStart;
            _popup.SettingsRequested += delegate
            {
                ShowOptionsMenu(new Rectangle(_popup.Right - 1, _popup.Bottom - 1, 1, 1));
            };
            _popup.KeyboardDismissed += delegate { _trayIcon.RestoreFocus(); };
            _popup.SetAutoStart(_autoStartItem.Checked);
            _popup.UpdateUsage(null, null, IconState.Loading, null, null);

            _trayIcon = new NumericTrayIcon(_displayMode);
            _trayIcon.LeftClick += delegate(Rectangle anchor)
            {
                _menu.Close();
                _popup.SetAutoStart(_autoStart.IsEnabled());
                _popup.ToggleAt(anchor);
                if (!_popup.Visible)
                {
                    _trayIcon.RestoreFocus();
                }
            };
            _trayIcon.RightClick += ShowOptionsMenu;
            _trayIcon.Update(null, null, IconState.Loading);

            string logoPath = Path.Combine(Application.StartupPath, "assets", "codex-terminal.png");

            string dashboardPath = Path.Combine(Application.StartupPath, "assets", "dashboard.html");
            _dashboardServer = new DashboardServer(
                dashboardPath, logoPath, GetDashboardState, RequestDashboardRefresh,
                GetDashboardTheme, SaveDashboardTheme);
            _dashboardServer.EnsureStarted();

            _refreshTimer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMilliseconds };
            _refreshTimer.Tick += delegate { RefreshAsync(); };
            _refreshTimer.Start();

            _updateTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _updateTimer.Tick += delegate
            {
                _updateTimer.Interval = 6 * 60 * 60 * 1000;
                CheckForUpdatesAsync(false);
            };
            _updateTimer.Start();

            _dispatcher.BeginInvoke(new Action(RefreshAsync));
        }

        private async void RefreshAsync()
        {
            if (_exiting)
            {
                return;
            }

            if (_refreshService.LastSnapshot == null)
            {
                _statusItem.Text = "사용량을 불러오는 중...";
                _statusItem.Visible = true;
            }

            _popup.SetRefreshing(true);
            UsageRefreshResult result = await _refreshService.RefreshAsync();
            if (_exiting)
            {
                return;
            }
            if (result == null)
            {
                return;
            }
            _popup.SetRefreshing(false);

            if (result.Succeeded)
            {
                ApplySnapshot(result.Snapshot);
            }
            else
            {
                ApplyError(result.ErrorMessage);
            }
        }

        private void ShowOptionsMenu(Rectangle anchor)
        {
            _popup.Hide();
            _menu.Close();
            _menu.Show(new Point(anchor.Right, anchor.Top), ToolStripDropDownDirection.AboveLeft);
        }

        private void OpenDashboard(object sender, EventArgs e)
        {
            try
            {
                if (_dashboardServer == null)
                {
                    string dashboardPath = Path.Combine(
                        Application.StartupPath, "assets", "dashboard.html");
                    string logoPath = Path.Combine(
                        Application.StartupPath, "assets", "codex-terminal.png");
                    _dashboardServer = new DashboardServer(
                        dashboardPath, logoPath, GetDashboardState, RequestDashboardRefresh,
                        GetDashboardTheme, SaveDashboardTheme);
                }

                _dashboardServer.EnsureStarted();
                RequestDashboardRefresh();
                Process.Start(new ProcessStartInfo
                {
                    FileName = _dashboardServer.Url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("대시보드를 열지 못했습니다.\r\n\r\n" + ex.Message,
                    "Codex 사용량", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private DashboardState GetDashboardState()
        {
            return _refreshService.State;
        }

        private string GetDashboardTheme()
        {
            return _dashboardTheme;
        }

        private void SaveDashboardTheme(string theme)
        {
            _settings.SaveDashboardTheme(theme);
            _dashboardTheme = theme;
        }

        private void RequestDashboardRefresh()
        {
            _refreshService.RequestDetailsRefresh();
            if (_exiting || _dispatcher.IsDisposed)
            {
                return;
            }

            try
            {
                _dispatcher.BeginInvoke(new Action(RefreshAsync));
            }
            catch (InvalidOperationException)
            {
                // The application is shutting down.
            }
        }

        private void OnRateLimitsChanged(object sender, EventArgs e)
        {
            if (_exiting || _dispatcher.IsDisposed)
            {
                return;
            }

            try
            {
                _dispatcher.BeginInvoke(new Action(RefreshAsync));
            }
            catch (InvalidOperationException)
            {
                // The application is shutting down.
            }
        }

        private void ApplySnapshot(RateLimitSnapshot snapshot)
        {
            if (snapshot.Windows.Count == 0)
            {
                ApplyError("사용량 정보가 없습니다. Codex에 ChatGPT 계정으로 로그인했는지 확인하세요.");
                return;
            }

            RateLimitWindow fiveHourWindow = snapshot.GetFiveHourWindow();
            RateLimitWindow sevenDayWindow = snapshot.GetSevenDayWindow();
            int? fiveHourRemaining = fiveHourWindow == null
                ? (int?)null
                : fiveHourWindow.RemainingPercent;
            int? sevenDayRemaining = sevenDayWindow == null
                ? (int?)null
                : sevenDayWindow.RemainingPercent;

            _statusItem.Visible = false;

            for (int i = 0; i < _detailItems.Length; i++)
            {
                if (i < snapshot.Windows.Count)
                {
                    RateLimitWindow window = snapshot.Windows[i];
                    _detailItems[i].Text = window.DurationText + " " +
                        window.RemainingPercent.ToString(CultureInfo.InvariantCulture) + "% 남음 · 초기화 " +
                        (window.ResetsAtUnixSeconds > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(window.ResetsAtUnixSeconds)
                                .LocalDateTime.ToString("M/d HH:mm", CultureInfo.CurrentCulture)
                            : "시각 없음");
                    _detailItems[i].Visible = true;
                }
                else
                {
                    _detailItems[i].Visible = false;
                }
            }

            _lastSuccessfulUpdate = DateTime.Now;
            _trayIcon.Update(fiveHourRemaining, sevenDayRemaining, IconState.Normal);
            _popup.UpdateUsage(fiveHourWindow, sevenDayWindow, IconState.Normal,
                null, _lastSuccessfulUpdate);
        }

        private void ApplyError(string message)
        {
            _statusItem.Text = "갱신 실패: " + message;
            _statusItem.Visible = true;
            foreach (ToolStripMenuItem item in _detailItems)
            {
                item.Visible = false;
            }

            RateLimitSnapshot lastSnapshot = _refreshService.LastSnapshot;
            if (lastSnapshot == null)
            {
                _trayIcon.Update(null, null, IconState.Error);
                _popup.UpdateUsage(null, null, IconState.Error, message, _lastSuccessfulUpdate);
            }
            else
            {
                RateLimitWindow fiveHourWindow = lastSnapshot.GetFiveHourWindow();
                RateLimitWindow sevenDayWindow = lastSnapshot.GetSevenDayWindow();
                _trayIcon.Update(
                    fiveHourWindow == null ? (int?)null : fiveHourWindow.RemainingPercent,
                    sevenDayWindow == null ? (int?)null : sevenDayWindow.RemainingPercent,
                    IconState.Stale);
                _popup.UpdateUsage(fiveHourWindow, sevenDayWindow, IconState.Stale,
                    message, _lastSuccessfulUpdate);
            }
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            string autoStartStatus = _autoStart.IsEnabled() ? "켜짐" : "꺼짐";

            MessageBox.Show(
                "Codex 사용량 트레이  ·  버전 " + typeof(Program).Assembly.GetName().Version.ToString(3) + "\r\n\r\n" +
                "표시 기준\r\n" +
                "시스템 트레이 숫자는 선택한 기간의 남은 비율(%)입니다.\r\n" +
                "왼쪽 클릭으로 5시간과 7일을 함께 보고, 오른쪽 클릭으로 설정을 엽니다.\r\n\r\n" +
                "대시보드\r\n" +
                "누적 토큰, 일별 사용량, 계정과 남은 사용량을 로컬에서 표시합니다.\r\n\r\n" +
                "갱신 방식\r\n" +
                "사용량 변경 알림을 즉시 반영하고, 10초마다 다시 확인합니다.\r\n\r\n" +
                "Windows 로그인 시 자동 실행: " + autoStartStatus + "\r\n" +
                "설치 버전은 시작 후와 6시간마다 새 버전을 자동 확인합니다.\r\n" +
                "별도 API 키 불필요 · 인증 정보 저장 안 함",
                "Codex 사용량 트레이 정보",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private ToolStripMenuItem CreateDisplayModeItem(string text, UsageDisplayMode mode)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = mode;
            item.Click += ChangeDisplayMode;
            return item;
        }

        private void ChangeDisplayMode(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null || item.Tag == null)
            {
                return;
            }

            UsageDisplayMode newMode = (UsageDisplayMode)item.Tag;
            try
            {
                _settings.SaveUsageDisplayMode(newMode);
                _displayMode = newMode;
                UpdateDisplayModeChecks();
                _trayIcon.SetDisplayMode(newMode);
            }
            catch (Exception ex)
            {
                MessageBox.Show("표시할 사용량을 변경하지 못했습니다.\r\n\r\n" + ex.Message,
                    "Codex 사용량", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateDisplayModeChecks()
        {
            foreach (ToolStripMenuItem item in _displayModeItems)
            {
                item.Checked = (UsageDisplayMode)item.Tag == _displayMode;
                if (item.Checked)
                {
                    _displayModeMenu.ShortcutKeyDisplayString = item.Text;
                }
            }
        }

        private void ToggleAutoStart(object sender, EventArgs e)
        {
            try
            {
                _autoStart.Toggle();
                _autoStartItem.Checked = _autoStart.IsEnabled();
                _popup.SetAutoStart(_autoStartItem.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show("자동 실행 설정을 변경하지 못했습니다.\r\n\r\n" + ex.Message,
                    "Codex 사용량", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void CheckForUpdatesAsync(bool interactive)
        {
            if (_exiting || _checkingForUpdate)
            {
                return;
            }

            _checkingForUpdate = true;
            _updateItem.Enabled = false;
            _updateItem.Text = "업데이트 확인 중...";
            string setupPath = null;
            try
            {
                if (!AppUpdateService.IsInstalled(Application.StartupPath))
                {
                    if (interactive)
                    {
                        MessageBox.Show("Setup 파일로 설치한 앱에서 자동 업데이트를 사용할 수 있습니다.",
                            "Codex 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                UpdateRelease release = await _updateService.CheckAsync(typeof(Program).Assembly.GetName().Version);
                if (_exiting) { return; }
                if (release == null)
                {
                    if (interactive)
                    {
                        MessageBox.Show("현재 사용할 수 있는 새 버전이 없습니다.", "Codex 업데이트",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                _updateItem.Text = "업데이트 다운로드 중...";
                setupPath = await _updateService.DownloadAsync(release);
                if (_exiting) { return; }
                if (!AppUpdateService.IsInstalled(Application.StartupPath))
                {
                    throw new InvalidOperationException("설치 정보를 확인할 수 없습니다.");
                }

                string readyEventName = @"Local\CodexUsageTray.UpdateReady." + Guid.NewGuid().ToString("N");
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = setupPath,
                    Arguments = "/update " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + " \"" + readyEventName + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(setupPath)
                };
                using (EventWaitHandle ready = new EventWaitHandle(false, EventResetMode.AutoReset, readyEventName))
                {
                    using (Process installer = Process.Start(startInfo))
                    {
                        if (installer == null) { throw new InvalidOperationException("업데이트 설치를 시작할 수 없습니다."); }
                    }
                    setupPath = null;
                    bool canUpdate = await Task.Run(() => ready.WaitOne(15000));
                    if (_exiting) { return; }
                    if (!canUpdate) { throw new InvalidOperationException("업데이트 설치 준비를 확인할 수 없습니다."); }
                }
                ExitApplication();
            }
            catch
            {
                if (interactive && !_exiting)
                {
                    MessageBox.Show("업데이트를 확인하거나 설치하지 못했습니다. 현재 버전은 계속 사용할 수 있습니다.\r\n" +
                        "인터넷 연결을 확인한 뒤 다시 시도하세요.", "Codex 업데이트",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (setupPath != null)
                {
                    try { File.Delete(setupPath); Directory.Delete(Path.GetDirectoryName(setupPath)); } catch { }
                }
                _checkingForUpdate = false;
                if (!_exiting)
                {
                    _updateItem.Text = "업데이트 확인";
                    _updateItem.Enabled = true;
                }
            }
        }

        private void ExitApplication()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _updateTimer.Stop();
            _updateTimer.Dispose();

            _refreshService.Dispose();

            if (_dashboardServer != null)
            {
                _dashboardServer.Dispose();
            }

            _trayIcon.Dispose();
            _popup.Dispose();
            _menu.Dispose();
            _dispatcher.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_exiting)
            {
                ExitApplication();
            }

            base.Dispose(disposing);
        }

    }

}
