using System;
using System.Globalization;

namespace CodexUsageTray
{
    internal sealed class RateLimitWindow
    {
        public string BucketName { get; private set; }
        public string Kind { get; private set; }
        public int RemainingPercent { get; private set; }
        public int WindowDurationMinutes { get; private set; }
        public long ResetsAtUnixSeconds { get; private set; }

        public string DurationText
        {
            get
            {
                if (WindowDurationMinutes <= 0)
                {
                    return Kind;
                }

                if (WindowDurationMinutes % 1440 == 0)
                {
                    return (WindowDurationMinutes / 1440).ToString(CultureInfo.InvariantCulture) + "일";
                }

                if (WindowDurationMinutes % 60 == 0)
                {
                    return (WindowDurationMinutes / 60).ToString(CultureInfo.InvariantCulture) + "시간";
                }

                return WindowDurationMinutes.ToString(CultureInfo.InvariantCulture) + "분";
            }
        }

        public RateLimitWindow(string bucketName, string kind, int remainingPercent,
            int windowDurationMinutes, long resetsAtUnixSeconds)
        {
            BucketName = bucketName;
            Kind = kind;
            RemainingPercent = remainingPercent;
            WindowDurationMinutes = windowDurationMinutes;
            ResetsAtUnixSeconds = resetsAtUnixSeconds;
        }

        public string ToDisplayText()
        {
            string resetText = string.Empty;
            if (ResetsAtUnixSeconds > 0)
            {
                DateTime localReset = DateTimeOffset.FromUnixTimeSeconds(ResetsAtUnixSeconds).LocalDateTime;
                resetText = " · 초기화 " + localReset.ToString("M/d HH:mm", CultureInfo.CurrentCulture);
            }

            return DurationText + ": " + RemainingPercent.ToString(CultureInfo.InvariantCulture) + "% 남음" + resetText;
        }
    }
}
