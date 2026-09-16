using System;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Codex Usage Tray")]
[assembly: System.Reflection.AssemblyDescription("Codex remaining usage indicator for the Windows notification area")]
[assembly: System.Reflection.AssemblyCompany("Local")]
[assembly: System.Reflection.AssemblyProduct("Codex Usage Tray")]
[assembly: System.Reflection.AssemblyVersion("1.6.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.6.0.0")]

namespace CodexUsageTray
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, @"Local\CodexUsageTray", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Codex 사용량 표시기가 이미 실행 중입니다.", "Codex 사용량",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DpiAwareness.Enable();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new TrayApplicationContext());
            }
            finally
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
        }
    }
}
