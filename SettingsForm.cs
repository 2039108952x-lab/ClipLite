using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClipLite
{
    public class SettingsForm : Form
    {
        private ClipLiteSettings _settings;
        private Action<ClipLiteSettings> _onSave;

        private CheckBox _chkFileDetails;
        private RadioButton _rdoFull, _rdoTextOnly;
        private CheckBox _chkAutoStart;
        private TextBox _txtHotkey;
        private CheckBox _chkCopyToast;
        private uint _capturedModifiers = 6;
        private uint _capturedKey = 0x56;
        private ListBox _lstExcluded;
        private Button _btnAdd, _btnRemove;
        private TextBox _txtNewApp;
        private CheckBox _chkEncryption;
        private Label _lblStorageInfo;
        private Button _btnSave, _btnCancel, _btnClear;

        public SettingsForm(ClipLiteSettings settings, Action<ClipLiteSettings> onSave, SafeStorage storage)
        {
            _settings = settings;
            _onSave = onSave;
            InitializeComponent();
            LoadSettings();
            UpdateStorageInfo(storage);
        }

        private void InitializeComponent()
        {
            this.Text = "ClipLite 设置";
            this.Size = new Size(380, 580);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.Font = new Font("Segoe UI", 9);
            this.BackColor = Color.FromArgb(250, 250, 250);

            int y = 16;

            // ── Audio section ──
            _chkFileDetails = new CheckBox
            {
                Text = "复制文件时显示文件名称和格式",
                Location = new Point(30, y),
                AutoSize = true
            };
            this.Controls.Add(_chkFileDetails);

                        y += 22;
            var lblMode = new Label { Text = "抓取模式",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(16, y), AutoSize = true };
            this.Controls.Add(lblMode);

            y += 22;
            _rdoFull = new RadioButton
            {
                Text = "全功能模式（存储所有格式）",
                Location = new Point(30, y),
                AutoSize = true
            };
            this.Controls.Add(_rdoFull);

            y += 22;
            _rdoTextOnly = new RadioButton
            {
                Text = "纯文本模式（仅存储文字）",
                Location = new Point(30, y),
                AutoSize = true
            };
            this.Controls.Add(_rdoTextOnly);

            y += 22;
            _chkAutoStart = new CheckBox
            {
                Text = "开机自启动",
                Location = new Point(30, y),
                AutoSize = true
            };
            this.Controls.Add(_chkAutoStart);

            y += 22;

            var lblHotkey = new Label { Text = "快捷键（点击文本框后按下快捷键）",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(16, y), AutoSize = true };
            this.Controls.Add(lblHotkey);

            y += 22;
            _txtHotkey = new TextBox
            {
                Location = new Point(30, y),
                Width = 200,
                Height = 24,
                Font = new Font("Segoe UI", 9),
                Text = GetHotkeyText(_capturedModifiers, _capturedKey)
            };
            _txtHotkey.Enter += (s, e) => _txtHotkey.SelectAll();
            _txtHotkey.KeyDown += OnHotkeyKeyDown;
            this.Controls.Add(_txtHotkey);

            y += 30;
            _chkCopyToast = new CheckBox
            {
                Text = "复制成功时显示提示",
                Checked = true,
                Location = new Point(30, y),
                AutoSize = true
            };
            this.Controls.Add(_chkCopyToast);

            y += 26;

            // ── Excluded apps section ──
            var lblExclude = new Label { Text = "排除程序",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(16, y), AutoSize = true };
            this.Controls.Add(lblExclude);

            y += 22;
            _txtNewApp = new TextBox
            {
                Location = new Point(30, y),
                Width = 200,
                Height = 24,
                Font = new Font("Segoe UI", 9)
            };
            this.Controls.Add(_txtNewApp);

            _btnAdd = new Button
            {
                Text = "添加",
                Location = new Point(240, y - 1),
                Width = 60,
                Height = 26,
                UseVisualStyleBackColor = true
            };
            _btnAdd.Click += (s, e) => AddExcludedApp();
            this.Controls.Add(_btnAdd);

            y += 30;
            _lstExcluded = new ListBox
            {
                Location = new Point(30, y),
                Width = 270,
                Height = 120,
                Font = new Font("Segoe UI", 9),
                IntegralHeight = false
            };
            this.Controls.Add(_lstExcluded);

            _btnRemove = new Button
            {
                Text = "移除",
                Location = new Point(310, y),
                Width = 50,
                Height = 26,
                UseVisualStyleBackColor = true
            };
            _btnRemove.Click += (s, e) => RemoveExcludedApp();
            this.Controls.Add(_btnRemove);

            y += 130;

            // ── Encryption section ──
            var lblEncrypt = new Label { Text = "加密存储",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(16, y), AutoSize = true };
            this.Controls.Add(lblEncrypt);

            y += 22;
            _chkEncryption = new CheckBox
            {
                Text = "加密历史数据（AES-256，自动密钥）",
                Location = new Point(30, y),
                AutoSize = true
            };
            this.Controls.Add(_chkEncryption);

            y += 30;

            // ── Storage info ──
            _lblStorageInfo = new Label
            {
                Location = new Point(16, y),
                Width = 340,
                Height = 60,
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(100, 100, 100)
            };
            this.Controls.Add(_lblStorageInfo);

            y += 70;

            // ── Clear history ──
            _btnClear = new Button
            {
                Text = "清空全部历史",
                Location = new Point(16, y),
                Width = 140,
                Height = 28,
                UseVisualStyleBackColor = true,
                ForeColor = Color.FromArgb(180, 40, 40)
            };
            _btnClear.Click += (s, e) => OnClearHistory();
            this.Controls.Add(_btnClear);

            y += 44;

            // ── Save / Cancel ──
            _btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(this.Width - 180, this.Height - 60),
                Width = 70,
                Height = 28,
                UseVisualStyleBackColor = true,
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(_btnCancel);

            _btnSave = new Button
            {
                Text = "保存",
                Location = new Point(this.Width - 100, this.Height - 60),
                Width = 70,
                Height = 28,
                UseVisualStyleBackColor = true
            };
            _btnSave.Click += (s, e) => SaveAndClose();
            this.Controls.Add(_btnSave);

            this.CancelButton = _btnCancel;
            this.AcceptButton = _btnSave;
        }

        private void LoadSettings()
        {
            _chkFileDetails.Checked = _settings.ShowFileDetails;
            _rdoFull.Checked = (_settings.CaptureMode == "full");
            _rdoTextOnly.Checked = (_settings.CaptureMode != "full");
            _chkAutoStart.Checked = _settings.AutoStart;
            _capturedModifiers = _settings.HotkeyModifiers;
            _capturedKey = _settings.HotkeyKey;
            _txtHotkey.Text = GetHotkeyText(_capturedModifiers, _capturedKey);
            _chkCopyToast.Checked = _settings.ShowCopyToast;
            _chkEncryption.Checked = _settings.EnableEncryption;
            _lstExcluded.Items.Clear();
            foreach (var app in _settings.ExcludedApps)
                _lstExcluded.Items.Add(app);
        }

        private void SaveAndClose()
        {
            _settings.ShowFileDetails = _chkFileDetails.Checked;
            _settings.CaptureMode = _rdoFull.Checked ? "full" : "text_only";
            _settings.AutoStart = _chkAutoStart.Checked;
            _settings.HotkeyModifiers = _capturedModifiers;
            _settings.HotkeyKey = _capturedKey;
            _settings.ShowCopyToast = _chkCopyToast.Checked;
            _settings.EnableEncryption = _chkEncryption.Checked;
            _settings.ExcludedApps.Clear();
            foreach (string item in _lstExcluded.Items)
                _settings.ExcludedApps.Add(item);

            if (_settings.EnableEncryption)
                _settings.EnsureEncryptionKey();
            else
                _settings.EncryptionKey = "";

            if (_onSave != null)
                _onSave(_settings);

            _settings.Save();
            this.Close();
        }

        private void AddExcludedApp()
        {
            string name = _txtNewApp.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (!name.EndsWith(".exe")) name += ".exe";
            if (!_lstExcluded.Items.Contains(name))
            {
                _lstExcluded.Items.Add(name);
                _txtNewApp.Clear();
            }
        }

        private void RemoveExcludedApp()
        {
            if (_lstExcluded.SelectedIndex >= 0)
                _lstExcluded.Items.RemoveAt(_lstExcluded.SelectedIndex);
        }

        private void UpdateStorageInfo(SafeStorage storage)
        {
            long totalSize = (storage != null) ? storage.GetTotalSize() : 0;
            long maxSize = (_settings != null) ? _settings.MaxTotalSize : 50L * 1024 * 1024;

            string sizeStr = FormatBytes(totalSize);
            string maxStr = FormatBytes(maxSize);
            int entryCount = 0;
            string indexPath = (storage != null)
                ? Path.Combine(storage.DataDir, "index.jsonl") : "";
            if (File.Exists(indexPath))
            {
                try
                {
                    string content = File.ReadAllText(indexPath);
                    entryCount = content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                }
                catch { }
            }

            _lblStorageInfo.Text = "存储：" + sizeStr + " / " + maxStr + "\n"
                + "记录条数：" + entryCount + "\n"
                + "Location: cliplite_data/";
        }


        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back)
            {
                _capturedModifiers = 0;
                _capturedKey = 0;
                _txtHotkey.Text = "无";
                _txtHotkey.SelectAll();
                return;
            }
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey ||
                e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                return;
            uint mod = 0;
            if (e.Control) mod |= 2;
            if (e.Shift) mod |= 4;
            if (e.Alt) mod |= 1;
            if (mod == 0) return;
            _capturedModifiers = mod;
            _capturedKey = (uint)e.KeyCode;
            _txtHotkey.Text = GetHotkeyText(mod, (uint)e.KeyCode);
            _txtHotkey.SelectAll();
        }

        private static string GetHotkeyText(uint mod, uint key)
        {
            if (mod == 0 || key == 0) return "无";
            string text = "";
            if ((mod & 1) != 0) text += "Alt+";
            if ((mod & 2) != 0) text += "Ctrl+";
            if ((mod & 4) != 0) text += "Shift+";
            text += ((Keys)key).ToString();
            return text;
        }
        private void OnClearHistory()
        {
            if (MessageBox.Show("Clear all clipboard history? This cannot be undone.",
                "ClipLite", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                if (_onSave != null)
                {
                    var cleared = new ClipLiteSettings
                    {
                        ExcludedApps = _settings.ExcludedApps,
                        EnableEncryption = _settings.EnableEncryption,
                        ShowCopyToast = _settings.ShowCopyToast,
                        CaptureMode = _settings.CaptureMode,
                        HotkeyModifiers = _settings.HotkeyModifiers,
                        HotkeyKey = _settings.HotkeyKey,
                        AutoStart = _settings.AutoStart,
                        EncryptionKey = _settings.EncryptionKey,
                        MaxEntries = _settings.MaxEntries,
                        MaxTotalSize = _settings.MaxTotalSize
                    };
                    _onSave(cleared);
                }
                // Signal caller to clear history
                this.Tag = "clear";
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return Math.Round(bytes / 1024.0, 1) + " KB";
            return Math.Round(bytes / (1024.0 * 1024.0), 1) + " MB";
        }
    }
}



