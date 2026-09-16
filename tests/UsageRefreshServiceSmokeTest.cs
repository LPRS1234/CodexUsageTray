using System;

namespace CodexUsageTray.Tests
{
    internal static class UsageRefreshServiceSmokeTest
    {
        public static int Main()
        {
            using (UsageRefreshService service = new UsageRefreshService())
            {
                UsageRefreshResult result = service.RefreshAsync().GetAwaiter().GetResult();
                if (result == null || !result.Succeeded || result.Snapshot == null ||
                    result.Snapshot.Windows.Count == 0)
                {
                    Console.Error.WriteLine("Usage refresh service smoke test failed.");
                    return 1;
                }
            }

            Console.WriteLine("Usage refresh service smoke test passed.");
            return 0;
        }
    }
}
