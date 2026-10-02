using System;
using System.IO;
using System.Net;

namespace CodexUsageTray.Tests
{
    internal static class DashboardServerSmokeTest
    {
        private static bool _refreshRequested;
        private static string _theme;

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
                delegate { _refreshRequested = true; },
                delegate { return _theme; },
                delegate(string theme) { _theme = theme; }))
            {
                server.EnsureStarted();

                string dashboardHtml = Get(server.Url);
                AssertContains(dashboardHtml, "/dashboard.css", "HTML stylesheet reference");
                AssertContains(dashboardHtml, "/dashboard.js", "HTML script reference");
                AssertContains(dashboardHtml, "themeButton", "dashboard theme control");
                string dashboardStyles = Get(server.Url + "dashboard.css");
                AssertContains(dashboardStyles, ".shell", "dashboard CSS");
                AssertContains(dashboardStyles, "data-theme=\"dark\"", "dark theme styles");
                AssertContains(dashboardStyles, "--token-surface", "theme-aware lifetime token card");
                string dashboardScript = Get(server.Url + "dashboard.js");
                AssertContains(dashboardScript, "loadSnapshot", "dashboard script");
                AssertContains(dashboardScript, "bar-tooltip", "daily token tooltip");
                AssertContains(dashboardScript, "/api/theme/", "theme preference storage");
                AssertContains(Get(server.Url + "api/snapshot"), "\"status\":\"loading\"", "snapshot API");

                HttpWebRequest refreshRequest = (HttpWebRequest)WebRequest.Create(server.Url + "api/refresh");
                refreshRequest.Method = "POST";
                using (HttpWebResponse response = (HttpWebResponse)refreshRequest.GetResponse())
                {
                    Assert(response.StatusCode == HttpStatusCode.Accepted, "refresh API status");
                }
                Assert(_refreshRequested, "refresh callback");

                HttpWebRequest themeRequest = (HttpWebRequest)WebRequest.Create(
                    server.Url + "api/theme/dark");
                themeRequest.Method = "POST";
                using (HttpWebResponse response = (HttpWebResponse)themeRequest.GetResponse())
                {
                    Assert(response.StatusCode == HttpStatusCode.NoContent, "theme API status");
                }
                Assert(string.Equals(_theme, "dark", StringComparison.Ordinal), "theme callback");
                AssertContains(Get(server.Url), "data-theme=\"dark\"", "persisted dashboard theme");
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
