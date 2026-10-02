using System;
using System.IO;

namespace CodexUsageTray.Tests
{
    internal static class UpdateReleaseSmokeTest
    {
        private const string Hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        public static int Main()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CodexUsageTray.UpdateTest." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Run(directory);
                Console.WriteLine("Update release smoke test passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
            finally
            {
                File.Delete(Path.Combine(directory, "setup.exe"));
                Directory.Delete(directory);
            }
        }

        private static void Run(string directory)
        {
            Version current = new Version(1, 7, 0, 0);
            string valid = Fixture("v1.8.0", "false", "false", "https://github.com/LPRS1234/CodexUsageTray/releases/download/v1.8.0/CodexUsageTray-Setup.exe", "sha256:" + Hash, 3);
            UpdateRelease release = UpdateRelease.Parse(valid, current);
            Assert(release != null && release.Version == new Version(1, 8, 0, 0), "new stable version is accepted");
            UpdateRelease revisionRelease = UpdateRelease.Parse(valid.Replace("v1.8.0", "v1.8.0.1"), new Version(1, 8, 0, 0));
            Assert(revisionRelease != null && revisionRelease.Version == new Version(1, 8, 0, 1), "revision update is accepted");
            Assert(UpdateRelease.Parse(valid, new Version(1, 8, 0, 0)) == null, "same version is skipped");
            Assert(UpdateRelease.Parse(valid, new Version(2, 0, 0, 0)) == null, "downgrade is skipped");
            Assert(UpdateRelease.Parse(valid.Replace("\"draft\":false", "\"draft\":true"), current) == null, "draft is skipped");
            Assert(UpdateRelease.Parse(valid.Replace("\"prerelease\":false", "\"prerelease\":true"), current) == null, "prerelease is skipped");
            Assert(UpdateRelease.Parse(valid.Replace("v1.8.0", "v1.8.0-beta"), current) == null, "non-stable tag is skipped");
            AssertRejected(valid.Replace("https://github.com/", "http://github.com/"), current, "HTTP download rejected");
            AssertRejected(valid.Replace("LPRS1234/CodexUsageTray/releases", "someone/other/releases"), current, "other repository rejected");
            AssertRejected(valid.Replace("github.com/", "github.com.evil.invalid/"), current, "other host rejected");
            AssertRejected(valid.Replace("sha256:" + Hash, "sha256:bad"), current, "invalid hash rejected");
            AssertRejected(valid.Replace("sha256:" + Hash, ""), current, "missing hash rejected");
            AssertRejected(valid.Replace("\"size\":3", "\"size\":104857601"), current, "oversized download rejected");
            AssertRejected(valid.Replace("CodexUsageTray-Setup.exe\",\"state", "unexpected.exe\",\"state"), current, "missing setup rejected");

            string file = Path.Combine(directory, "setup.exe");
            File.WriteAllText(file, "abc", new System.Text.UTF8Encoding(false));
            release.VerifyPayload(file);
            File.WriteAllText(file, "abd", new System.Text.UTF8Encoding(false));
            bool rejected = false;
            try { release.VerifyPayload(file); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "tampered download rejected");
            File.WriteAllText(file, "abcd", new System.Text.UTF8Encoding(false));
            rejected = false;
            try { release.VerifyPayload(file); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "wrong download length rejected");
        }

        private static string Fixture(string tag, string draft, string prerelease, string url, string digest, int size)
        {
            return "{\"tag_name\":\"" + tag + "\",\"draft\":" + draft + ",\"prerelease\":" + prerelease +
                ",\"assets\":[{\"name\":\"CodexUsageTray-Setup.exe\",\"state\":\"uploaded\",\"browser_download_url\":\"" +
                url + "\",\"digest\":\"" + digest + "\",\"size\":" + size + "}]}";
        }

        private static void AssertRejected(string json, Version current, string description)
        {
            bool rejected = false;
            try { UpdateRelease.Parse(json, current); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, description);
        }

        private static void Assert(bool condition, string description)
        {
            if (!condition) { throw new InvalidOperationException("Update test failed: " + description); }
        }
    }
}
