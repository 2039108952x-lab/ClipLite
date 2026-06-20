using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClipLite
{
    static class Program
    {
        private static System.Threading.Mutex _mutex;

        [STAThread]
        static void Main()
        {
            bool firstInstance;
            _mutex = new System.Threading.Mutex(true, "ClipLite-Singleton-Mutex", out firstInstance);

            if (!firstInstance)
            {
                _mutex.Close();
                MessageBox.Show("ClipLite is already running.", "ClipLite",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var context = new ClipLiteContext())
            {
                Application.Run(context);
            }

            _mutex.Close();
        }
    }

    public class ClipLiteContext : ApplicationContext
    {
        private NotifyIcon _trayIcon;
        private HistoryForm _historyForm;
        private ClipboardMonitor _clipboardMonitor;
        private HotkeyManager _hotkeyManager;
        private SafeStorage _storage;
        private ClipLiteSettings _settings;
        private ThumbnailCache _thumbCache;
        private bool _paused;
        private Icon _appIcon;
        private Icon _successIcon;

        public ClipLiteContext()
        {
            CreateAppIcon();

            // ── Initialize storage ──
            _storage = new SafeStorage();

            // ── Load settings ──
            _settings = ClipLiteSettings.Load();

            // V1 → V2 migration
            if (_storage.HasV1Data())
            {
                var migrated = _storage.MigrateFromV1();
                if (migrated != null)
                {
                    // Migration successful
                }
            }

            var savedEntries = _storage.Load();

            // Clean up orphaned assets after loading
            _storage.CleanupOrphans(savedEntries);

            // ── Initialize thumbnail cache ──
            _thumbCache = new ThumbnailCache();

            // ── Initialize clipboard monitor ──
            _clipboardMonitor = new ClipboardMonitor();

            // Apply settings to monitor
            _clipboardMonitor.ExcludedApps = _settings.ExcludedApps;
            ClipboardEntry.ShowFileDetails = _settings.ShowFileDetails;
            _clipboardMonitor.CaptureMode = _settings.CaptureMode;
            SetAutoStart(_settings.AutoStart);
            ClipboardEntry.ShowFileDetails = _settings.ShowFileDetails;

            // Apply encryption key to storage
            _storage.EncryptionKey = _settings.EnableEncryption ? _settings.EncryptionKey : "";
            var knownHashes = new HashSet<string>(savedEntries.Select(e => e.Id));
            _clipboardMonitor.SetKnownHashes(knownHashes);
            _clipboardMonitor.ClipboardDataAvailable += OnClipboardData;

            // ── Hotkey ──
            _hotkeyManager = new HotkeyManager();
            _clipboardMonitor.HotkeyMessageReceived += OnWindowMessage;
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            _clipboardMonitor.EnsureHandle();
            _clipboardMonitor.SetHotkeyManager(_hotkeyManager);
            _hotkeyManager.Modifiers = _settings.HotkeyModifiers;
            _hotkeyManager.KeyCode = _settings.HotkeyKey;
            bool hotkeyOk = _hotkeyManager.Register(_clipboardMonitor.WindowHandle);
            if (!hotkeyOk && (_settings.HotkeyModifiers != 0 || _settings.HotkeyKey != 0))
            {
                _trayIcon.Text = "ClipLite (快捷键注册失败)";
            }

            // ── History form ──
            _historyForm = new HistoryForm(_storage, _thumbCache);
            _historyForm.SetEntries(savedEntries);
            _historyForm.ItemSelected += OnItemSelected;
            _historyForm.EntryCopied += OnEntryCopied;
            ToastForm.ToastEnabled = _settings.ShowCopyToast;

            // ── Tray icon ──
            _trayIcon = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "ClipLite",
                Visible = true
            };

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示历史", null, (s, e) => ShowHistory());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("暂停监听", null, (s, e) => TogglePause());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("清空历史", null, (s, e) => ClearHistory());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("设置", null, (s, e) => ShowSettings());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, (s, e) => ExitApp());

            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.DoubleClick += (s, e) => ShowHistory();

            // ── Capture current clipboard at startup ──
            TryCaptureCurrentClipboard();
        }

        private bool OnWindowMessage(Message m)
        {
            // _hotkeyManager.HandleMessage takes ref Message; we create a local copy
            // since the event system passes structs by value
            Message localMsg = m;
            return _hotkeyManager.HandleMessage(ref localMsg);
        }

        private void CreateAppIcon()
        {
            using (var bmp = new Bitmap(16, 16))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    using (var pen = new Pen(Color.FromArgb(0, 120, 215), 1.5f))
                    {
                        g.DrawRectangle(pen, 2, 2, 12, 12);
                        g.DrawLine(pen, 5, 5, 11, 5);
                        g.DrawLine(pen, 5, 8, 11, 8);
                        g.DrawLine(pen, 5, 11, 9, 11);
                    }

                    using (var brush = new SolidBrush(Color.FromArgb(0, 120, 215)))
                    {
                        g.FillRectangle(brush, 5, 1, 6, 2);
                    }
                }

                var hIcon = bmp.GetHicon();
                _appIcon = Icon.FromHandle(hIcon);
            }

            // ── Success icon (green checkmark) ──
            using (var bmp = new Bitmap(16, 16))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    using (var brush = new SolidBrush(Color.FromArgb(0, 180, 80)))
                    {
                        g.FillEllipse(brush, 1, 1, 14, 14);
                    }

                    using (var pen = new Pen(Color.White, 2.5f))
                    {
                        g.DrawLine(pen, 4, 8, 7, 12);
                        g.DrawLine(pen, 7, 12, 13, 4);
                    }
                }

                var hSuccess = bmp.GetHicon();
                _successIcon = Icon.FromHandle(hSuccess);
            }
        }

        // ── Multi-format clipboard handler ──

        private void OnClipboardData(ClipboardFormatFlags formats, string text, Image image, string[] files, string rtf, string html, byte[] audio)
        {
            // Visual: tray icon → green checkmark
            FlashIcon();
            // Bottom status in history panel
            try { _historyForm.ShowStatus("✔ 已捕获"); } catch { }

            if (_paused) return;

            // Determine primary type
            string primaryType = "text";
            if (formats.HasFlag(ClipboardFormatFlags.Image)) primaryType = "image";
            else if (formats.HasFlag(ClipboardFormatFlags.FileList)) primaryType = "filelist";
            else if (formats.HasFlag(ClipboardFormatFlags.RichText)) primaryType = "richtext";
            else if (formats.HasFlag(ClipboardFormatFlags.Html)) primaryType = "html";

            string fallbackText = text ?? "";

            // Hash dedup based on primary content
            string hash = null;
            if (primaryType == "text" || primaryType == "richtext" || primaryType == "html")
            {
                hash = _clipboardMonitor.ComputeHash(fallbackText);
            }
            else if (primaryType == "image" && image != null)
            {
                var hashData = ThumbnailCache.GetImageHashData(image);
                if (hashData != null)
                    hash = _clipboardMonitor.ComputeBlobHash(hashData);
            }
            else if (primaryType == "audio" && audio != null)
            {
                hash = _clipboardMonitor.ComputeBlobHash(audio);
            }
            else if (primaryType == "filelist" && files != null && files.Length > 0)
            {
                hash = _clipboardMonitor.ComputeHash(string.Join("|", files));
            }

            if (string.IsNullOrEmpty(hash))
                hash = Guid.NewGuid().ToString("N");

            // Check dedup
            if (_clipboardMonitor.IsKnownHash(hash))
            {
                _historyForm.MoveToTop(hash);
                SaveHistory();
                return;
            }

            // Build entry
            var entry = new ClipboardEntry
            {
                Id = hash,
                Type = primaryType,
                Text = fallbackText,
                Timestamp = DateTime.Now
            };

            // Handle each format
            if (primaryType == "image" && image != null)
            {
                // Save as PNG
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        image.Save(ms, ImageFormat.Png);
                        byte[] pngData = ms.ToArray();
                        entry.Size = pngData.Length;
                        entry.ImageFile = _storage.SaveAsset(hash, pngData, ".png");
                        entry.ImageWidth = image.Width;
                        entry.ImageHeight = image.Height;
                    }
                }
                catch { }

                // Clean up image
                try { image.Dispose(); } catch { }
            }
            else if (primaryType == "filelist" && files != null && files.Length > 0)
            {
                entry.SetFilePaths(files);
                entry.Size = entry.FilePathsRaw.Length;
            }
            else if (primaryType == "richtext" && rtf != null)
            {
                if (rtf.Length <= SafeStorage.InlineThreshold)
                {
                    entry.RtfData = rtf;
                    entry.Size = rtf.Length * 2; // UTF-16 estimate
                }
                else
                {
                    string rtfHash = ClipboardEntry.ComputeSha1(rtf);
                    byte[] rtfBytes = Encoding.UTF8.GetBytes(rtf);
                    entry.RtfFile = _storage.SaveAsset(rtfHash, rtfBytes, ".rtf");
                    entry.Size = rtfBytes.Length;
                }
                // Extract plain text for preview
                entry.RtfPreview = ExtractRtfText(rtf);
            }
            else if (primaryType == "html" && html != null)
            {
                if (html.Length <= SafeStorage.InlineThreshold)
                {
                    entry.HtmlData = html;
                    entry.Size = html.Length * 2;
                }
                else
                {
                    string htmlHash = ClipboardEntry.ComputeSha1(html);
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
                    entry.HtmlFile = _storage.SaveAsset(htmlHash, htmlBytes, ".html");
                    entry.Size = htmlBytes.Length;
                }
            }
            else
            {
                // Plain text
                entry.Size = Encoding.UTF8.GetByteCount(fallbackText);
            }

            // Add to history

            // Store source file paths when available (for all types)
            if (files != null && files.Length > 0 && string.IsNullOrEmpty(entry.FilePathsRaw))
            {
                entry.SetFilePaths(files);
                if (entry.Size == 0)
                    entry.Size = entry.FilePathsRaw.Length;
            }
            _historyForm.AddEntry(entry);
            _clipboardMonitor.AddHash(hash);

            SaveHistory();

        }

        private string ExtractRtfText(string rtf)
        {
            if (string.IsNullOrEmpty(rtf)) return "";
            try
            {
                using (var rtb = new RichTextBox())
                {
                    rtb.Rtf = rtf;
                    string text = rtb.Text;
                    if (text.Length > 200) text = text.Substring(0, 200);
                    return text;
                }
            }
            catch { return ""; }
        }

        // ── Clipboard operations ──

        private void OnItemSelected(string text)
        {
            // Backward compat — handled by EntryCopied
        }

        private void OnEntryCopied(ClipboardEntry entry)
        {
            switch (entry.Type)
            {
                case "text":
                case "richtext":
                case "html":
                    _clipboardMonitor.CopyText(entry.Text ?? "");
                    break;

                case "image":
                    if (!string.IsNullOrEmpty(entry.ImageFile))
                    {
                        byte[] imgData = _storage.LoadAsset(entry.ImageFile);
                        if (imgData != null)
                        {
                            using (var ms = new MemoryStream(imgData))
                            using (var img = Image.FromStream(ms))
                            {
                                _clipboardMonitor.CopyImage(img);
                            }
                        }
                    }
                    break;

                case "filelist":
                    var paths = entry.GetFilePaths();
                    if (paths.Length > 0)
                        _clipboardMonitor.CopyFilePaths(paths);
                    break;
            }

            // Visual: tray icon → green checkmark
            FlashIcon();
            // Bottom status in history panel
            try { _historyForm.ShowStatus("✔ 已复制"); } catch { }
        }

        // ── Startup clipboard capture ──

        private void TryCaptureCurrentClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        string hash = _clipboardMonitor.ComputeHash(text);
                        if (!_clipboardMonitor.IsKnownHash(hash))
                        {
                            var entry = new ClipboardEntry { Id = hash, Text = text, Timestamp = DateTime.Now };
                                                        entry.Size = Encoding.UTF8.GetByteCount(text);
                            _historyForm.AddEntry(entry);
                            _clipboardMonitor.AddHash(hash);
                            SaveHistory();
                        }
                    }
                }
            }
            catch { }
        }

        // ── UI events ──

        private void OnHotkeyPressed(string source) { ShowHistory(); }

        /// <summary>
        /// Swap tray icon to green checkmark for 1.5s, then revert to blue clipboard.
        /// Visible in system tray without hovering — unlike tooltip text changes.
        /// </summary>
        private void FlashIcon()
        {
            try
            {
                _trayIcon.Icon = _successIcon;
                var t = new Timer { Interval = 1500 };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    t.Dispose();
                    try { _trayIcon.Icon = _appIcon; } catch { }
                };
                t.Start();
            }
            catch { }
        }

        private void ShowHistory()
        {
            if (_historyForm == null || _historyForm.IsDisposed) return;

            if (_historyForm.Visible)
            {
                _historyForm.Hide();
                return;
            }

            var cursor = Cursor.Position;
            var screen = Screen.FromPoint(cursor).WorkingArea;

            int x = cursor.X - 20;
            int y = cursor.Y - 20;

            if (x + _historyForm.Width > screen.Right)
                x = screen.Right - _historyForm.Width - 10;
            if (x < screen.Left) x = screen.Left + 10;
            if (y + _historyForm.Height > screen.Bottom)
                y = screen.Bottom - _historyForm.Height - 10;
            if (y < screen.Top) y = screen.Top + 10;

            _historyForm.Location = new Point(x, y);
            _historyForm.Show();
            _historyForm.Activate();
            _historyForm.FocusSearch();
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused)
            {
                _trayIcon.Text = "ClipLite (Paused)";
                _trayIcon.ContextMenuStrip.Items[2].Text = "恢复监听";
            }
            else
            {
                _trayIcon.Text = "ClipLite";
                _trayIcon.ContextMenuStrip.Items[2].Text = "暂停监听";
            }
        }

        private void ClearHistory()
        {
            if (MessageBox.Show("Clear all clipboard history?", "ClipLite",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _historyForm.ClearEntries();
                _clipboardMonitor.SetKnownHashes(new HashSet<string>());
                SaveHistory();
            }
        }

        private void SaveHistory()
        {
            _storage.Save(_historyForm.GetAllEntries());
        }


        private void ShowSettings()
        {
            using (var form = new SettingsForm(_settings, OnSettingsChanged, _storage))
            {
                form.ShowDialog();
                if (form.Tag as string == "clear")
                {
                    _historyForm.ClearEntries();
                    _clipboardMonitor.SetKnownHashes(new HashSet<string>());
                    SaveHistory();
                }
            }
        }

        private void OnSettingsChanged(ClipLiteSettings newSettings)
        {
            _settings = newSettings;
            _clipboardMonitor.ExcludedApps = _settings.ExcludedApps;
            _storage.EncryptionKey = _settings.EnableEncryption ? _settings.EncryptionKey : "";
            ClipboardEntry.ShowFileDetails = _settings.ShowFileDetails;
            ToastForm.ToastEnabled = _settings.ShowCopyToast;
            if (_hotkeyManager.Modifiers != _settings.HotkeyModifiers || _hotkeyManager.KeyCode != _settings.HotkeyKey)
            {
                _hotkeyManager.Unregister(_clipboardMonitor.WindowHandle);
                _hotkeyManager.Modifiers = _settings.HotkeyModifiers;
                _hotkeyManager.KeyCode = _settings.HotkeyKey;
                bool ok = _hotkeyManager.Register(_clipboardMonitor.WindowHandle);
                if (!ok && (_settings.HotkeyModifiers != 0 || _settings.HotkeyKey != 0))
                    _trayIcon.Text = "ClipLite (快捷键注册失败)";
                else
                    _trayIcon.Text = "ClipLite";
            }
            _clipboardMonitor.CaptureMode = _settings.CaptureMode;
            SetAutoStart(_settings.AutoStart);
        }

        private void SetAutoStart(bool enabled)
        {
            try
            {
                string keyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
                string appPath = typeof(Program).Assembly.Location;
                string q = "\"";
                if (enabled)
                {
                    string arg = "add " + q + keyPath + q + " /v " + q + "ClipLite" + q + " /d " + q + appPath + q + " /f";
                    System.Diagnostics.Process.Start("reg.exe", arg);
                }
                else
                {
                    string arg = "delete " + q + keyPath + q + " /v " + q + "ClipLite" + q + " /f";
                    System.Diagnostics.Process.Start("reg.exe", arg);
                }
            }
            catch { }
        }
        private void ExitApp()
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _hotkeyManager.Unregister(_clipboardMonitor.WindowHandle);
            _clipboardMonitor.Dispose();
            _thumbCache.Dispose();
            _historyForm.Dispose();
            if (_appIcon != null)
                _appIcon.Dispose();
            Application.Exit();
        }

        protected override void ExitThreadCore()
        {
            try
            {
                if (_hotkeyManager != null && _clipboardMonitor != null)
                    _hotkeyManager.Unregister(_clipboardMonitor.WindowHandle);
                if (_clipboardMonitor != null)
                    _clipboardMonitor.Dispose();
                if (_thumbCache != null)
                    _thumbCache.Dispose();
                if (_historyForm != null && !_historyForm.IsDisposed)
                    _historyForm.Dispose();
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                if (_appIcon != null)
                    _appIcon.Dispose();
            }
            catch { }
            base.ExitThreadCore();
        }
    }
}





