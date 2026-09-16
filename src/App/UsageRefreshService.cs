using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodexUsageTray
{
    internal sealed class UsageRefreshService : IDisposable
    {
        private static readonly TimeSpan DetailsRefreshInterval = TimeSpan.FromMinutes(1);

        private CodexAppServerClient _client;
        private RateLimitSnapshot _lastSnapshot;
        private AccountSnapshot _lastAccount;
        private TokenUsageSnapshot _lastTokenUsage;
        private DashboardState _state = DashboardState.CreateLoading();
        private DateTime _lastDetailsRefreshUtc = DateTime.MinValue;
        private bool _forceDetailsRefresh;
        private bool _refreshing;
        private bool _disposed;

        public event EventHandler RefreshRequested;

        public DashboardState State
        {
            get { return _state; }
        }

        public RateLimitSnapshot LastSnapshot
        {
            get { return _lastSnapshot; }
        }

        public void RequestDetailsRefresh()
        {
            _forceDetailsRefresh = true;
        }

        public async Task<UsageRefreshResult> RefreshAsync()
        {
            if (_refreshing || _disposed)
            {
                return null;
            }

            _refreshing = true;
            try
            {
                EnsureClient();

                RateLimitSnapshot snapshot = await _client.ReadRateLimitsAsync();
                _lastSnapshot = snapshot;

                string detailsWarning = null;
                bool shouldRefreshDetails = ShouldRefreshDetails();
                _forceDetailsRefresh = false;
                if (shouldRefreshDetails)
                {
                    detailsWarning = await RefreshDetailsAsync();
                }

                _state = DashboardState.CreateReady(
                    _lastAccount, _lastTokenUsage, snapshot, DateTime.Now, detailsWarning);
                return UsageRefreshResult.Success(snapshot);
            }
            catch (Exception ex)
            {
                string message = FriendlyErrorFormatter.Format(ex);
                _state = DashboardState.CreateStale(
                    _lastAccount, _lastTokenUsage, _lastSnapshot, DateTime.Now, message);
                return UsageRefreshResult.Failure(message);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void EnsureClient()
        {
            if (_client != null && _client.IsRunning)
            {
                return;
            }

            if (_client != null)
            {
                _client.Dispose();
            }

            _client = new CodexAppServerClient();
            _client.RateLimitsChanged += OnRateLimitsChanged;
        }

        private bool ShouldRefreshDetails()
        {
            return _forceDetailsRefresh || _lastAccount == null || _lastTokenUsage == null ||
                DateTime.UtcNow - _lastDetailsRefreshUtc >= DetailsRefreshInterval;
        }

        private async Task<string> RefreshDetailsAsync()
        {
            List<string> errors = new List<string>();
            try
            {
                _lastAccount = await _client.ReadAccountAsync();
            }
            catch (Exception ex)
            {
                errors.Add("계정 정보를 불러오지 못했습니다: " + FriendlyErrorFormatter.Format(ex));
            }

            try
            {
                _lastTokenUsage = await _client.ReadTokenUsageAsync();
            }
            catch (Exception ex)
            {
                errors.Add("토큰 통계를 불러오지 못했습니다: " + FriendlyErrorFormatter.Format(ex));
            }

            _lastDetailsRefreshUtc = DateTime.UtcNow;
            return errors.Count == 0 ? null : string.Join(" ", errors);
        }

        private void OnRateLimitsChanged(object sender, EventArgs e)
        {
            EventHandler handler = RefreshRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
        }
    }

    internal sealed class UsageRefreshResult
    {
        public bool Succeeded { get; private set; }
        public RateLimitSnapshot Snapshot { get; private set; }
        public string ErrorMessage { get; private set; }

        private UsageRefreshResult(bool succeeded, RateLimitSnapshot snapshot, string errorMessage)
        {
            Succeeded = succeeded;
            Snapshot = snapshot;
            ErrorMessage = errorMessage;
        }

        public static UsageRefreshResult Success(RateLimitSnapshot snapshot)
        {
            return new UsageRefreshResult(true, snapshot, null);
        }

        public static UsageRefreshResult Failure(string errorMessage)
        {
            return new UsageRefreshResult(false, null, errorMessage);
        }
    }

    internal static class FriendlyErrorFormatter
    {
        public static string Format(Exception exception)
        {
            string message = exception.GetBaseException().Message;
            if (message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("찾을 수", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Codex CLI를 찾을 수 없습니다.";
            }

            if (message.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Codex 로그인이 필요합니다.";
            }

            return message.Length > 90 ? message.Substring(0, 87) + "..." : message;
        }
    }
}
