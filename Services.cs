using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace ClipLite
{
    /// <summary>
    /// Message-driven clipboard monitor using AddClipboardFormatListener (zero CPU polling).
    /// Also acts as the hidden message window for global hotkey dispatch.
    /// </summary>
    public class ClipboardMonitor : NativeWindow, IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll")]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        public event Action<string> ClipboardTextChanged;

        private HashSet<string> _knownHashes = new HashSet<string>();
        private string _lastText = "";
        private bool _initialized;
        private bool _skipNext;

        /// <summary>
        /// Initialize the hidden message window and register for clipboard notifications.
        /// </summary>
        public void EnsureHandle()
        {
            if (_initialized) return;
            CreateHandle(new CreateParams());
            AddClipboardFormatListener(Handle);
            _initialized = true;
        }

        public IntPtr WindowHandle
        {
            get { return _initialized ? Handle : IntPtr.Zero; }
        }

        public void SkipNextUpdate()
        {
            _skipNext = true;
        }

        public void SetKnownHashes(HashSet<string> hashes)
        {
            _knownHashes = hashes;
        }

        public bool IsKnownHash(string hash)
        {
            return _knownHashes.Contains(hash);
        }

        public string ComputeHash(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }

        /// <summary>
        /// Fired for unhandled Windows messages (used by HotkeyManager hook)
        /// </summary>
        public event Action<Message> WindowMessageReceived;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardUpdate();
            }
            else
            {
                if (WindowMessageReceived != null)
                {
                    WindowMessageReceived(m);
                }
            }
            base.WndProc(ref m);
        }

        private void OnClipboardUpdate()
        {
            if (_skipNext)
            {
                _skipNext = false;
                _lastText = GetClipboardText();
                return;
            }

            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (text != _lastText && !string.IsNullOrWhiteSpace(text))
                    {
                        _lastText = text;
                        if (ClipboardTextChanged != null)
                        {
                            ClipboardTextChanged(text);
                        }
                    }
                }
            }
            catch { }
        }

        private string GetClipboardText()
        {
            try
            {
                if (Clipboard.ContainsText()) return Clipboard.GetText();
            }
            catch { }
            return "";
        }

        public void Dispose()
        {
            try
            {
                RemoveClipboardFormatListener(Handle);
                _initialized = false;
                DestroyHandle();
            }
            catch { }
        }
    }

    public class HotkeyManager
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9001;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_V = 0x56;

        public event Action HotkeyPressed;

        public bool Register(IntPtr hWnd)
        {
            return RegisterHotKey(hWnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_V);
        }

        public void Unregister(IntPtr hWnd)
        {
            if (hWnd != IntPtr.Zero)
                UnregisterHotKey(hWnd, HOTKEY_ID);
        }

        public bool HandleMessage(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID)
            {
                if (HotkeyPressed != null)
                {
                    HotkeyPressed();
                }
                return true;
            }
            return false;
        }
    }
}
