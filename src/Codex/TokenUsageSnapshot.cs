using System.Collections.Generic;
using System;
using System.Linq;

namespace CodexUsageTray
{
    internal sealed class TokenUsageSnapshot
    {
        public long? LifetimeTokens { get; private set; }
        public long? PeakDailyTokens { get; private set; }
        public long? LongestRunningTurnSeconds { get; private set; }
        public long? CurrentStreakDays { get; private set; }
        public long? LongestStreakDays { get; private set; }
        public List<DailyTokenUsage> DailyUsage { get; private set; }

        private TokenUsageSnapshot(long? lifetimeTokens, long? peakDailyTokens,
            long? longestRunningTurnSeconds, long? currentStreakDays, long? longestStreakDays,
            List<DailyTokenUsage> dailyUsage)
        {
            LifetimeTokens = lifetimeTokens;
            PeakDailyTokens = peakDailyTokens;
            LongestRunningTurnSeconds = longestRunningTurnSeconds;
            CurrentStreakDays = currentStreakDays;
            LongestStreakDays = longestStreakDays;
            DailyUsage = dailyUsage;
        }

        public static TokenUsageSnapshot FromResult(Dictionary<string, object> result)
        {
            object summaryValue;
            Dictionary<string, object> summary = result.TryGetValue("summary", out summaryValue)
                ? summaryValue as Dictionary<string, object>
                : null;
            List<DailyTokenUsage> dailyUsage = new List<DailyTokenUsage>();
            object dailyValue;
            object[] dailyBuckets = result.TryGetValue("dailyUsageBuckets", out dailyValue)
                ? dailyValue as object[]
                : null;
            if (dailyBuckets != null)
            {
                foreach (object bucketValue in dailyBuckets)
                {
                    Dictionary<string, object> bucket = bucketValue as Dictionary<string, object>;
                    if (bucket == null)
                    {
                        continue;
                    }

                    string startDate = JsonValueReader.GetString(bucket, "startDate");
                    long? tokens = JsonValueReader.GetLong(bucket, "tokens");
                    if (!string.IsNullOrWhiteSpace(startDate) && tokens.HasValue)
                    {
                        dailyUsage.Add(new DailyTokenUsage(startDate, tokens.Value));
                    }
                }
            }

            dailyUsage = dailyUsage.OrderBy(item => item.StartDate, StringComparer.Ordinal).ToList();
            return new TokenUsageSnapshot(
                JsonValueReader.GetLong(summary, "lifetimeTokens"),
                JsonValueReader.GetLong(summary, "peakDailyTokens"),
                JsonValueReader.GetLong(summary, "longestRunningTurnSec"),
                JsonValueReader.GetLong(summary, "currentStreakDays"),
                JsonValueReader.GetLong(summary, "longestStreakDays"),
                dailyUsage);
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "lifetimeTokens", LifetimeTokens },
                { "peakDailyTokens", PeakDailyTokens },
                { "longestRunningTurnSec", LongestRunningTurnSeconds },
                { "currentStreakDays", CurrentStreakDays },
                { "longestStreakDays", LongestStreakDays },
                { "dailyUsage", DailyUsage.Select(item => item.ToDictionary()).ToList() }
            };
        }

    }

}
