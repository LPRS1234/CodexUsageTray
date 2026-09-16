using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexUsageTray
{
    internal static class JsonValueReader
    {
        public static string GetString(Dictionary<string, object> values, string key)
        {
            if (values == null)
            {
                return null;
            }

            object value;
            return values.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : null;
        }

        public static long? GetLong(Dictionary<string, object> values, string key)
        {
            if (values == null)
            {
                return null;
            }

            object value;
            if (!values.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }
    }
}
