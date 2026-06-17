using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

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
                MessageBox.Show("ClipLite 已在运行中。", "ClipLite",
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
        private JsonStorage _storage;
        private bool _paused;
        private Icon _appIcon;

        public ClipLiteContext()
        {
            CreateAppIcon();

            _storage = new JsonStorage();
            var savedEntries = _storage.Load();

            _clipboardMonitor = new ClipboardMonitor();
            var knownHashes = new HashSet<string>(savedEntries.Select(e => e.Id));
            _clipboardMonitor.SetKnownHashes(knownHashes);
            _clipboardMonitor.ClipboardTextChanged += OnClipboardTextChanged;

            _hotkeyManager = new HotkeyManager();
            _clipboardMonitor.WindowMessageReceived += OnWindowMessage;
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
            _clipboardMonitor.EnsureHandle();
            _hotkeyManager.Register(_clipboardMonitor.WindowHandle);

            _historyForm = new HistoryForm();
            _historyForm.SetEntries(savedEntries);
            _historyForm.ItemSelected += OnItemSelected;

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
            trayMenu.Items.Add("退出", null, (s, e) => ExitApp());

            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.DoubleClick += (s, e) => ShowHistory();

            TryCaptureCurrentClipboard();
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
                            var entry = new ClipboardEntry(text);
                            entry.Id = hash;
                            _historyForm.AddEntry(entry);
                            var allEntries = _historyForm.GetAllEntries();
                            _clipboardMonitor.SetKnownHashes(
                                new HashSet<string>(allEntries.Select(e => e.Id)));
                            SaveHistory();
                        }
                    }
                }
            }
            catch { }
        }

        private void OnClipboardTextChanged(string text)
        {
            if (_paused) return;

            string hash = _clipboardMonitor.ComputeHash(text);
            if (_clipboardMonitor.IsKnownHash(hash))
            {
                _historyForm.MoveToTop(hash);
                SaveHistory();
                return;
            }

            var entry = new ClipboardEntry(text);
            entry.Id = hash;
            _historyForm.AddEntry(entry);

            var allEntries = _historyForm.GetAllEntries();
            _clipboardMonitor.SetKnownHashes(
                new HashSet<string>(allEntries.Select(e => e.Id)));
            SaveHistory();
        }

        private void OnHotkeyPressed()
        {
            ShowHistory();
        }

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
            if (x < screen.Left)
                x = screen.Left + 10;
            if (y + _historyForm.Height > screen.Bottom)
                y = screen.Bottom - _historyForm.Height - 10;
            if (y < screen.Top)
                y = screen.Top + 10;

            _historyForm.Location = new Point(x, y);
            _historyForm.Show();
            _historyForm.Activate();
            _historyForm.FocusSearch();
        }

        private void OnItemSelected(string text)
        {
            _clipboardMonitor.SkipNextUpdate();
            try
            {
                Clipboard.SetText(text);
            }
            catch { }
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused)
            {
                _trayIcon.Text = "ClipLite (已暂停)";
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
            if (MessageBox.Show("确定清空所有剪贴板历史？", "ClipLite",
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

        private void ExitApp()
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _hotkeyManager.Unregister(_clipboardMonitor.WindowHandle);
            _clipboardMonitor.Dispose();
            _historyForm.Dispose();
            if (_appIcon != null)
            {
                _appIcon.Dispose();
            }
            Application.Exit();
        }

        protected override void ExitThreadCore()
        {
            if (_trayIcon != null) _trayIcon.Visible = false;
            base.ExitThreadCore();
        }
    }
}

