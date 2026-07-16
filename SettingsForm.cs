using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ClipLite
{
    public class SettingsForm : Form
    {
        private const int WindowWidth = 360;
        private const int WindowHeight = 864;
        private const int HeaderHeight = 48;
        private const int FooterHeight = 58;
        // Leave two extra pixels on the right so a vertical scrollbar never
        // causes an unwanted horizontal scrollbar on short displays.
        private const int ContentWidth = 326;

        private readonly ClipLiteSettings _settings;
        private readonly Func<ClipLiteSettings, string> _applySettings;
        private readonly SafeStorage _storage;
        private readonly Action _clearHistory;

        private Panel _content;
        private ModernCheckBox _showFileDetails;
        private ModernRadioButton _captureFull;
        private ModernRadioButton _captureTextOnly;
        private ToggleSwitch _autoStart;
        private TextBox _hotkeyText;
        private Label _hotkeyMessage;
        private RoundedPanel _hotkeyField;
        private ModernCheckBox _showCopyToast;
        private TextBox _excludedInput;
        private ExcludedAppsList _excludedList;
        private ModernCheckBox _enableEncryption;
        private StorageInfoCard _storageCard;
        private ModernButton _clearButton;
        private ModernButton _saveButton;

        private uint _capturedModifiers;
        private uint _capturedKey;

        public SettingsForm(ClipLiteSettings settings,
            Func<ClipLiteSettings, string> applySettings,
            SafeStorage storage,
            Action clearHistory)
        {
            _settings = settings == null ? new ClipLiteSettings() : settings.Clone();
            _applySettings = applySettings;
            _storage = storage;
            _clearHistory = clearHistory;

            InitializeComponent();
            LoadSettings();
            UpdateStorageInfo();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            Text = "ClipLite 设置";
            ClientSize = new Size(WindowWidth, WindowHeight);
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = UiTheme.Surface;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            CreateHeader();
            CreateContent();
            CreateFooter();
            ResumeLayout(false);
        }

        private void CreateHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Width = WindowWidth,
                Height = HeaderHeight,
                BackColor = UiTheme.Surface
            };
            var title = new Label
            {
                Text = "ClipLite 设置",
                Font = new Font(UiTheme.FontFamilyName, 11.0f, FontStyle.Bold),
                ForeColor = UiTheme.OnSurface,
                Location = new Point(16, 15),
                AutoSize = true
            };
            header.Controls.Add(title);
            header.MouseDown += BeginWindowDrag;
            title.MouseDown += BeginWindowDrag;

            var close = new ModernButton
            {
                Text = "×",
                Font = new Font("Segoe UI Symbol", 15.0f),
                TextColor = UiTheme.OnSurfaceVariant,
                FillColor = UiTheme.Surface,
                HoverColor = UiTheme.SurfaceContainerHigh,
                PressedColor = UiTheme.SurfaceContainer,
                BorderColor = Color.Transparent,
                Radius = 5,
                Size = new Size(32, 32),
                Location = new Point(WindowWidth - 44, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            close.Click += (s, e) => Close();
            header.Controls.Add(close);
            Controls.Add(header);
        }

        private void CreateContent()
        {
            _content = new Panel
            {
                Location = new Point(0, HeaderHeight),
                Size = new Size(WindowWidth, ClientSize.Height - HeaderHeight - FooterHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                         AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
                BackColor = UiTheme.Surface,
                TabStop = true
            };
            Controls.Add(_content);

            int y = 14;
            _showFileDetails = new ModernCheckBox
            {
                Text = "复制文件时显示文件名称和格式",
                Location = new Point(16, y),
                Width = ContentWidth
            };
            _content.Controls.Add(_showFileDetails);

            y += 30;
            AddSectionLabel("抓取模式", y);
            y += 24;
            _captureFull = new ModernRadioButton
            {
                Text = "全功能模式（存储所有格式）",
                Location = new Point(20, y),
                Width = ContentWidth - 4
            };
            _content.Controls.Add(_captureFull);
            y += 27;
            _captureTextOnly = new ModernRadioButton
            {
                Text = "纯文本模式（仅存储文字）",
                Location = new Point(20, y),
                Width = ContentWidth - 4
            };
            _content.Controls.Add(_captureTextOnly);

            y += 34;
            var autoStartCard = new RoundedPanel
            {
                Location = new Point(16, y),
                Size = new Size(ContentWidth, 58),
                Radius = 6,
                FillColor = Color.FromArgb(252, 252, 252),
                BorderColor = UiTheme.OutlineSoft
            };
            autoStartCard.Controls.Add(new Label
            {
                Text = "开机自启动",
                Font = UiTheme.ItemFont,
                ForeColor = UiTheme.OnSurface,
                Location = new Point(14, 18),
                AutoSize = true,
                BackColor = Color.Transparent
            });
            _autoStart = new ToggleSwitch
            {
                Location = new Point(ContentWidth - 62, 17),
                BackColor = Color.FromArgb(252, 252, 252)
            };
            autoStartCard.Controls.Add(_autoStart);
            _content.Controls.Add(autoStartCard);

            y += 67;
            AddSectionLabel("快捷键（点击输入框后按下组合键）", y);
            y += 24;
            _hotkeyField = CreateTextField(y, ContentWidth, out _hotkeyText);
            _hotkeyText.ReadOnly = true;
            _hotkeyText.ShortcutsEnabled = false;
            _hotkeyText.Cursor = Cursors.Hand;
            _hotkeyText.KeyDown += OnHotkeyKeyDown;
            _hotkeyText.Enter += (s, e) =>
            {
                _hotkeyText.SelectAll();
                ShowHotkeyMessage("按 Backspace 或 Delete 可清除快捷键。", false);
            };
            _hotkeyText.Leave += (s, e) =>
            {
                if (_hotkeyMessage.ForeColor != UiTheme.Error)
                    ShowHotkeyMessage("", false);
            };
            _content.Controls.Add(_hotkeyField);

            y += 43;
            _hotkeyMessage = new Label
            {
                Location = new Point(18, y),
                Size = new Size(ContentWidth - 4, 24),
                Font = UiTheme.MetaFont,
                ForeColor = UiTheme.OnSurfaceVariant,
                BackColor = UiTheme.Surface,
                TextAlign = ContentAlignment.TopLeft
            };
            _content.Controls.Add(_hotkeyMessage);

            y += 16;
            _showCopyToast = new ModernCheckBox
            {
                Text = "复制成功时显示提示",
                Location = new Point(16, y),
                Width = ContentWidth
            };
            _content.Controls.Add(_showCopyToast);

            y += 40;
            AddSectionLabel("排除程序", y);
            y += 26;
            RoundedPanel excludedField = CreateTextField(y, ContentWidth - 82, out _excludedInput);
            SetCueBanner(_excludedInput, "输入程序名称...");
            _excludedInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    AddExcludedApp();
                    e.SuppressKeyPress = true;
                }
            };
            _content.Controls.Add(excludedField);

            var addButton = CreatePrimaryButton("添加");
            addButton.Location = new Point(16 + ContentWidth - 74, y);
            addButton.Size = new Size(74, 38);
            addButton.Click += (s, e) => AddExcludedApp();
            _content.Controls.Add(addButton);

            y += 48;
            var listContainer = new RoundedPanel
            {
                Location = new Point(16, y),
                Size = new Size(ContentWidth, 96),
                Radius = 6,
                FillColor = Color.FromArgb(253, 253, 253),
                BorderColor = UiTheme.OutlineSoft
            };
            _excludedList = new ExcludedAppsList
            {
                Location = new Point(5, 5),
                Size = new Size(ContentWidth - 10, 86),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                         AnchorStyles.Left | AnchorStyles.Right
            };
            _excludedList.RemoveRequested += RemoveExcludedApp;
            listContainer.Controls.Add(_excludedList);
            _content.Controls.Add(listContainer);

            y += 116;
            AddSectionLabel("安全", y);
            y += 25;
            _enableEncryption = new ModernCheckBox
            {
                Text = "加密历史数据（AES-256，自动密钥）",
                Location = new Point(16, y),
                Width = ContentWidth
            };
            _content.Controls.Add(_enableEncryption);

            y += 40;
            _storageCard = new StorageInfoCard
            {
                Location = new Point(16, y),
                Width = ContentWidth
            };
            _content.Controls.Add(_storageCard);

            y += 116;
            _clearButton = new ModernButton
            {
                Text = "清空全部历史",
                Location = new Point(16, y),
                Size = new Size(ContentWidth, 40),
                FillColor = Color.FromArgb(253, 237, 237),
                HoverColor = Color.FromArgb(250, 224, 224),
                PressedColor = Color.FromArgb(247, 210, 210),
                BorderColor = Color.FromArgb(235, 170, 170),
                TextColor = UiTheme.Error,
                Radius = 8
            };
            _clearButton.Click += (s, e) => ClearHistory();
            _content.Controls.Add(_clearButton);

            _content.AutoScrollMinSize = new Size(0, y + 56);
        }

        private void CreateFooter()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = FooterHeight,
                BackColor = Color.FromArgb(251, 251, 251)
            };
            footer.Paint += (s, e) =>
            {
                using (var pen = new Pen(UiTheme.OutlineSoft))
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            var cancel = new ModernButton
            {
                Text = "取消",
                Location = new Point(WindowWidth - 178, 9),
                Size = new Size(70, 40),
                FillColor = Color.FromArgb(251, 251, 251),
                HoverColor = UiTheme.SurfaceContainerHigh,
                PressedColor = UiTheme.SurfaceContainer,
                BorderColor = Color.Transparent,
                TextColor = UiTheme.OnSurfaceVariant,
                Radius = 6,
                DialogResult = DialogResult.Cancel
            };
            footer.Controls.Add(cancel);

            _saveButton = CreatePrimaryButton("保存");
            _saveButton.Location = new Point(WindowWidth - 100, 9);
            _saveButton.Size = new Size(84, 40);
            _saveButton.Click += (s, e) => SaveAndClose();
            footer.Controls.Add(_saveButton);
            Controls.Add(footer);

            CancelButton = cancel;
            AcceptButton = _saveButton;
        }

        private void AddSectionLabel(string text, int y)
        {
            _content.Controls.Add(new Label
            {
                Text = text,
                Font = UiTheme.ChipFont,
                ForeColor = UiTheme.Outline,
                Location = new Point(16, y),
                Size = new Size(ContentWidth, 18),
                BackColor = UiTheme.Surface
            });
        }

        private RoundedPanel CreateTextField(int y, int width, out TextBox textBox)
        {
            var field = new RoundedPanel
            {
                Location = new Point(16, y),
                Size = new Size(width, 38),
                Radius = 5,
                FillColor = Color.FromArgb(253, 253, 253),
                BorderColor = UiTheme.Outline,
                FocusBorderColor = UiTheme.Primary
            };
            textBox = new TextBox
            {
                Location = new Point(12, 9),
                Size = new Size(width - 24, 22),
                BorderStyle = BorderStyle.None,
                Font = UiTheme.ItemFont,
                ForeColor = UiTheme.OnSurface,
                BackColor = Color.FromArgb(253, 253, 253),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            textBox.Enter += (s, e) => field.HasInputFocus = true;
            textBox.Leave += (s, e) => field.HasInputFocus = false;
            field.Controls.Add(textBox);
            return field;
        }

        private ModernButton CreatePrimaryButton(string text)
        {
            return new ModernButton
            {
                Text = text,
                FillColor = UiTheme.Primary,
                HoverColor = Color.FromArgb(0, 94, 178),
                PressedColor = Color.FromArgb(0, 65, 124),
                BorderColor = UiTheme.Primary,
                TextColor = Color.White,
                Radius = 6
            };
        }

        private void LoadSettings()
        {
            _showFileDetails.Checked = _settings.ShowFileDetails;
            _captureFull.Checked = _settings.CaptureMode == "full";
            _captureTextOnly.Checked = !_captureFull.Checked;
            _autoStart.Checked = _settings.AutoStart;
            _capturedModifiers = _settings.HotkeyModifiers;
            _capturedKey = _settings.HotkeyKey;
            _hotkeyText.Text = HotkeyGesture.Format(_capturedModifiers, _capturedKey);
            _showCopyToast.Checked = _settings.ShowCopyToast;
            _enableEncryption.Checked = _settings.EnableEncryption;

            _excludedList.Items.Clear();
            foreach (string app in _settings.ExcludedApps ?? new List<string>())
                _excludedList.Items.Add(app);
        }

        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.Escape)
            {
                _saveButton.Focus();
                return;
            }
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                _capturedModifiers = 0;
                _capturedKey = 0;
                _hotkeyText.Text = "无";
                ShowHotkeyMessage("快捷键已禁用，保存后生效。", false);
                return;
            }
            if (HotkeyGesture.IsModifierKey(e.KeyCode))
            {
                ShowHotkeyMessage("请继续按一个字母、数字或功能键。", false);
                return;
            }

            uint modifiers = 0;
            if (e.Control) modifiers |= HotkeyGesture.Control;
            if (e.Shift) modifiers |= HotkeyGesture.Shift;
            if (e.Alt) modifiers |= HotkeyGesture.Alt;
            if (IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin))
                modifiers |= HotkeyGesture.Win;

            string error;
            if (!HotkeyGesture.TryValidate(modifiers, (uint)e.KeyCode, out error))
            {
                ShowHotkeyMessage(error, true);
                return;
            }

            _capturedModifiers = modifiers;
            _capturedKey = (uint)e.KeyCode;
            _hotkeyText.Text = HotkeyGesture.Format(_capturedModifiers, _capturedKey);
            _hotkeyText.SelectAll();
            ShowHotkeyMessage("保存时会检查该组合是否被其他程序占用。", false);
        }

        private void ShowHotkeyMessage(string text, bool isError)
        {
            if (_hotkeyMessage == null) return;
            _hotkeyMessage.Text = text ?? "";
            _hotkeyMessage.ForeColor = isError ? UiTheme.Error : UiTheme.OnSurfaceVariant;
            _hotkeyField.BorderColor = isError ? UiTheme.Error : UiTheme.Outline;
            _hotkeyField.Invalidate();
        }

        private void SaveAndClose()
        {
            string validationError;
            if (!HotkeyGesture.TryValidate(_capturedModifiers, _capturedKey, out validationError))
            {
                ShowHotkeyMessage(validationError, true);
                _hotkeyText.Focus();
                return;
            }

            ClipLiteSettings candidate = BuildCandidateSettings();
            string applyError = _applySettings == null ? null : _applySettings(candidate);
            if (!string.IsNullOrEmpty(applyError))
            {
                ShowHotkeyMessage(applyError, true);
                _hotkeyText.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private ClipLiteSettings BuildCandidateSettings()
        {
            var candidate = _settings.Clone();
            candidate.ShowFileDetails = _showFileDetails.Checked;
            candidate.CaptureMode = _captureFull.Checked ? "full" : "text_only";
            candidate.AutoStart = _autoStart.Checked;
            candidate.HotkeyModifiers = _capturedModifiers;
            candidate.HotkeyKey = _capturedKey;
            candidate.ShowCopyToast = _showCopyToast.Checked;
            candidate.EnableEncryption = _enableEncryption.Checked;
            if (!candidate.EnableEncryption) candidate.EncryptionKey = "";

            candidate.ExcludedApps = new List<string>();
            foreach (string app in _excludedList.Items)
                candidate.ExcludedApps.Add(app);
            return candidate;
        }

        private void AddExcludedApp()
        {
            string name = (_excludedInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";

            bool exists = _excludedList.Items.Cast<string>()
                .Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            if (!exists) _excludedList.Items.Add(name);
            _excludedInput.Clear();
            _excludedInput.Focus();
        }

        private void RemoveExcludedApp(int index)
        {
            if (index >= 0 && index < _excludedList.Items.Count)
                _excludedList.Items.RemoveAt(index);
        }

        private void ClearHistory()
        {
            DialogResult result = MessageBox.Show(
                "确定清空全部剪贴板历史吗？此操作无法撤销。",
                "ClipLite", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            if (_clearHistory != null) _clearHistory();
            UpdateStorageInfo();
            string originalText = _clearButton.Text;
            _clearButton.Text = "已清空";
            var timer = new Timer { Interval = 1400 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!_clearButton.IsDisposed) _clearButton.Text = originalText;
            };
            timer.Start();
        }

        private void UpdateStorageInfo()
        {
            long totalSize = _storage == null ? 0 : _storage.GetTotalSize();
            long maximum = _settings.MaxTotalSize > 0
                ? _settings.MaxTotalSize : 50L * 1024 * 1024;
            int entryCount = 0;
            string indexPath = _storage == null ? "" : Path.Combine(_storage.DataDir, "index.jsonl");
            if (File.Exists(indexPath))
            {
                try
                {
                    entryCount = File.ReadLines(indexPath).Count(line => !string.IsNullOrWhiteSpace(line));
                }
                catch { }
            }

            _storageCard.CurrentBytes = totalSize;
            _storageCard.MaximumBytes = maximum;
            _storageCard.EntryCount = entryCount;
            _storageCard.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeWindowEffects.Apply(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int maximumHeight = Math.Max(620, area.Height - 32);
            if (Height > maximumHeight) Height = maximumHeight;
            Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
            Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            NativeWindowEffects.ApplyRoundedRegion(this, 8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(205, 209, 218)))
                UiTheme.DrawRoundedRectangle(e.Graphics, pen,
                    new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 8);
        }

        private const int EmSetCueBanner = 0x1501;
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, string lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

        private void BeginWindowDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        }

        private static bool IsKeyDown(Keys key)
        {
            return (GetKeyState((int)key) & 0x8000) != 0;
        }

        private static void SetCueBanner(TextBox textBox, string text)
        {
            textBox.HandleCreated += (s, e) =>
            {
                try { SendMessage(textBox.Handle, EmSetCueBanner, (IntPtr)1, text); }
                catch { }
            };
        }
    }
}
