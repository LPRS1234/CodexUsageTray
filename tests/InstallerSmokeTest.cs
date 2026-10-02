using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using CodexUsageTray.Setup;

[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]

namespace CodexUsageTray.Tests
{
    internal static class InstallerSmokeTest
    {
        private static string _root;
        private static string _registryRoot;
        private static InstallContext _context;
        private static Dictionary<string, byte[]> _payload;

        [STAThread]
        private static int Main(string[] args)
        {
            if (String.Equals(Path.GetFileName(Assembly.GetExecutingAssembly().Location), "CodexUsageTray.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length == 3 && args[0] == "/fixture-hold")
                {
                    using (Mutex mutex = new Mutex(true, args[2])) Thread.Sleep(Int32.Parse(args[1]));
                }
                else File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture-launched.txt"), "launched");
                return 0;
            }

            _root = Path.Combine(Path.GetTempPath(), "CodexUsageTray.InstallerTest." + Guid.NewGuid().ToString("N"));
            _registryRoot = @"Software\CodexUsageTray.InstallerTest." + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(_root);
                _context = new InstallContext(Path.Combine(_root, "program"), Path.Combine(_root, "menu"),
                    _registryRoot + @"\Uninstall", _registryRoot + @"\Run", "CodexUsageTray", "Local\\CodexUsageTray.InstallerTest." + Guid.NewGuid().ToString("N"),
                    Assembly.GetExecutingAssembly().Location);
                _payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                _payload.Add("CodexUsageTray.exe", File.ReadAllBytes(Assembly.GetExecutingAssembly().Location));
                _payload.Add(@"assets\codex-terminal.png", new byte[] { 1, 2, 3 });
                _payload.Add(@"assets\dashboard.html", System.Text.Encoding.UTF8.GetBytes("old html"));
                _payload.Add(@"assets\dashboard.css", System.Text.Encoding.UTF8.GetBytes("old css"));
                _payload.Add(@"assets\dashboard.js", System.Text.Encoding.UTF8.GetBytes("old js"));

                FreshInstallCreatesRegistrationAndShortcuts();
                StartupCanBeEnabledAndDisabled();
                LegacyStartupMigratesWithUserChoice();
                FailedInstallRestoresPreviousFilesAndStartup();
                RunningApplicationBlocksInstallation();
                UpdateValidatesRegistrationAndProcess();
                UpdateWaitsAndRestartsPreservingStartup();
                FailedUpdateRestartsRestoredApplication();
                AcceptedUpdateFailureRestartsOldApplication();
                UpdateTimeoutLeavesApplicationRunning();
                UpdatePreservesDisabledStartup();
                UninstallRegistryFailureRestoresFilesAndStartup();
                UninstallPreservesAdditionalFilesAndSettings();
                ForeignStartupValueIsPreserved();
                if (args.Length == 2 && args[0] == "/preview") RenderPreview(args[1]);
                Console.WriteLine("Installer smoke tests passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Installer smoke test failed: " + ex.Message);
                return 1;
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(_registryRoot, false);
                if (Directory.Exists(_root) && Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(_root, true);
            }
        }

        private static SetupEngine Engine() { return new SetupEngine(_context, _payload); }

        private static void FreshInstallCreatesRegistrationAndShortcuts()
        {
            SetupEngine engine = Engine();
            using (SetupForm form = new SetupForm(engine)) Assert(!form.StartAutomatically, "A fresh installation must default startup off.");
            engine.Install(false);
            Assert(File.Exists(Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe")), "Application executable was not installed.");
            Assert(File.Exists(Path.Combine(_context.InstallDirectory, @"assets\dashboard.js")), "Dashboard asset was not installed.");
            Assert(File.Exists(Path.Combine(_context.InstallDirectory, "Uninstall.exe")), "Uninstaller was not installed.");
            Assert(!engine.IsStartupEnabled, "Fresh install added a startup entry.");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.UninstallRegistryPath))
            {
                Assert(key != null, "Uninstall registration is missing.");
                Assert(String.Equals((string)key.GetValue("InstallLocation"), _context.InstallDirectory, StringComparison.OrdinalIgnoreCase), "Registration has the wrong install location.");
                Assert((string)key.GetValue("DisplayVersion") == "1.7.0", "Registration does not use the packaged application version.");
                Assert((string)key.GetValue("UninstallString") == "\"" + Path.Combine(_context.InstallDirectory, "Uninstall.exe") + "\" /uninstall", "Uninstall command is not quoted.");
            }
            Assert(ShortcutTarget(Path.Combine(_context.StartMenuDirectory, "Codex Usage Tray.lnk")) == Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe"), "Start menu shortcut targets the wrong application.");
            Assert(ShortcutTarget(Path.Combine(_context.StartMenuDirectory, "제거.lnk")) == Path.Combine(_context.InstallDirectory, "Uninstall.exe"), "Uninstall shortcut targets the wrong executable.");
        }

        private static void StartupCanBeEnabledAndDisabled()
        {
            Engine().Install(true);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.RunRegistryPath))
                Assert((string)key.GetValue(_context.RunValueName) == "\"" + Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe") + "\"", "Startup command is not the quoted installed application.");
            using (SetupForm form = new SetupForm(Engine())) Assert(form.StartAutomatically, "Reinstallation must preserve startup selection.");
            Engine().Install(false);
            Assert(!Engine().IsStartupEnabled, "Disabling startup retained its Run value.");
        }

        private static void FailedInstallRestoresPreviousFilesAndStartup()
        {
            Engine().Install(true);
            Dictionary<string, byte[]> changed = new Dictionary<string, byte[]>(_payload, StringComparer.OrdinalIgnoreCase);
            changed[@"assets\dashboard.css"] = System.Text.Encoding.UTF8.GetBytes("new css");
            changed[@"assets\dashboard.js"] = System.Text.Encoding.UTF8.GetBytes("new js");
            using (FileStream locked = new FileStream(Path.Combine(_context.InstallDirectory, @"assets\dashboard.js"), FileMode.Open, FileAccess.Read, FileShare.Read))
                ExpectFailure(delegate { new SetupEngine(_context, changed).Install(false); }, "Installing over a locked file must fail.");
            Assert(File.ReadAllText(Path.Combine(_context.InstallDirectory, @"assets\dashboard.css")) == "old css", "Failed install did not roll back an earlier replaced asset.");
            Assert(File.ReadAllText(Path.Combine(_context.InstallDirectory, @"assets\dashboard.js")) == "old js", "Failed install damaged the locked asset.");
            Assert(Engine().IsStartupEnabled && Engine().IsRegistered, "Failed install changed prior startup or registration.");
            Assert(ShortcutTarget(Path.Combine(_context.StartMenuDirectory, "Codex Usage Tray.lnk")) == Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe"), "Failed install changed the shortcut.");
        }

        private static void LegacyStartupMigratesWithUserChoice()
        {
            string portableDirectory = Path.Combine(_root, "portable");
            Directory.CreateDirectory(portableDirectory);
            string source = Path.Combine(portableDirectory, "Fixture.cs");
            string executable = Path.Combine(portableDirectory, "CodexUsageTray.exe");
            File.WriteAllText(source, "[assembly: System.Reflection.AssemblyProduct(\"Codex Usage Tray\")] class Fixture { public static void Main() {} }");
            string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            if (!File.Exists(compiler)) compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework\v4.0.30319\csc.exe");
            using (Process process = Process.Start(new ProcessStartInfo(compiler, "/nologo /target:exe /out:\"" + executable + "\" \"" + source + "\"") { UseShellExecute = false, CreateNoWindow = true }))
            {
                process.WaitForExit(); Assert(process.ExitCode == 0, "Unable to compile isolated legacy startup fixture.");
            }
            try
            {
                FileVersionInfo identity = FileVersionInfo.GetVersionInfo(executable);
                Assert(identity.ProductName == "Codex Usage Tray", "Legacy fixture has the wrong product identity.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Legacy fixture metadata inspection failed: " + ex.GetType().Name + " (0x" + ex.HResult.ToString("X8") + ").");
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_context.RunRegistryPath)) key.SetValue(_context.RunValueName, "\"" + executable + "\"");
            using (SetupForm form = new SetupForm(Engine())) Assert(form.StartAutomatically, "Legacy same-app startup must be reflected in the installer.");
            Engine().Install(false);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.RunRegistryPath)) Assert(key.GetValue(_context.RunValueName) == null, "Choosing startup off did not remove legacy same-app startup.");
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_context.RunRegistryPath)) key.SetValue(_context.RunValueName, "\"" + executable + "\"");
            Engine().Install(true);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.RunRegistryPath)) Assert((string)key.GetValue(_context.RunValueName) == "\"" + Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe") + "\"", "Legacy same-app startup did not migrate to installed path.");
        }

        private static void RunningApplicationBlocksInstallation()
        {
            using (Mutex mutex = new Mutex(true, _context.ApplicationMutexName))
            {
                Exception result = null;
                Thread thread = new Thread(delegate() { try { Engine().Install(false); } catch (Exception ex) { result = ex; } });
                thread.Start(); thread.Join();
                Assert(result != null, "Installer ignored the application's running mutex.");
            }
        }

        private static void UpdateValidatesRegistrationAndProcess()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.UninstallRegistryPath, true)) key.SetValue("InstallLocation", Path.Combine(_root, "wrong"));
            ExpectFailure(delegate { Engine().Update(Process.GetCurrentProcess().Id, 100); }, "Update must reject a mismatched registration.");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.UninstallRegistryPath, true)) key.SetValue("InstallLocation", _context.InstallDirectory);
            ExpectFailure(delegate { Engine().Update(Process.GetCurrentProcess().Id, 100); }, "Update must reject a PID outside the installed executable.");
        }

        private static int _exitedApplicationPid;

        private static void UpdateWaitsAndRestartsPreservingStartup()
        {
            string application = Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe");
            using (Process process = Process.Start(new ProcessStartInfo(application, "/fixture-hold 900 \"" + _context.ApplicationMutexName + "\"") { UseShellExecute = false, CreateNoWindow = true }))
            {
                _exitedApplicationPid = process.Id;
                Engine().Update(process.Id, 5000);
                Assert(process.HasExited, "Update replaced files before the old application exited.");
            }
            WaitForLaunchMarker();
            Assert(Engine().IsStartupEnabled, "Update disabled the existing startup choice.");
            File.Delete(Path.Combine(_context.InstallDirectory, "fixture-launched.txt"));
        }

        private static void FailedUpdateRestartsRestoredApplication()
        {
            using (FileStream locked = new FileStream(Path.Combine(_context.InstallDirectory, @"assets\dashboard.js"), FileMode.Open, FileAccess.Read, FileShare.Read))
                ExpectFailure(delegate { Engine().Update(_exitedApplicationPid, 1000); }, "Update over a locked file must fail.");
            WaitForLaunchMarker();
            Assert(Engine().IsStartupEnabled && File.ReadAllText(Path.Combine(_context.InstallDirectory, @"assets\dashboard.css")) == "old css", "Failed update did not preserve the installed application.");
            File.Delete(Path.Combine(_context.InstallDirectory, "fixture-launched.txt"));
        }

        private static void UpdatePreservesDisabledStartup()
        {
            Engine().Install(false);
            Engine().Update(_exitedApplicationPid, 1000);
            WaitForLaunchMarker();
            Assert(!Engine().IsStartupEnabled, "Update enabled previously disabled startup.");
            File.Delete(Path.Combine(_context.InstallDirectory, "fixture-launched.txt"));
            Engine().Install(true);
        }

        private static void AcceptedUpdateFailureRestartsOldApplication()
        {
            SetupEngine engine = Engine();
            using (Process accepted = engine.ValidateUpdateTarget(_exitedApplicationPid))
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.UninstallRegistryPath, true)) key.SetValue("InstallLocation", Path.Combine(_root, "changed-after-acceptance"));
                try
                {
                    ExpectFailure(delegate { engine.Update(accepted, 1000); }, "An accepted update must report a later installation failure.");
                    WaitForLaunchMarker();
                    Assert(File.ReadAllText(Path.Combine(_context.InstallDirectory, @"assets\dashboard.css")) == "old css", "Accepted failure changed the old installation.");
                }
                finally
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.UninstallRegistryPath, true)) key.SetValue("InstallLocation", _context.InstallDirectory);
                }
                File.Delete(Path.Combine(_context.InstallDirectory, "fixture-launched.txt"));
            }
        }

        private static void UpdateTimeoutLeavesApplicationRunning()
        {
            string application = Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe");
            using (Process process = Process.Start(new ProcessStartInfo(application, "/fixture-hold 1500 \"" + _context.ApplicationMutexName + "\"") { UseShellExecute = false, CreateNoWindow = true }))
            {
                ExpectFailure(delegate { Engine().Update(process.Id, 50); }, "Update must time out while the old application is still running.");
                Assert(!process.HasExited, "Update terminated the old application on timeout.");
                Assert(!File.Exists(Path.Combine(_context.InstallDirectory, "fixture-launched.txt")), "Update launched a duplicate application after timeout.");
                process.WaitForExit();
            }
        }

        private static void UninstallRegistryFailureRestoresFilesAndStartup()
        {
            using (RegistryKey child = Registry.CurrentUser.CreateSubKey(_context.UninstallRegistryPath + @"\Preserve")) child.SetValue("Test", 1);
            ExpectFailure(delegate { Engine().Uninstall(); }, "Uninstall must fail safely when registration has a child key.");
            Assert(File.Exists(Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe")), "Registry failure did not restore removed application.");
            Assert(Engine().IsRegistered && Engine().IsStartupEnabled, "Registry failure did not restore registration and startup.");
            Assert(ShortcutTarget(Path.Combine(_context.StartMenuDirectory, "Codex Usage Tray.lnk")) == Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe"), "Registry failure did not restore shortcut.");
            Registry.CurrentUser.DeleteSubKey(_context.UninstallRegistryPath + @"\Preserve", false);
        }

        private static void UninstallPreservesAdditionalFilesAndSettings()
        {
            File.WriteAllText(Path.Combine(_context.InstallDirectory, "user-note.txt"), "keep");
            Directory.CreateDirectory(Path.Combine(_context.InstallDirectory, "custom"));
            File.WriteAllText(Path.Combine(_context.InstallDirectory, "custom", "settings.json"), "keep settings");
            File.WriteAllText(Path.Combine(_root, "authentication.json"), "keep auth");
            using (FileStream locked = new FileStream(Path.Combine(_context.InstallDirectory, @"assets\dashboard.js"), FileMode.Open, FileAccess.Read, FileShare.Read))
                ExpectFailure(delegate { Engine().Uninstall(); }, "Uninstall over a locked file must fail safely.");
            Assert(File.Exists(Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe")) && Engine().IsRegistered, "Failed uninstall lost the application or registration.");
            Engine().Uninstall();
            Assert(!File.Exists(Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe")), "Uninstall retained the application.");
            Assert(!File.Exists(Path.Combine(_context.InstallDirectory, @"assets\dashboard.js")), "Uninstall retained an owned asset.");
            Assert(!File.Exists(Path.Combine(_context.StartMenuDirectory, "Codex Usage Tray.lnk")), "Uninstall retained a shortcut.");
            Assert(!Engine().IsRegistered && !Engine().IsStartupEnabled, "Uninstall retained registration or owned startup.");
            Assert(File.ReadAllText(Path.Combine(_context.InstallDirectory, "user-note.txt")) == "keep", "Uninstall removed a user's extra file.");
            Assert(File.ReadAllText(Path.Combine(_context.InstallDirectory, "custom", "settings.json")) == "keep settings", "Uninstall removed extra settings.");
            Assert(File.ReadAllText(Path.Combine(_root, "authentication.json")) == "keep auth", "Uninstall changed authentication outside the installation.");
        }

        private static void ForeignStartupValueIsPreserved()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_context.RunRegistryPath)) key.SetValue(_context.RunValueName, "other-application.exe");
            Engine().Install(false);
            ExpectFailure(delegate { Engine().Install(true); }, "Install must not overwrite a foreign startup value.");
            Engine().Uninstall();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.RunRegistryPath)) Assert((string)key.GetValue(_context.RunValueName) == "other-application.exe", "Uninstall removed a foreign startup value.");
        }

        private static void WaitForLaunchMarker()
        {
            string marker = Path.Combine(_context.InstallDirectory, "fixture-launched.txt");
            for (int i = 0; i < 50 && !File.Exists(marker); i++) Thread.Sleep(100);
            Assert(File.Exists(marker), "Update did not restart the installed application.");
            Thread.Sleep(200);
        }

        private static string ShortcutTarget(string path)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object shortcut = null;
            try
            {
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                return (string)shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void RenderPreview(string path)
        {
            using (SetupForm form = new SetupForm(Engine()))
            using (System.Drawing.Bitmap image = new System.Drawing.Bitmap(form.Width, form.Height))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.Show(); Application.DoEvents();
                form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                form.Hide();
                image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void ExpectFailure(Action action, string message)
        {
            bool failed = false;
            try { action(); } catch (Exception) { failed = true; }
            Assert(failed, message);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
