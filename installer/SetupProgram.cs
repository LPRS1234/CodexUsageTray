using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexUsageTray.Setup
{
    internal static class SetupProgram
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            bool update = args.Length > 0 && String.Equals(args[0], "/update", StringComparison.OrdinalIgnoreCase);
            try
            {
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                RequireFramework48();
                InstallContext context = InstallContext.ForCurrentUser();
                bool worker = args.Length > 0 && String.Equals(args[0], "/uninstall-worker", StringComparison.OrdinalIgnoreCase);
                if (worker) WaitForUninstaller(args, context);
                using (ApplicationLock setup = new ApplicationLock(@"Local\CodexUsageTray.Setup"))
                {
                    if (update)
                    {
                        int pid;
                        if ((args.Length != 2 && args.Length != 3) || !Int32.TryParse(args[1], out pid) || pid <= 0) return 1;
                        SetupEngine engine = new SetupEngine(context, ReadPayload());
                        using (Process application = engine.ValidateUpdateTarget(pid))
                        {
                            if (args.Length == 3)
                            {
                                Guid eventId;
                                const string prefix = @"Local\CodexUsageTray.UpdateReady.";
                                if (!args[2].StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParse(args[2].Substring(prefix.Length), out eventId)) return 1;
                                using (EventWaitHandle ready = EventWaitHandle.OpenExisting(args[2]))
                                {
                                    if (!ready.Set()) return 1;
                                    engine.Update(application, 60000);
                                }
                            }
                            else engine.Update(application, 60000);
                        }
                        return 0;
                    }
                    bool uninstall = worker || (args.Length == 1 && String.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase))
                        || (args.Length == 0 && String.Equals(Path.GetFileName(context.SetupFilePath), "Uninstall.exe", StringComparison.OrdinalIgnoreCase));
                    if (uninstall)
                    {
                        SetupEngine engine = new SetupEngine(context, new Dictionary<string, byte[]>());
                        if (!engine.IsRegistered) throw new InvalidOperationException("현재 사용자에게 등록된 설치를 찾을 수 없습니다.");
                        if (!worker)
                        {
                            if (MessageBox.Show("Codex Usage Tray를 제거하시겠습니까?\r\n사용자 설정과 Codex 로그인 정보는 유지됩니다.", "Codex Usage Tray 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 2;
                            if (String.Equals(context.SetupFilePath, Path.Combine(context.InstallDirectory, "Uninstall.exe"), StringComparison.OrdinalIgnoreCase))
                            {
                                StartUninstallWorker(context);
                                return 0;
                            }
                        }
                        engine.Uninstall();
                        MessageBox.Show("Codex Usage Tray를 제거했습니다.", "Codex Usage Tray 제거", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 0;
                    }
                    if (args.Length != 0) throw new ArgumentException("설치 프로그램의 실행 옵션이 올바르지 않습니다.");
                    using (SetupForm form = new SetupForm(new SetupEngine(context, ReadPayload())))
                    {
                        Application.Run(form);
                        return form.DialogResult == DialogResult.OK ? 0 : 2;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!update) MessageBox.Show(ex.Message, "Codex Usage Tray 설치", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static Dictionary<string, byte[]> ReadPayload()
        {
            Dictionary<string, byte[]> payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (string relative in SetupEngine.PayloadFiles)
            {
                using (Stream resource = assembly.GetManifestResourceStream("Payload." + relative.Replace('\\', '.')))
                {
                    if (resource == null) throw new InvalidOperationException("설치 파일에 필요한 구성 요소가 없습니다.");
                    using (MemoryStream bytes = new MemoryStream())
                    {
                        resource.CopyTo(bytes);
                        payload.Add(relative, bytes.ToArray());
                    }
                }
            }
            return payload;
        }

        private static void RequireFramework48()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                object release = key == null ? null : key.GetValue("Release");
                if (!(release is int) || (int)release < 528040) throw new InvalidOperationException(".NET Framework 4.8 이상을 설치한 뒤 다시 실행해 주세요.");
            }
        }

        private static void StartUninstallWorker(InstallContext context)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "CodexUsageTray.UninstallWorker." + Guid.NewGuid().ToString("N") + ".exe");
            PathSafety.Check(temporary);
            File.Copy(context.SetupFilePath, temporary);
            try
            {
                Process.Start(new ProcessStartInfo(temporary, "/uninstall-worker " + Process.GetCurrentProcess().Id)
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetTempPath() });
            }
            catch { File.Delete(temporary); throw; }
            // Windows locks the running image. Leave this unique copy in the OS temp folder.
        }

        private static void WaitForUninstaller(string[] args, InstallContext context)
        {
            int pid;
            if (args.Length != 2 || !Int32.TryParse(args[1], out pid) || pid <= 0) throw new ArgumentException("제거 작업의 프로세스가 올바르지 않습니다.");
            string fileName = Path.GetFileName(context.SetupFilePath);
            const string prefix = "CodexUsageTray.UninstallWorker.";
            Guid workerId;
            if (!String.Equals(Path.GetDirectoryName(context.SetupFilePath), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || !fileName.StartsWith(prefix, StringComparison.Ordinal) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(fileName.Substring(prefix.Length, fileName.Length - prefix.Length - 4), "N", out workerId))
                throw new InvalidOperationException("임시 제거 작업의 경로가 올바르지 않습니다.");
            PathSafety.Check(context.SetupFilePath);
            Process parent;
            try { parent = Process.GetProcessById(pid); } catch (ArgumentException) { return; }
            using (parent)
            {
                if (parent.HasExited) return;
                string path;
                try { path = parent.MainModule.FileName; }
                catch (InvalidOperationException) { if (parent.HasExited) return; throw; }
                if (!String.Equals(path, Path.Combine(context.InstallDirectory, "Uninstall.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("제거 프로그램의 프로세스가 일치하지 않습니다.");
                if (!parent.WaitForExit(60000)) throw new InvalidOperationException("제거 프로그램이 종료되지 않았습니다. 잠시 후 다시 시도해 주세요.");
            }
        }
    }
}
