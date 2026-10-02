using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace CodexUsageTray
{
    internal sealed class UpdateRelease
    {
        public const string Repository = "LPRS1234/CodexUsageTray";
        public const string SetupFileName = "CodexUsageTray-Setup.exe";
        public const long MaximumDownloadSize = 100 * 1024 * 1024;

        public Version Version { get; private set; }
        public Uri DownloadUrl { get; private set; }
        public long Size { get; private set; }
        private string _sha256;

        public static UpdateRelease Parse(string json, Version currentVersion)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
            Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            object draft;
            object prerelease;
            if (root == null || !root.TryGetValue("draft", out draft) || !(draft is bool) || (bool)draft ||
                !root.TryGetValue("prerelease", out prerelease) || !(prerelease is bool) || (bool)prerelease)
            {
                return null;
            }

            string tag = JsonValueReader.GetString(root, "tag_name");
            Version version;
            if (string.IsNullOrEmpty(tag) || !System.Version.TryParse(tag.TrimStart('v', 'V'), out version) ||
                NormalizeVersion(version) <= NormalizeVersion(currentVersion))
            {
                return null;
            }

            object assetsValue;
            IEnumerable assets = root.TryGetValue("assets", out assetsValue) ? assetsValue as IEnumerable : null;
            if (assets != null)
            {
                foreach (object value in assets)
                {
                    Dictionary<string, object> asset = value as Dictionary<string, object>;
                    if (JsonValueReader.GetString(asset, "name") != SetupFileName)
                    {
                        continue;
                    }

                    Uri url;
                    string digest = JsonValueReader.GetString(asset, "digest");
                    long? size = JsonValueReader.GetLong(asset, "size");
                    string expectedPath = "/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + SetupFileName;
                    if (!Uri.TryCreate(JsonValueReader.GetString(asset, "browser_download_url"), UriKind.Absolute, out url) ||
                        url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com" || !url.IsDefaultPort ||
                        url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
                        url.AbsolutePath != expectedPath || JsonValueReader.GetString(asset, "state") != "uploaded" ||
                        !size.HasValue || size.Value <= 0 || size.Value > MaximumDownloadSize ||
                        string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                        digest.Length != 71 || !IsHex(digest.Substring(7)))
                    {
                        throw new InvalidDataException("업데이트 설치 파일의 배포 정보가 올바르지 않습니다.");
                    }

                    return new UpdateRelease
                    {
                        Version = NormalizeVersion(version),
                        DownloadUrl = url,
                        Size = size.Value,
                        _sha256 = digest.Substring(7)
                    };
                }
            }

            throw new InvalidDataException("새 릴리스에 검증 가능한 설치 파일이 없습니다.");
        }

        public void VerifyPayload(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
                if (stream.Length != Size || !string.Equals(hash, _sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("업데이트 설치 파일의 무결성 검증에 실패했습니다.");
                }
            }
        }

        public static Version NormalizeVersion(Version version)
        {
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        private static bool IsHex(string value)
        {
            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character)) { return false; }
            }
            return true;
        }
    }
}
