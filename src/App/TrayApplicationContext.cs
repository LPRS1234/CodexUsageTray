using System;
using System.Collections.Generic;
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

        private readonly CornerUsageCard _widget;
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
        private readonly ToolStripMenuItem[] _positionItems;
        private readonly ToolStripMenuItem[] _displayModeItems;
        private DashboardServer _dashboardServer;
        private CardCorner _selectedCorner;
        private UsageDisplayMode _displayMode;
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
            refreshItem.Click += delegate { RefreshAsync(); };

            ToolStripMenuItem dashboardItem = new ToolStripMenuItem("대시보드로 이동");
            dashboardItem.Name = "DashboardItem";
            dashboardItem.Font = new Font(dashboardItem.Font, FontStyle.Bold);
            dashboardItem.Click += OpenDashboard;

            _selectedCorner = _settings.LoadCardCorner();
            _positionItems = new[]
            {
                CreatePositionItem("왼쪽 위", CardCorner.TopLeft),
                CreatePositionItem("오른쪽 위", CardCorner.TopRight),
                CreatePositionItem("왼쪽 아래", CardCorner.BottomLeft),
                CreatePositionItem("오른쪽 아래", CardCorner.BottomRight)
            };
            ToolStripMenuItem positionMenu = new ToolStripMenuItem("카드 위치");
            positionMenu.DropDownItems.AddRange(_positionItems);
            UpdatePositionChecks();

            _displayMode = _settings.LoadUsageDisplayMode();
            _displayModeItems = new[]
            {
                CreateDisplayModeItem("5시간", UsageDisplayMode.FiveHours),
                CreateDisplayModeItem("7일", UsageDisplayMode.SevenDays),
                CreateDisplayModeItem("5시간 + 7일", UsageDisplayMode.Both)
            };
            ToolStripMenuItem displayModeMenu = new ToolStripMenuItem("표시할 사용량");
            displayModeMenu.DropDownItems.AddRange(_displayModeItems);
            UpdateDisplayModeChecks();

            _autoStartItem = new ToolStripMenuItem("Windows 시작 시 자동 실행");
            _autoStartItem.Checked = _autoStart.IsEnabled();
            _autoStartItem.Click += ToggleAutoStart;

            ToolStripMenuItem aboutItem = new ToolStripMenuItem("정보");
            aboutItem.Click += ShowAbout;

            _updateItem = new ToolStripMenuItem("업데이트 확인");
            _updateItem.Click += delegate { CheckForUpdatesAsync(true); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("종료");
            exitItem.Name = "ExitItem";
            exitItem.Click += delegate { ExitApplication(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(_statusItem);
            foreach (ToolStripMenuItem item in _detailItems)
            {
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(dashboardItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(displayModeMenu);
            menu.Items.Add(positionMenu);
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(_updateItem);
            menu.Items.Add(aboutItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            DashboardMenuRenderer menuRenderer = new DashboardMenuRenderer();
            menuRenderer.ApplyTo(menu, 360);
            menu.Opening += delegate
            {
                _autoStartItem.Checked = _autoStart.IsEnabled();
                UpdatePositionChecks();
                UpdateDisplayModeChecks();
            };

            string logoPath = Path.Combine(Application.StartupPath, "assets", "codex-terminal.png");
            _widget = new CornerUsageCard(menu, logoPath, _selectedCorner, _displayMode);
            _widget.Update(null, null, IconState.Loading);

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
            }

            UsageRefreshResult result = await _refreshService.RefreshAsync();
            if (result == null)
            {
                return;
            }

            if (result.Succeeded)
            {
                ApplySnapshot(result.Snapshot);
            }
            else
            {
                ApplyError(result.ErrorMessage);
            }
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

            List<string> summary = new List<string>();
            if (fiveHourRemaining.HasValue)
            {
                summary.Add("5시간 " + fiveHourRemaining.Value.ToString(CultureInfo.InvariantCulture) + "%");
            }
            if (sevenDayRemaining.HasValue)
            {
                summary.Add("7일 " + sevenDayRemaining.Value.ToString(CultureInfo.InvariantCulture) + "%");
            }
            _statusItem.Text = summary.Count == 0
                ? "Codex 남은 사용량"
                : "Codex 남은 사용량 · " + string.Join(" · ", summary);

            for (int i = 0; i < _detailItems.Length; i++)
            {
                if (i < snapshot.Windows.Count)
                {
                    _detailItems[i].Text = snapshot.Windows[i].ToDisplayText();
                    _detailItems[i].Visible = true;
                }
                else
                {
                    _detailItems[i].Visible = false;
                }
            }

            _widget.Update(fiveHourRemaining, sevenDayRemaining, IconState.Normal);
        }

        private void ApplyError(string message)
        {
            _statusItem.Text = "갱신 실패: " + message;
            foreach (ToolStripMenuItem item in _detailItems)
            {
                item.Visible = false;
            }

            RateLimitSnapshot lastSnapshot = _refreshService.LastSnapshot;
            if (lastSnapshot == null)
            {
                _widget.Update(null, null, IconState.Error);
            }
            else
            {
                RateLimitWindow fiveHourWindow = lastSnapshot.GetFiveHourWindow();
                RateLimitWindow sevenDayWindow = lastSnapshot.GetSevenDayWindow();
                _widget.Update(
                    fiveHourWindow == null ? (int?)null : fiveHourWindow.RemainingPercent,
                    sevenDayWindow == null ? (int?)null : sevenDayWindow.RemainingPercent,
                    IconState.Stale);
            }
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            string autoStartStatus = _autoStart.IsEnabled() ? "켜짐" : "꺼짐";

            MessageBox.Show(
                "Codex 사용량 카드  ·  버전 " + typeof(Program).Assembly.GetName().Version.ToString(3) + "\r\n\r\n" +
                "표시 기준\r\n" +
                "5시간, 7일 또는 두 사용량을 함께 표시할 수 있습니다.\r\n\r\n" +
                "대시보드\r\n" +
                "누적 토큰, 일별 사용량, 계정과 남은 사용량을 로컬에서 표시합니다.\r\n\r\n" +
                "갱신 방식\r\n" +
                "사용량 변경 알림을 즉시 반영하고, 10초마다 다시 확인합니다.\r\n\r\n" +
                "Windows 로그인 시 자동 실행: " + autoStartStatus + "\r\n" +
                "설치 버전은 시작 후와 6시간마다 새 버전을 자동 확인합니다.\r\n" +
                "별도 API 키 불필요 · 인증 정보 저장 안 함",
                "Codex 사용량 카드 정보",
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
                _widget.SetDisplayMode(newMode);
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
            }
        }

        private ToolStripMenuItem CreatePositionItem(string text, CardCorner corner)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = corner;
            item.Click += ChangeCardPosition;
            return item;
        }

        private void ChangeCardPosition(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null || item.Tag == null)
            {
                return;
            }

            CardCorner newCorner = (CardCorner)item.Tag;
            try
            {
                _settings.SaveCardCorner(newCorner);
                _selectedCorner = newCorner;
                UpdatePositionChecks();
                _widget.SetCorner(newCorner);
            }
            catch (Exception ex)
            {
                MessageBox.Show("카드 위치를 변경하지 못했습니다.\r\n\r\n" + ex.Message,
                    "Codex 사용량", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdatePositionChecks()
        {
            foreach (ToolStripMenuItem item in _positionItems)
            {
                item.Checked = (CardCorner)item.Tag == _selectedCorner;
            }
        }

        private void ToggleAutoStart(object sender, EventArgs e)
        {
            try
            {
                _autoStart.Toggle();
                _autoStartItem.Checked = _autoStart.IsEnabled();
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
            _updateTimer.Stop();
            _updateTimer.Dispose();

            _refreshService.Dispose();

            if (_dashboardServer != null)
            {
                _dashboardServer.Dispose();
            }

            _widget.Dispose();
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
