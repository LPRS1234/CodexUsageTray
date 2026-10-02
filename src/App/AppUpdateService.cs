using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CodexUsageTray
{
    internal sealed class AppUpdateService
    {
        private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexUsageTray";
        private const string LatestReleaseUrl = "https://api.github.com/repos/" + UpdateRelease.Repository + "/releases/latest";

        public static bool IsInstalled(string applicationDirectory)
        {
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexUsageTray");
            if (!string.Equals(Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar),
                expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(expected, "Uninstall.exe")))
            {
                return false;
            }

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallRegistryPath))
            {
                return key != null && string.Equals(key.GetValue("InstallLocation") as string,
                    expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        public Task<UpdateRelease> CheckAsync(Version currentVersion)
        {
            return Task.Run(() =>
            {
                try
                {
                    HttpWebRequest request = CreateRequest(new Uri(LatestReleaseUrl));
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (MemoryStream data = new MemoryStream())
                    {
                        CopyLimited(stream, data, 1024 * 1024);
                        return UpdateRelease.Parse(Encoding.UTF8.GetString(data.ToArray()), currentVersion);
                    }
                }
                catch (WebException exception)
                {
                    HttpWebResponse response = exception.Response as HttpWebResponse;
                    if (response != null)
                    {
                        using (response)
                        {
                            if (response.StatusCode == HttpStatusCode.NotFound) { return null; }
                        }
                    }
                    throw;
                }
            });
        }

        public Task<string> DownloadAsync(UpdateRelease release)
        {
            return Task.Run(() =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "CodexUsageTray.Update." + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, UpdateRelease.SetupFileName);
                try
                {
                    HttpWebRequest request = CreateRequest(release.DownloadUrl);
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    {
                        CopyLimited(stream, file, release.Size);
                    }
                    release.VerifyPayload(path);
                    Version setupVersion;
                    if (!Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out setupVersion) ||
                        UpdateRelease.NormalizeVersion(setupVersion) != release.Version)
                    {
                        throw new InvalidDataException("업데이트 설치 파일의 버전이 배포 정보와 일치하지 않습니다.");
                    }
                    return path;
                }
                catch
                {
                    try { File.Delete(path); Directory.Delete(directory); } catch { }
                    throw;
                }
            });
        }

        private static HttpWebRequest CreateRequest(Uri url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "CodexUsageTray/" + typeof(Program).Assembly.GetName().Version.ToString(3);
            request.Accept = "application/vnd.github+json";
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.MaximumAutomaticRedirections = 5;
            return request;
        }

        private static void CopyLimited(Stream source, Stream destination, long limit)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += count;
                if (total > limit) { throw new InvalidDataException("업데이트 응답의 크기가 제한을 초과했습니다."); }
                destination.Write(buffer, 0, count);
            }
        }
    }
}
