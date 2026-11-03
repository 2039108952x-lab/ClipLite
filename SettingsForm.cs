using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ClipLite
{
    public class SettingsForm : Form
    {
        private ClipLiteSettings _settings;
        private Action<ClipLiteSettings> _onSave;

        private Panel _titleBar;
        private Panel _footerLine;
        private Label _closeLabel;
        private CheckBox _chkFileDetails;
        private RadioButton _rdoFull, _rdoTextOnly;
        private CheckBox _chkAutoStart;
        private TextBox _hotkeyBox;
        private CheckBox _chkCopyToast;
        private ComboBox _cboToastPosition;
        private TextBox _txtNewApp;
        private ListBox _lstExcluded;
        private Button _btnAdd, _btnRemove;
        private CheckBox _chkEncryption;
        private Label _lblStorageValue, _lblEntryValue, _lblLocation;
        private Button _btnSave, _btnCancel, _btnClear;

        private int _hotkeyModifiers;
        private int _hotkeyKey;
        private bool _hotkeyCapturing;
        private bool _loadingSettings;

        /// <summary>Raised when the toast position is changed; the choice is saved immediately.</summary>
        public event Action<string> ToastPositionChanged;

        private bool _dragging;
        private Point _dragOffset;

        public SettingsForm(ClipLiteSettings settings, Action<ClipLiteSettings> onSave,
            SafeStorage storage, int entryCount)
        {
            _settings = settings;
            _onSave = onSave;
            InitializeComponent();
            LoadSettings();
            UpdateStorageInfo(storage, entryCount);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UiTheme.RoundControl(this, 12);
            if (_titleBar != null)
                _titleBar.Width = ClientSize.Width;
            if (_closeLabel != null)
                _closeLabel.Left = Math.Max(8, ClientSize.Width - 42);
            if (_footerLine != null)
                _footerLine.Width = ClientSize.Width;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiTheme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (var pen = new Pen(UiTheme.Border))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Esc inside the hotkey box cancels the capture instead of the whole dialog.
            if (keyData == Keys.Escape && _hotkeyBox != null && _hotkeyBox.Focused)
            {
                UpdateHotkeyText();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void InitializeComponent()
        {
            this.Text = "ClipLite 设置";
            this.Size = new Size(430, 690);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.BackColor = UiTheme.Surface;
            this.Font = UiTheme.CreateFont(9F, FontStyle.Regular);
            this.AutoScroll = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);

            BuildHeader();

            int left = 24;
            int width = 382;
            int y = 58;

            _chkFileDetails = new CheckBox
            {
                Text = "复制文件时显示文件名称和格式",
                Location = new Point(left, y),
                AutoSize = true,
                ForeColor = UiTheme.TextPrimary
            };
            this.Controls.Add(_chkFileDetails);

            y += 30;
            AddSection("抓取模式", left, y);
            y += 22;
            _rdoFull = new RadioButton
            {
                Text = "全功能模式（存储所有格式）",
                Location = new Point(left + 10, y),
                AutoSize = true,
                ForeColor = UiTheme.TextPrimary
            };
            this.Controls.Add(_rdoFull);

            y += 24;
            _rdoTextOnly = new RadioButton
            {
                Text = "纯文本模式（仅存储文字）",
                Location = new Point(left + 10, y),
                AutoSize = true,
                ForeColor = UiTheme.TextPrimary
            };
            this.Controls.Add(_rdoTextOnly);

            y += 28;
            _chkAutoStart = new CheckBox
            {
                Text = "开机自启动",
                Location = new Point(left, y),
                AutoSize = true,
                ForeColor = UiTheme.TextPrimary
            };
            this.Controls.Add(_chkAutoStart);

            y += 30;
            AddSection("全局快捷键", left, y);
            y += 22;
            _hotkeyBox = new TextBox
            {
                Location = new Point(left, y),
                Width = width,
                Height = 28,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true,
                ShortcutsEnabled = false,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary,
                Font = UiTheme.CreateFont(9.5F, FontStyle.Regular)
            };
            _hotkeyBox.Enter += OnHotkeyEnter;
            _hotkeyBox.Leave += OnHotkeyLeave;
            _hotkeyBox.KeyDown += OnHotkeyKeyDown;
            this.Controls.Add(_hotkeyBox);

            y += 33;
            var hotkeyHint = new Label
            {
                Text = "点击后按下组合键；Backspace 清除，Esc 取消",
                Location = new Point(left, y),
                AutoSize = true,
                Font = UiTheme.CreateFont(8F, FontStyle.Regular),
                ForeColor = UiTheme.TextMuted
            };
            this.Controls.Add(hotkeyHint);

            y += 24;
            _chkCopyToast = new CheckBox
            {
                Text = "复制成功时显示提示",
                Location = new Point(left, y),
                AutoSize = true,
                ForeColor = UiTheme.TextPrimary
            };
            this.Controls.Add(_chkCopyToast);

            var positionLabel = new Label
            {
                Text = "位置",
                Location = new Point(left + 166, y + 3),
                AutoSize = true,
                Font = UiTheme.CreateFont(8.5F, FontStyle.Regular),
                ForeColor = UiTheme.TextSecondary
            };
            this.Controls.Add(positionLabel);

            _cboToastPosition = new ComboBox
            {
                Location = new Point(left + 205, y - 2),
                Width = 155,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UiTheme.CreateFont(8.5F, FontStyle.Regular),
                ForeColor = UiTheme.TextPrimary
            };
            _cboToastPosition.Items.Add("跟随复制内容");
            _cboToastPosition.Items.Add("屏幕底部");
            _cboToastPosition.SelectedIndexChanged += OnToastPositionChanged;
            _chkCopyToast.CheckedChanged += delegate
            {
                _cboToastPosition.Enabled = _chkCopyToast.Checked;
            };
            this.Controls.Add(_cboToastPosition);

            y += 32;
            AddSection("排除程序", left, y);
            y += 22;
            var txtNewApp = new CueTextBox
            {
                Location = new Point(left, y),
                Width = 250,
                Height = 28,
                BorderStyle = BorderStyle.FixedSingle,
                CueText = "输入程序名称，如 KeePassXC.exe",
                Font = UiTheme.CreateFont(9F, FontStyle.Regular),
                ForeColor = UiTheme.TextPrimary
            };
            _txtNewApp = txtNewApp;
            _txtNewApp.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                AddExcludedApp();
                e.SuppressKeyPress = true;
            };
            this.Controls.Add(_txtNewApp);

            _btnAdd = UiTheme.FlatButton("添加", 86, 28, false);
            _btnAdd.Location = new Point(left + 258, y);
            _btnAdd.Click += delegate { AddExcludedApp(); };
            this.Controls.Add(_btnAdd);

            y += 36;
            _lstExcluded = new ListBox
            {
                Location = new Point(left, y),
                Width = 266,
                Height = 96,
                BorderStyle = BorderStyle.FixedSingle,
                Font = UiTheme.CreateFont(9F, FontStyle.Regular),
                IntegralHeight = false,
                ForeColor = UiTheme.TextPrimary
            };
            this.Controls.Add(_lstExcluded);

            _btnRemove = UiTheme.FlatButton("移除", 86, 28, false);
            _btnRemove.Location = new Point(left + 274, y);
            _btnRemove.Click += delegate { RemoveExcludedApp(); };
            this.Controls.Add(_btnRemove);

            y += 108;
            AddSection("安全", left, y);
            y += 22;
            _chkEncryption = new CheckBox
            {
                Text = "加密历史数据（AES-256，自动密钥）",
                Location = new Point(left, y),
                AutoSize = true,
                ForeColor = UiTheme.TextPrimary
            };
            this.Controls.Add(_chkEncryption);

            y += 28;
            BuildStorageCard(left, y, width);

            y += 78;
            _btnClear = UiTheme.FlatButton("清空全部历史", 150, 30, false);
            _btnClear.ForeColor = UiTheme.Danger;
            _btnClear.FlatAppearance.BorderColor = Color.FromArgb(241, 198, 198);
            _btnClear.Location = new Point(left, y);
            _btnClear.Click += delegate { OnClearHistory(); };
            this.Controls.Add(_btnClear);

            var footerLine = new Panel
            {
                Left = 0,
                Top = 636,
                Width = this.Width,
                Height = 1,
                BackColor = UiTheme.BorderSoft
            };
            _footerLine = footerLine;
            this.Controls.Add(_footerLine);

            _btnCancel = UiTheme.FlatButton("取消", 76, 30, false);
            _btnCancel.Location = new Point(240, 646);
            _btnCancel.DialogResult = DialogResult.Cancel;
            this.Controls.Add(_btnCancel);

            _btnSave = UiTheme.FlatButton("保存", 82, 30, true);
            _btnSave.Location = new Point(324, 646);
            _btnSave.Click += delegate { SaveAndClose(); };
            this.Controls.Add(_btnSave);

            this.CancelButton = _btnCancel;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Borderless fixed layout: shrink vertically (content stays reachable via
            // AutoScroll) when the work area is smaller than the preferred height.
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int height = Math.Min(690, Math.Max(420, area.Height - 24));
            this.Height = height;
            this.Location = new Point(
                area.Left + (area.Width - this.Width) / 2,
                area.Top + (area.Height - height) / 2);
        }

        private void BuildHeader()
        {
            _titleBar = new Panel
            {
                Left = 0,
                Top = 0,
                Width = this.Width,
                Height = 45,
                BackColor = UiTheme.Surface
            };
            this.Controls.Add(_titleBar);
            _titleBar.MouseDown += OnTitleMouseDown;
            _titleBar.MouseMove += OnTitleMouseMove;
            _titleBar.MouseUp += delegate { _dragging = false; };

            var title = new Label
            {
                Text = "ClipLite 设置",
                Font = UiTheme.CreateFont(10.5F, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(18, 13),
                AutoSize = true
            };
            _titleBar.Controls.Add(title);
            AttachDrag(title);

            var close = new Label
            {
                Text = "✕",
                Font = UiTheme.CreateFont(9.5F, FontStyle.Regular),
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(30, 30),
                Location = new Point(this.Width - 42, 7),
                Cursor = Cursors.Hand
            };
            _closeLabel = close;
            close.MouseEnter += delegate { close.ForeColor = UiTheme.TextPrimary; };
            close.MouseLeave += delegate { close.ForeColor = UiTheme.TextMuted; };
            close.Click += delegate
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            _titleBar.Controls.Add(close);

            var line = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = UiTheme.BorderSoft
            };
            _titleBar.Controls.Add(line);
        }

        private void AttachDrag(Control control)
        {
            control.MouseDown += OnTitleMouseDown;
            control.MouseMove += OnTitleMouseMove;
            control.MouseUp += delegate { _dragging = false; };
        }

        private void OnTitleMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            var screen = this.PointToScreen(e.Location);
            _dragOffset = new Point(screen.X - this.Left, screen.Y - this.Top);
        }

        private void OnTitleMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var screen = this.PointToScreen(e.Location);
            this.Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
        }

        private void AddSection(string text, int x, int y)
        {
            var label = new Label
            {
                Text = text,
                Font = UiTheme.CreateFont(9F, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(x, y),
                AutoSize = true
            };
            this.Controls.Add(label);
        }

        private void BuildStorageCard(int x, int y, int width)
        {
            var card = new RoundedPanel
            {
                Location = new Point(x, y),
                Size = new Size(width, 66),
                Radius = 8,
                BackColor = Color.FromArgb(248, 250, 252),
                BorderColor = UiTheme.BorderSoft
            };
            this.Controls.Add(card);

            AddCardLabel(card, "存储占用", 12, 9);
            AddCardLabel(card, "记录条数", 12, 27);
            AddCardLabel(card, "位置", 12, 45);

            _lblStorageValue = AddCardValue(card, 12, 9);
            _lblEntryValue = AddCardValue(card, 12, 27);
            _lblLocation = AddCardValue(card, 12, 45);
        }

        private static void AddCardLabel(Control parent, string text, int x, int y)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(x, y),
                Width = 84,
                Height = 16,
                Font = UiTheme.CreateFont(8F, FontStyle.Regular),
                ForeColor = UiTheme.TextSecondary
            };
            parent.Controls.Add(label);
        }

        private static Label AddCardValue(Control parent, int x, int y)
        {
            var label = new Label
            {
                Location = new Point(x + 92, y),
                Width = 248,
                Height = 16,
                Font = UiTheme.CreateFont(8F, FontStyle.Regular),
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleRight
            };
            parent.Controls.Add(label);
            return label;
        }

        private void LoadSettings()
        {
            _loadingSettings = true;
            try
            {
                _chkFileDetails.Checked = _settings.ShowFileDetails;
                _rdoFull.Checked = (_settings.CaptureMode == "full");
                _rdoTextOnly.Checked = (_settings.CaptureMode != "full");
                _chkAutoStart.Checked = _settings.AutoStart;
                _chkCopyToast.Checked = _settings.ShowCopyToast;
                _cboToastPosition.SelectedIndex = _settings.ToastPosition == "bottom" ? 1 : 0;
                _cboToastPosition.Enabled = _chkCopyToast.Checked;
                _chkEncryption.Checked = _settings.EnableEncryption;

                _hotkeyModifiers = _settings.HotkeyModifiers;
                _hotkeyKey = _settings.HotkeyKey;
                UpdateHotkeyText();

                _lstExcluded.Items.Clear();
                foreach (var app in _settings.ExcludedApps)
                    _lstExcluded.Items.Add(app);
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void OnToastPositionChanged(object sender, EventArgs e)
        {
            if (_loadingSettings || _cboToastPosition.SelectedIndex < 0) return;

            string position = _cboToastPosition.SelectedIndex == 1 ? "bottom" : "cursor";
            _settings.ToastPosition = position;
            _settings.Save(); // display preference takes effect immediately
            if (ToastPositionChanged != null)
                ToastPositionChanged(position);
        }

        private void UpdateHotkeyText()
        {
            _hotkeyBox.Text = HotkeyManager.FormatHotkey(_hotkeyModifiers, _hotkeyKey);
            _hotkeyBox.ForeColor = UiTheme.TextPrimary;
        }

        private void OnHotkeyEnter(object sender, EventArgs e)
        {
            _hotkeyCapturing = true;
            _hotkeyBox.Text = "按下快捷键…";
            _hotkeyBox.ForeColor = UiTheme.TextMuted;
        }

        private void OnHotkeyLeave(object sender, EventArgs e)
        {
            _hotkeyCapturing = false;
            UpdateHotkeyText();
        }

        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            if (!_hotkeyCapturing) return;
            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode == Keys.Escape)
            {
                UpdateHotkeyText();
                return;
            }
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                _hotkeyModifiers = 0;
                _hotkeyKey = 0;
                UpdateHotkeyText();
                return;
            }

            int modifiers;
            int key;
            if (HotkeyManager.TryCapture(e.KeyData, out modifiers, out key))
            {
                _hotkeyModifiers = modifiers;
                _hotkeyKey = key;
                UpdateHotkeyText();
            }
            else if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu)
            {
                _hotkeyBox.Text = "继续按下主键…";
            }
            else
            {
                _hotkeyBox.Text = "需要 Ctrl / Alt + 主键";
            }
        }

        private void SaveAndClose()
        {
            _settings.ShowFileDetails = _chkFileDetails.Checked;
            _settings.CaptureMode = _rdoFull.Checked ? "full" : "text_only";
            _settings.AutoStart = _chkAutoStart.Checked;
            _settings.HotkeyModifiers = _hotkeyModifiers;
            _settings.HotkeyKey = _hotkeyKey;
            _settings.ShowCopyToast = _chkCopyToast.Checked;
            _settings.ToastPosition = _cboToastPosition.SelectedIndex == 1 ? "bottom" : "cursor";
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
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void AddExcludedApp()
        {
            string name = _txtNewApp.Text.Trim().Trim('"');
            if (name.Length == 0) return;

            try
            {
                name = Path.GetFileName(name).Trim();
            }
            catch
            {
                return;
            }
            if (name.Length == 0) return;

            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                if (name.IndexOf(invalid[i]) >= 0)
                    return;
            }

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";
            name = name.ToLowerInvariant();

            for (int i = 0; i < _lstExcluded.Items.Count; i++)
            {
                string existing = _lstExcluded.Items[i] as string;
                if (existing != null && string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            _lstExcluded.Items.Add(name);
            _txtNewApp.Clear();
        }

        private void RemoveExcludedApp()
        {
            if (_lstExcluded.SelectedIndex >= 0)
                _lstExcluded.Items.RemoveAt(_lstExcluded.SelectedIndex);
        }

        private void UpdateStorageInfo(SafeStorage storage, int entryCount)
        {
            long totalSize = (storage != null) ? storage.GetTotalSize() : 0;
            long maxSize = (_settings != null) ? _settings.MaxTotalSize : 50L * 1024 * 1024;

            _lblStorageValue.Text = FormatBytes(totalSize) + " / " + FormatBytes(maxSize);
            _lblEntryValue.Text = entryCount.ToString();
            _lblLocation.Text = "cliplite_data/";
        }

        private void OnClearHistory()
        {
            if (MessageBox.Show("确定清空全部剪贴板历史？此操作不可撤销。",
                "ClipLite", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                // Program.ClearHistoryCore() deletes the data and closes over these settings.
                this.Tag = "clear";
                this.DialogResult = DialogResult.Abort;
                this.Close();
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
