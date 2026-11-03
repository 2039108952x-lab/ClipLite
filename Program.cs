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
                MessageBox.Show("ClipLite 已经在运行了。", "ClipLite",
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
        private ToastForm _toast;
        private bool _paused;
        private bool _hotkeyRegistered;
        private Icon _appIcon;

        public ClipLiteContext()
        {
            CreateAppIcon();

            // ── Initialize storage ──
            _storage = new SafeStorage();

            // ── Load settings ──
            _settings = ClipLiteSettings.Load();

            // The key must be in place before Load(): encrypted histories can only be
            // decoded with it, and setting it afterwards made them unreadable.
            _storage.EncryptionKey = _settings.EnableEncryption ? _settings.EncryptionKey : "";

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

            // A failed load means the on-disk history is encrypted/corrupt beyond what we
            // can read. Never cleanup assets or overwrite anything in that state.
            if (_storage.LastLoadFailed)
                _storage.BlockWrites = true;
            else
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

            var knownHashes = new HashSet<string>(savedEntries.Select(e => e.Id));
            _clipboardMonitor.SetKnownHashes(knownHashes);
            _clipboardMonitor.ClipboardDataAvailable += OnClipboardData;

            // ── Hotkey ──
            _hotkeyManager = new HotkeyManager();
            _clipboardMonitor.WindowMessageReceived += OnWindowMessage;
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            _clipboardMonitor.EnsureHandle();
            bool hotkeyOk = _hotkeyManager.Configure(_clipboardMonitor.WindowHandle,
                _settings.HotkeyModifiers, _settings.HotkeyKey);
            _hotkeyRegistered = hotkeyOk;

            // ── History form ──
            _historyForm = new HistoryForm(_storage, _thumbCache);
            _historyForm.MaxEntries = _settings.MaxEntries;
            _historyForm.SetEntries(savedEntries);
            _historyForm.EntryCopied += OnEntryCopied;
            _historyForm.EntryRemoved += OnEntryRemoved;
            _historyForm.EntryEvicted += OnEntryEvicted;
            _historyForm.HistoryChanged += SaveHistory;
            _historyForm.SettingsRequested += ShowSettings;

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

            if (!hotkeyOk)
            {
                _trayIcon.ShowBalloonTip(3000, "ClipLite",
                    "全局热键 " + HotkeyManager.FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey)
                    + " 注册失败，可能被其他程序占用。可以从托盘菜单打开历史。",
                    ToolTipIcon.Warning);
            }

            if (_storage.LastLoadFailed)
            {
                MessageBox.Show(
                    "历史记录存在，但当前无法解密或解析（密钥丢失或文件损坏）。\n\n"
                    + "本次运行已进入保护模式：不会写入磁盘，也不会删除任何数据。\n"
                    + "如需重新开始，请在托盘菜单执行「清空历史」。",
                    "ClipLite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // ── Capture current clipboard at startup ──
            TryCaptureCurrentClipboard();

            // Confirm in the same lightweight style as the copy toast.
            ShowStartupToast();
        }

        private void ShowStartupToast()
        {
            string detail = _hotkeyRegistered
                ? "按 " + HotkeyManager.FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey) + " 打开历史"
                : "可从托盘图标打开历史";

            if (_toast == null || _toast.IsDisposed)
                _toast = new ToastForm();
            _toast.PositionMode = _settings.ToastPosition == "bottom" ? "bottom" : "cursor";
            _toast.ShowToast(detail, "ClipLite 已启动");
        }

        private void OnWindowMessage(Message m)
        {
            _hotkeyManager.HandleMessage(ref m);
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
        }

        // ── Multi-format clipboard handler ──

        private void OnClipboardData(ClipboardFormatFlags formats, string text, Image image, string[] files, string rtf, string html, byte[] audio)
        {
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
                ShowCopyToast(BuildClipboardPreview(primaryType, fallbackText, files, rtf, image));
                if (image != null)
                {
                    try { image.Dispose(); } catch { }
                }
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

            // The image is only needed while building the entry; release it on every path,
            // not just when it was the primary format.
            if (image != null)
            {
                try { image.Dispose(); } catch { }
            }

            SaveHistory();
            ShowCopyToast(entry.PreviewText);
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

        private void OnEntryCopied(ClipboardEntry entry)
        {
            bool copied = false;
            switch (entry.Type)
            {
                case "text":
                case "richtext":
                case "html":
                    copied = _clipboardMonitor.CopyText(entry.Text ?? "");
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
                                copied = _clipboardMonitor.CopyImage(img);
                            }
                        }
                    }
                    break;

                case "filelist":
                    var paths = entry.GetFilePaths();
                    if (paths.Length > 0)
                        copied = _clipboardMonitor.CopyFilePaths(paths);
                    break;
            }

            if (copied)
                ShowCopyToast(entry.PreviewText);
        }

        private string BuildClipboardPreview(string primaryType, string text, string[] files,
            string rtf, Image image)
        {
            string preview = text ?? "";

            if (string.IsNullOrWhiteSpace(preview))
            {
                if (primaryType == "image" && image != null)
                    preview = "[图片] (" + image.Width + "x" + image.Height + ")";
                else if (primaryType == "filelist" && files != null && files.Length > 0)
                {
                    preview = files.Length > 1
                        ? files.Length + " 个文件 · " + Path.GetFileName(files[0])
                        : Path.GetFileName(files[0]);
                }
                else if (primaryType == "richtext")
                    preview = ExtractRtfText(rtf);
                else if (primaryType == "html" && !string.IsNullOrEmpty(text))
                    preview = text;
                if (string.IsNullOrEmpty(preview)) preview = "内容已复制";
            }

            return preview;
        }

        private void ShowCopyToast(string preview)
        {
            if (_settings == null || !_settings.ShowCopyToast) return;

            string text = (preview ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 48) text = text.Substring(0, 48) + "…";
            if (_toast == null || _toast.IsDisposed)
                _toast = new ToastForm();
            if (_historyForm != null && _historyForm.Visible)
                _historyForm.SuppressDeactivateFor(600);
            _toast.PositionMode = _settings.ToastPosition == "bottom" ? "bottom" : "cursor";
            _toast.ShowToast(text);
        }

        private void OnEntryRemoved(ClipboardEntry entry)
        {
            if (entry == null) return;
            _clipboardMonitor.RemoveHash(entry.Id);
            if (!string.IsNullOrEmpty(entry.ImageFile))
                _thumbCache.Remove(entry.ImageFile);
            _storage.DeleteAsset(entry.ImageFile);
            _storage.DeleteAsset(entry.RtfFile);
            _storage.DeleteAsset(entry.HtmlFile);
            _storage.DeleteAsset(entry.AudioFile);
            SaveHistory();
        }

        private void OnEntryEvicted(ClipboardEntry entry)
        {
            // Trimming happens inside AddEntry; the caller saves immediately after.
            if (entry == null) return;
            _clipboardMonitor.RemoveHash(entry.Id);
            if (!string.IsNullOrEmpty(entry.ImageFile))
                _thumbCache.Remove(entry.ImageFile);
            _storage.DeleteAsset(entry.ImageFile);
            _storage.DeleteAsset(entry.RtfFile);
            _storage.DeleteAsset(entry.HtmlFile);
            _storage.DeleteAsset(entry.AudioFile);
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

        private void OnHotkeyPressed() { ShowHistory(); }

        private void ShowHistory()
        {
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
            if (MessageBox.Show("确定清空全部剪贴板历史？此操作不可撤销。", "ClipLite",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                ClearHistoryCore();
            }
        }

        private void ClearHistoryCore()
        {
            _storage.ClearAll();
            _historyForm.ClearEntries();
            _clipboardMonitor.SetKnownHashes(new HashSet<string>());
            SaveHistory();
        }

        private void SaveHistory()
        {
            _storage.Save(_historyForm.GetAllEntries());
        }


        private void ShowSettings()
        {
            // Suspend the global hotkey while the capture box is open, otherwise pressing
            // the current combination would open the history panel instead of being captured.
            _hotkeyManager.Unregister(_clipboardMonitor.WindowHandle);

            // A clone keeps Cancel side-effect free and lets OnSettingsChanged compare
            // the previous encryption values instead of seeing mutated settings.
            using (var form = new SettingsForm(_settings.Clone(), OnSettingsChanged,
                _storage, _historyForm.GetAllEntries().Count))
            {
                form.ToastPositionChanged += OnToastPositionChanged;
                form.ShowDialog();
                if ((form.Tag as string) == "clear")
                    ClearHistoryCore();
            }

            bool hotkeyOk = _hotkeyManager.Configure(_clipboardMonitor.WindowHandle,
                _settings.HotkeyModifiers, _settings.HotkeyKey);
            if (!hotkeyOk && _trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(3000, "ClipLite",
                    "热键 " + HotkeyManager.FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey)
                    + " 注册失败，可能被其他程序占用。",
                    ToolTipIcon.Warning);
            }
        }

        private void OnToastPositionChanged(string position)
        {
            // The settings form saved this preference immediately; mirror it in the
            // runtime instance so the next copy honours it without reopening the app.
            if (_settings != null)
                _settings.ToastPosition = position;
        }

        private void OnSettingsChanged(ClipLiteSettings newSettings)
        {
            bool encryptionChanged = newSettings.EnableEncryption != _settings.EnableEncryption
                                     || newSettings.EncryptionKey != _settings.EncryptionKey;
            _settings = newSettings;

            _clipboardMonitor.ExcludedApps = _settings.ExcludedApps;
            ClipboardEntry.ShowFileDetails = _settings.ShowFileDetails;
            _clipboardMonitor.CaptureMode = _settings.CaptureMode;
            _historyForm.MaxEntries = _settings.MaxEntries;
            SetAutoStart(_settings.AutoStart);

            // Re-encrypt (or decrypt) the existing history with the new key. Without this,
            // turning encryption off would leave an encrypted file the next start can't read.
            if (encryptionChanged && !_storage.BlockWrites)
            {
                _storage.EncryptionKey = _settings.EnableEncryption ? _settings.EncryptionKey : "";
                _storage.Save(_historyForm.GetAllEntries());
            }
        }

        private void SetAutoStart(bool enabled)
        {
            try
            {
                string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        // Quote the path: an unquoted value in a directory that contains a
                        // space ("C:\Program Files\...") makes Windows launch the wrong target.
                        key.SetValue("ClipLite", "\"" + Application.ExecutablePath + "\"");
                        ClearStartupApproval();
                    }
                    else
                    {
                        key.DeleteValue("ClipLite", false);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// A Run value on its own is not enough. Task Manager's "Disabled" switch is
        /// stored as a separate binary flag under StartupApproved, and Windows silently
        /// skips the entry while that flag is set — the value exists, yet nothing runs
        /// at logon. Clear the flag whenever autostart is (re-)enabled so what we just
        /// wrote actually launches.
        /// </summary>
        private static void ClearStartupApproval()
        {
            try
            {
                using (var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true))
                {
                    byte[] state = approved == null ? null : approved.GetValue("ClipLite") as byte[];
                    if (state != null && state.Length > 0 && (state[0] & 1) == 0)
                        approved.DeleteValue("ClipLite", false);
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
            if (_toast != null && !_toast.IsDisposed)
                _toast.Dispose();
            _toast = null;
            if (_appIcon != null)
                _appIcon.Dispose();
            Application.Exit();
        }

        protected override void ExitThreadCore()
        {
            if (_trayIcon != null) _trayIcon.Visible = false;
            base.ExitThreadCore();
        }
    }
}





