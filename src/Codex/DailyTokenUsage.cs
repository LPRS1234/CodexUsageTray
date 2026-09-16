using System.Collections.Generic;

namespace CodexUsageTray
{
    internal sealed class DailyTokenUsage
    {
        public string StartDate { get; private set; }
        public long Tokens { get; private set; }

        public DailyTokenUsage(string startDate, long tokens)
        {
            StartDate = startDate;
            Tokens = tokens;
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "date", StartDate },
                { "tokens", Tokens }
            };
        }
    }

}
