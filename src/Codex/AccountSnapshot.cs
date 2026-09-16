using System.Collections.Generic;

namespace CodexUsageTray
{
    internal sealed class AccountSnapshot
    {
        public string Type { get; private set; }
        public string Email { get; private set; }
        public string PlanType { get; private set; }

        private AccountSnapshot(string type, string email, string planType)
        {
            Type = type;
            Email = email;
            PlanType = planType;
        }

        public static AccountSnapshot FromResult(Dictionary<string, object> result)
        {
            object accountValue;
            Dictionary<string, object> account = result.TryGetValue("account", out accountValue)
                ? accountValue as Dictionary<string, object>
                : null;
            if (account == null)
            {
                return new AccountSnapshot(null, null, null);
            }

            return new AccountSnapshot(JsonValueReader.GetString(account, "type"),
                JsonValueReader.GetString(account, "email"),
                JsonValueReader.GetString(account, "planType"));
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "type", Type },
                { "email", Email },
                { "planType", PlanType }
            };
        }

    }

}
