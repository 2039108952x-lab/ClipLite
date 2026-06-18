using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;

namespace ClipLite
{
    /// <summary>
    /// Message-driven clipboard monitor using AddClipboardFormatListener (zero CPU polling).
    /// Supports multi-format detection: text, image, HTML, file list, RTF.
    /// Includes 200ms debounce and self-write guard.
    /// </summary>
    public class ClipboardMonitor : NativeWindow, IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll")]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public delegate void ClipboardDataHandler(ClipboardFormatFlags formats, string text, Image image, string[] files, string rtf, string html, byte[] audio);

        /// <summary>
        /// Fired after debounce, with all extracted clipboard data.
        /// All format-specific parameters may be null if not available.
        /// </summary>
        public event ClipboardDataHandler ClipboardDataAvailable;

        private HashSet<string> _knownHashes = new HashSet<string>();
        private bool _initialized;
        private bool _skipNext;

        // Self-write guard
        private DateTime _lastSelfWriteTime = DateTime.MinValue;
        private readonly object _writeLock = new object();

        // Debounce
        private Timer _debounceTimer;
        private DateTime _lastChangeTime = DateTime.MinValue;

        public ClipboardMonitor()
        {
            _debounceTimer = new Timer { Interval = 200 };
            _debounceTimer.Tick += OnDebounceElapsed;
        }

        public void EnsureHandle()
        {
            if (_initialized) return;
            CreateHandle(new CreateParams());
            AddClipboardFormatListener(Handle);
            _initialized = true;
        }

        public IntPtr WindowHandle { get { return _initialized ? Handle : IntPtr.Zero; } }

        public List<string> ExcludedApps { get; set; }
        public string CaptureMode { get; set; }
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

        public void AddHash(string hash)
        {
            if (!string.IsNullOrEmpty(hash))
                _knownHashes.Add(hash);
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

        public string ComputeBlobHash(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }

        // ── Self-write helpers ──

        public void CopyText(string text)
        {
            lock (_writeLock)
            {
                _lastSelfWriteTime = DateTime.Now;
                try { Clipboard.SetText(text); } catch { }
            }
        }

        public void CopyImage(Image image)
        {
            lock (_writeLock)
            {
                _lastSelfWriteTime = DateTime.Now;
                try { Clipboard.SetImage(image); } catch { }
            }
        }

        public void CopyFilePaths(string[] paths)
        {
            lock (_writeLock)
            {
                _lastSelfWriteTime = DateTime.Now;
                try
                {
                    var col = new System.Collections.Specialized.StringCollection();
                    col.AddRange(paths);
                    Clipboard.SetFileDropList(col);
                }
                catch { }
            }
        }

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

        public event Action<Message> WindowMessageReceived;


        private bool IsExcludedApp()
        {
            if (ExcludedApps == null || ExcludedApps.Count == 0) return false;
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                using (var proc = Process.GetProcessById((int)pid))
                {
                    string name = proc.ProcessName.ToLower() + ".exe";
                    return ExcludedApps.Contains(name);
                }
            }
            catch { return false; }
        }
        private void OnClipboardUpdate()
        {
            if (_skipNext)
            {
                _skipNext = false;
                return;
            }

            // Self-write guard: skip if we just set the clipboard
            lock (_writeLock)
            {
                if ((DateTime.Now - _lastSelfWriteTime).TotalMilliseconds < 300)
                    return;
            }

            // Restart debounce timer
            _lastChangeTime = DateTime.Now;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void OnDebounceElapsed(object sender, EventArgs e)
        {
            _debounceTimer.Stop();

            // Verify enough time has passed
            if ((DateTime.Now - _lastChangeTime).TotalMilliseconds < 180)
            {
                _debounceTimer.Start();
                return;
            }

            ProcessClipboard();
        }

        private void ProcessClipboard()
        {
            // Exclusion check
            if (IsExcludedApp()) return;
            string text = null;
            Image image = null;
            string[] files = null;
            string rtf = null;
            string html = null;
            ClipboardFormatFlags formats = ClipboardFormatFlags.None;

            // Retry loop for clipboard access
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    IDataObject data = Clipboard.GetDataObject();
                    if (data == null) return;

                    // Detect formats
                    try { if (Clipboard.ContainsText()) { formats |= ClipboardFormatFlags.Text; text = Clipboard.GetText(); } } catch { }

            // ── Non-text format detection ──
            if (CaptureMode != "text_only")
            {
                    if (data.GetDataPresent(DataFormats.Rtf))
                    {
                        formats |= ClipboardFormatFlags.RichText;
                        rtf = (string)data.GetData(DataFormats.Rtf);
                        // Validate RTF
                        if (rtf != null && !rtf.StartsWith("{\\\rtf"))
                            rtf = null;
                    }

                    if (data.GetDataPresent(DataFormats.Html))
                    {
                        formats |= ClipboardFormatFlags.Html;
                        html = (string)data.GetData(DataFormats.Html);
                    }

                    if (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib))
                    {
                        formats |= ClipboardFormatFlags.Image;
                        try
                        {
                            // Prefer DIB for alpha preservation
                            if (data.GetDataPresent(DataFormats.Dib))
                            {
                                var dibStream = (MemoryStream)data.GetData(DataFormats.Dib);
                                if (dibStream != null)
                                    image = DibToBitmap(dibStream.ToArray());
                            }
                            if (image == null && data.GetDataPresent(DataFormats.Bitmap))
                            {
                                image = (Image)data.GetData(DataFormats.Bitmap);
                            }
                        }
                        catch
                        {
                            image = null;
                        }
                    var audioStream = data.GetData(DataFormats.WaveAudio) as System.IO.MemoryStream;
                }

                try { var cff = Clipboard.GetFileDropList(); if (cff != null && cff.Count > 0) { formats |= ClipboardFormatFlags.FileList; files = new string[cff.Count]; cff.CopyTo(files, 0); } } catch { }

            }
                    break; // Success
                }
                catch (ExternalException)
                {
                    if (retry < 2) System.Threading.Thread.Sleep(10);
                    else return; // Give up silently
                }
                catch
                {
                    return;
                }
            }

            // Nothing to process
            if (formats == ClipboardFormatFlags.None) return;

            // Fire event with all data
            if (ClipboardDataAvailable != null)
            {
                ClipboardDataAvailable(formats, text, image, files, rtf, html, null);
            }

            // Clean up image
            if (image != null)
            {
                // Don't dispose — the event handler will take ownership or copy it
            }
        }

        /// <summary>
        /// Convert DIB raw bytes (BITMAPINFOHEADER + pixels) to a Bitmap,
        /// preserving alpha channel (32-bit ARGB).
        /// </summary>
        private static Bitmap DibToBitmap(byte[] dibData)
        {
            if (dibData == null || dibData.Length < 40) return null;

            try
            {
                // Read BITMAPINFOHEADER
                int biSize = BitConverter.ToInt32(dibData, 0);
                int width = BitConverter.ToInt32(dibData, 4);
                int height = BitConverter.ToInt32(dibData, 8);
                short planes = BitConverter.ToInt16(dibData, 12);
                short bpp = BitConverter.ToInt16(dibData, 14);
                int compression = BitConverter.ToInt32(dibData, 16);

                int absHeight = (height < 0) ? -height : height;
                bool topDown = height < 0;

                // Compute palette size
                int paletteSize = 0;
                if (bpp <= 8)
                {
                    int colorCount = 1 << bpp;
                    int clrUsed = BitConverter.ToInt32(dibData, 32);
                    paletteSize = (clrUsed > 0) ? clrUsed * 4 : colorCount * 4;
                }

                // Pixel data starts after header + palette
                int pixelOffset = biSize + paletteSize;
                if (pixelOffset >= dibData.Length) return null;

                // Build BITMAPFILEHEADER + DIB = complete BMP
                int bmpHeaderSize = 14;
                int fileSize = bmpHeaderSize + dibData.Length;

                using (var ms = new MemoryStream(fileSize))
                {
                    // BITMAPFILEHEADER
                    ms.WriteByte((byte)'B'); ms.WriteByte((byte)'M');
                    ms.Write(BitConverter.GetBytes(fileSize), 0, 4);
                    ms.Write(BitConverter.GetBytes(0), 0, 4); // reserved
                    ms.Write(BitConverter.GetBytes(bmpHeaderSize + biSize + paletteSize), 0, 4); // pixel offset

                    // BITMAPINFOHEADER + palette + pixels
                    ms.Write(dibData, 0, dibData.Length);

                    ms.Position = 0;
                    var bmp = new Bitmap(ms);

                    if (topDown)
                    {
                        bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
                    }

                    return bmp;
                }
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                _debounceTimer.Dispose();
                RemoveClipboardFormatListener(Handle);
                _initialized = false;
                DestroyHandle();
            }
            catch { }
        }
    }

    // ── HotkeyManager (unchanged) ──

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
                    HotkeyPressed();
                return true;
            }
            return false;
        }
    }
}




