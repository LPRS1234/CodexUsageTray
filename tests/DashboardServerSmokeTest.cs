using System;
using System.IO;
using System.Net;

namespace CodexUsageTray.Tests
{
    internal static class DashboardServerSmokeTest
    {
        private static bool _refreshRequested;

        public static int Main()
        {
            try
            {
                Run();
                Console.WriteLine("Dashboard server smoke test passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.GetType().FullName);
                Console.Error.WriteLine(exception.Message);
                Console.Error.WriteLine(exception.StackTrace);
                return 1;
            }
        }

        private static void Run()
        {
            string projectDirectory = Directory.GetCurrentDirectory();
            string assetsDirectory = Path.Combine(projectDirectory, "assets");
            using (DashboardServer server = new DashboardServer(
                Path.Combine(assetsDirectory, "dashboard.html"),
                Path.Combine(assetsDirectory, "codex-terminal.png"),
                DashboardState.CreateLoading,
                delegate { _refreshRequested = true; }))
            {
                server.EnsureStarted();

                AssertContains(Get(server.Url), "/dashboard.css", "HTML stylesheet reference");
                AssertContains(Get(server.Url), "/dashboard.js", "HTML script reference");
                AssertContains(Get(server.Url + "dashboard.css"), ".shell", "dashboard CSS");
                string dashboardScript = Get(server.Url + "dashboard.js");
                AssertContains(dashboardScript, "loadSnapshot", "dashboard script");
                AssertContains(dashboardScript, "bar-tooltip", "daily token tooltip");
                AssertContains(Get(server.Url + "api/snapshot"), "\"status\":\"loading\"", "snapshot API");

                HttpWebRequest refreshRequest = (HttpWebRequest)WebRequest.Create(server.Url + "api/refresh");
                refreshRequest.Method = "POST";
                using (HttpWebResponse response = (HttpWebResponse)refreshRequest.GetResponse())
                {
                    Assert(response.StatusCode == HttpStatusCode.Accepted, "refresh API status");
                }
                Assert(_refreshRequested, "refresh callback");
            }

        }

        private static string Get(string url)
        {
            using (WebResponse response = WebRequest.Create(url).GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static void AssertContains(string actual, string expected, string description)
        {
            Assert(actual.IndexOf(expected, StringComparison.Ordinal) >= 0, description);
        }

        private static void Assert(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Smoke test failed: " + description);
            }
        }
    }
}
