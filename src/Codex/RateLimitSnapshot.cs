using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CodexUsageTray
{
    internal sealed class RateLimitSnapshot
    {
        public List<RateLimitWindow> Windows { get; private set; }

        public int MostRestrictiveRemainingPercent
        {
            get { return Windows.Count == 0 ? 0 : Windows.Min(window => window.RemainingPercent); }
        }

        private RateLimitSnapshot(List<RateLimitWindow> windows)
        {
            Windows = windows;
        }

        public RateLimitWindow GetFiveHourWindow()
        {
            return GetWindowByDuration(300, "단기");
        }

        public RateLimitWindow GetSevenDayWindow()
        {
            return GetWindowByDuration(7 * 1440, "장기");
        }

        private RateLimitWindow GetWindowByDuration(int durationMinutes, string fallbackKind)
        {
            RateLimitWindow exact = Windows
                .Where(window => window.WindowDurationMinutes == durationMinutes)
                .OrderBy(window => window.RemainingPercent)
                .FirstOrDefault();
            if (exact != null)
            {
                return exact;
            }

            return Windows
                .Where(window => string.Equals(window.Kind, fallbackKind,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(window => window.RemainingPercent)
                .FirstOrDefault();
        }

        public static RateLimitSnapshot FromResult(Dictionary<string, object> result)
        {
            List<RateLimitWindow> windows = new List<RateLimitWindow>();
            object bucketsValue;
            Dictionary<string, object> buckets = result.TryGetValue("rateLimitsByLimitId", out bucketsValue)
                ? bucketsValue as Dictionary<string, object>
                : null;

            if (buckets != null && buckets.Count > 0)
            {
                foreach (KeyValuePair<string, object> entry in buckets)
                {
                    Dictionary<string, object> bucket = entry.Value as Dictionary<string, object>;
                    if (bucket != null)
                    {
                        AddBucketWindows(windows, entry.Key, bucket);
                    }
                }
            }
            else
            {
                object legacyValue;
                Dictionary<string, object> legacy = result.TryGetValue("rateLimits", out legacyValue)
                    ? legacyValue as Dictionary<string, object>
                    : null;
                if (legacy != null)
                {
                    string id = JsonValueReader.GetString(legacy, "limitId") ?? "codex";
                    AddBucketWindows(windows, id, legacy);
                }
            }

            windows = windows
                .OrderBy(window => window.WindowDurationMinutes)
                .ThenBy(window => window.BucketName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new RateLimitSnapshot(windows);
        }

        private static void AddBucketWindows(List<RateLimitWindow> windows, string fallbackName,
            Dictionary<string, object> bucket)
        {
            string name = JsonValueReader.GetString(bucket, "limitName") ??
                JsonValueReader.GetString(bucket, "limitId") ?? fallbackName;
            AddWindow(windows, name, "단기", bucket, "primary");
            AddWindow(windows, name, "장기", bucket, "secondary");
        }

        private static void AddWindow(List<RateLimitWindow> windows, string bucketName, string kind,
            Dictionary<string, object> bucket, string key)
        {
            object windowValue;
            Dictionary<string, object> window = bucket.TryGetValue(key, out windowValue)
                ? windowValue as Dictionary<string, object>
                : null;
            if (window == null || !window.ContainsKey("usedPercent"))
            {
                return;
            }

            double used = Convert.ToDouble(window["usedPercent"], CultureInfo.InvariantCulture);
            int remaining = (int)Math.Round(Math.Max(0, Math.Min(100, 100 - used)),
                MidpointRounding.AwayFromZero);
            int duration = window.ContainsKey("windowDurationMins")
                ? Convert.ToInt32(window["windowDurationMins"], CultureInfo.InvariantCulture)
                : 0;
            long reset = window.ContainsKey("resetsAt")
                ? Convert.ToInt64(window["resetsAt"], CultureInfo.InvariantCulture)
                : 0;

            windows.Add(new RateLimitWindow(bucketName, kind, remaining, duration, reset));
        }

        public string ToTooltipText()
        {
            string compact = string.Join(" | ", Windows.Take(2).Select(window =>
                window.DurationText + " " + window.RemainingPercent.ToString(CultureInfo.InvariantCulture) + "%"));
            return "Codex 남음: " + compact;
        }

        public string ToBalloonText()
        {
            return string.Join("\r\n", Windows.Select(window => window.ToDisplayText()));
        }
    }

}
