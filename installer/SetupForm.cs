using System;
using System.Drawing;
using System.Windows.Forms;
namespace CodexUsageTray.Setup
{
    internal sealed class SetupForm : Form
    {
        private readonly SetupEngine _engine;
        private readonly CheckBox _startup, _launch;
        private readonly Button _install, _cancel;
        private readonly Label _status;
        internal SetupForm(SetupEngine engine)
        {
            _engine = engine;
            Text = "Codex Usage Tray 설치";
            Font = new Font("맑은 고딕", 10F);
            ClientSize = new Size(600, 438);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(249, 250, 252);
            Label title = new Label { Text = "Codex Usage Tray", Font = new Font("맑은 고딕", 21F, FontStyle.Bold), Location = new Point(28, 24), Size = new Size(540, 46) };
            Label summary = new Label { Text = "Codex 사용량을 Windows 트레이에서 확인하세요.", Location = new Point(30, 78), Size = new Size(540, 30) };
            Label requirements = new Label { Text = "사용하려면 이 PC에 Codex CLI가 설치되어 있고 로그인되어 있어야 합니다.\r\n.NET Framework 4.8이 필요합니다. 관리자 권한은 필요하지 않습니다.", Location = new Point(30, 118), Size = new Size(540, 54), ForeColor = Color.FromArgb(66, 76, 90) };
            Label updates = new Label { Text = "새 버전은 자동으로 업데이트됩니다. 앱 메뉴에서 즉시 확인할 수 있습니다.", Location = new Point(30, 178), Size = new Size(540, 26), ForeColor = Color.FromArgb(66, 76, 90), Font = new Font("맑은 고딕", 9F) };
            _startup = new CheckBox { Text = "Windows 로그인 시 자동 실행", Location = new Point(30, 218), Size = new Size(480, 28), Checked = engine.IsStartupEnabled };
            _launch = new CheckBox { Text = "설치 후 Codex Usage Tray 실행", Location = new Point(30, 254), Size = new Size(480, 28), Checked = true };
            Label location = new Label { Text = "설치 위치: " + engine.InstallDirectory, Location = new Point(30, 296), Size = new Size(540, 42), AutoEllipsis = true, ForeColor = Color.FromArgb(90, 99, 110), Font = new Font("맑은 고딕", 9F) };
            _status = new Label { Location = new Point(30, 340), Size = new Size(540, 36), ForeColor = Color.FromArgb(45, 90, 55) };
            _install = new Button { Text = "설치", Location = new Point(362, 386), Size = new Size(100, 34), BackColor = Color.FromArgb(31, 89, 155), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            _cancel = new Button { Text = "취소", Location = new Point(474, 386), Size = new Size(96, 34), DialogResult = DialogResult.Cancel };
            _install.Click += InstallClicked;
            Controls.AddRange(new Control[] { title, summary, requirements, updates, _startup, _launch, location, _status, _install, _cancel });
            AcceptButton = _install; CancelButton = _cancel;
        }
        internal bool StartAutomatically { get { return _startup.Checked; } }
        private void InstallClicked(object sender, EventArgs e)
        {
            _install.Enabled = false; _cancel.Enabled = false; _startup.Enabled = false; _launch.Enabled = false;
            _status.Text = "설치 중..."; _status.Refresh();
            try
            {
                _engine.Install(_startup.Checked);
                if (_launch.Checked)
                {
                    try { _engine.LaunchApplication(); }
                    catch (Exception) { MessageBox.Show(this, "설치가 완료되었지만 앱을 실행하지 못했습니다. 시작 메뉴에서 실행해 주세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex)
            {
                _status.Text = "설치를 완료하지 못했습니다.";
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _install.Enabled = true; _cancel.Enabled = true; _startup.Enabled = true; _launch.Enabled = true;
            }
        }
    }
}
