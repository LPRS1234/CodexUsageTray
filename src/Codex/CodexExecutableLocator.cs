using System;
using System.IO;

namespace CodexUsageTray
{
    internal static class CodexExecutableLocator
    {
        public static string Find()
        {
            foreach (string candidate in GetKnownPaths())
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                string candidate = BuildPathCandidate(directory);
                if (candidate != null && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("Codex CLI 실행 파일을 찾을 수 없습니다.");
        }

        private static string[] GetKnownPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new[]
            {
                Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe"),
                Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "node_modules",
                    "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe")
            };
        }

        private static string BuildPathCandidate(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            try
            {
                return Path.Combine(directory.Trim().Trim('"'), "codex.exe");
            }
            catch
            {
                return null;
            }
        }
    }
}
