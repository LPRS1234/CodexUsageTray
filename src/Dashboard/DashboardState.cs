using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexUsageTray
{
    internal sealed class DashboardState
    {
        private readonly string _status;
        private readonly AccountSnapshot _account;
        private readonly TokenUsageSnapshot _usage;
        private readonly RateLimitSnapshot _rateLimits;
        private readonly DateTime? _updatedAt;
        private readonly string _message;

        private DashboardState(string status, AccountSnapshot account, TokenUsageSnapshot usage,
            RateLimitSnapshot rateLimits, DateTime? updatedAt, string message)
        {
            _status = status;
            _account = account;
            _usage = usage;
            _rateLimits = rateLimits;
            _updatedAt = updatedAt;
            _message = message;
        }

        public static DashboardState CreateLoading()
        {
            return new DashboardState("loading", null, null, null, null, null);
        }

        public static DashboardState CreateReady(AccountSnapshot account, TokenUsageSnapshot usage,
            RateLimitSnapshot rateLimits, DateTime updatedAt, string warning)
        {
            return new DashboardState(string.IsNullOrWhiteSpace(warning) ? "ready" : "warning",
                account, usage, rateLimits, updatedAt, warning);
        }

        public static DashboardState CreateStale(AccountSnapshot account, TokenUsageSnapshot usage,
            RateLimitSnapshot rateLimits, DateTime updatedAt, string message)
        {
            return new DashboardState("stale", account, usage, rateLimits, updatedAt, message);
        }

        public Dictionary<string, object> ToDictionary()
        {
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "status", _status },
                { "updatedAt", _updatedAt.HasValue ? _updatedAt.Value.ToString("o", CultureInfo.InvariantCulture) : null },
                { "message", _message },
                { "account", _account == null ? null : _account.ToDictionary() },
                { "usage", _usage == null ? null : _usage.ToDictionary() }
            };

            List<Dictionary<string, object>> limits = new List<Dictionary<string, object>>();
            if (_rateLimits != null)
            {
                foreach (RateLimitWindow window in _rateLimits.Windows)
                {
                    limits.Add(new Dictionary<string, object>
                    {
                        { "label", window.DurationText },
                        { "bucket", window.BucketName },
                        { "remainingPercent", window.RemainingPercent },
                        { "usedPercent", 100 - window.RemainingPercent },
                        { "durationMinutes", window.WindowDurationMinutes },
                        { "resetsAt", window.ResetsAtUnixSeconds > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(window.ResetsAtUnixSeconds)
                                .ToLocalTime().ToString("o", CultureInfo.InvariantCulture)
                            : null }
                    });
                }
            }
            result["rateLimits"] = limits;
            return result;
        }
    }

}
