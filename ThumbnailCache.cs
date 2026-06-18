using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ClipLite
{
    /// <summary>
    /// Async thumbnail cache with LRU eviction.
    /// Generates thumbnails on a background thread, returns via callback.
    /// </summary>
    public class ThumbnailCache : IDisposable
    {
        private const int MaxCacheCount = 100;
        private const int ThumbnailSize = 64;

        private Dictionary<string, Image> _cache = new Dictionary<string, Image>();
        private LinkedList<string> _lru = new LinkedList<string>();
        private Queue<ThumbnailRequest> _pending = new Queue<ThumbnailRequest>();
        private Thread _worker;
        private AutoResetEvent _signal = new AutoResetEvent(false);
        private volatile bool _running = true;
        private float _dpiScale = 1.0f;

        public ThumbnailCache()
        {
            // Detect system DPI
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    _dpiScale = g.DpiX / 96.0f;
                }
            }
            catch { _dpiScale = 1.0f; }

            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Start();
        }

        /// <summary>
        /// Request async thumbnail generation. Callback is invoked on the UI thread.
        /// </summary>
        public void GetThumbnail(string key, SafeStorage storage, Control invoker, Action<string, Image> callback)
        {
            // Check cache
            Image cached;
            if (_cache.TryGetValue(key, out cached))
            {
                TouchLru(key);
                if (invoker != null && invoker.IsHandleCreated)
                    invoker.BeginInvoke(new Action(() => callback(key, cached)));
                return;
            }

            // Enqueue for background generation
            lock (_pending)
            {
                _pending.Enqueue(new ThumbnailRequest
                {
                    Key = key,
                    Storage = storage,
                    Invoker = invoker,
                    Callback = callback
                });
            }
            _signal.Set();
        }

        public bool TryGetFromCache(string key, out Image image)
        {
            if (_cache.TryGetValue(key, out image))
            {
                TouchLru(key);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Generate a thumbnail synchronously (for initial list rendering).
        /// </summary>
        public static Image GenerateThumbnailFromFile(string filePath, int maxSize = 64)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
            try
            {
                using (var img = Image.FromFile(filePath))
                {
                    return ScaleDown(img, maxSize);
                }
            }
            catch { return null; }
        }

        public static Image GenerateThumbnailFromBytes(byte[] data, int maxSize = 64)
        {
            if (data == null) return null;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms))
                {
                    return ScaleDown(img, maxSize);
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Get pixel data for hash computation without creating full-size Bitmap copy.
        /// </summary>
        public static byte[] GetImageHashData(Image img)
        {
            if (img == null) return null;
            using (var thumb = ScaleDown(img, 32))
            {
                using (var ms = new MemoryStream())
                {
                    thumb.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        public void Clear()
        {
            lock (_cache)
            {
                foreach (var img in _cache.Values)
                {
                    try { img.Dispose(); } catch { }
                }
                _cache.Clear();
                _lru.Clear();
            }
        }

        public void Remove(string key)
        {
            lock (_cache)
            {
                Image img;
                if (_cache.TryGetValue(key, out img))
                {
                    try { img.Dispose(); } catch { }
                    _cache.Remove(key);
                    _lru.Remove(key);
                }
            }
        }

        // ── Private ──

        private void TouchLru(string key)
        {
            lock (_cache)
            {
                _lru.Remove(key);
                _lru.AddFirst(key);
            }
        }

        private void AddToCache(string key, Image thumbnail)
        {
            lock (_cache)
            {
                if (_cache.ContainsKey(key)) return;

                // Evict if full
                if (_cache.Count >= MaxCacheCount)
                {
                    string last = _lru.Last.Value;
                    _lru.RemoveLast();
                    Image oldImg;
                    if (_cache.TryGetValue(last, out oldImg))
                    {
                        try { oldImg.Dispose(); } catch { }
                        _cache.Remove(last);
                    }
                }

                _cache[key] = thumbnail;
                _lru.AddFirst(key);
            }
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                _signal.WaitOne(1000);

                ThumbnailRequest req = null;
                lock (_pending)
                {
                    if (_pending.Count > 0)
                        req = _pending.Dequeue();
                }

                if (req == null) continue;

                Image thumb = null;
                try
                {
                    byte[] assetData = req.Storage.LoadAsset(req.Key);
                    if (assetData != null)
                    {
                        thumb = GenerateThumbnailFromBytes(assetData, (int)(ThumbnailSize * _dpiScale));
                        if (thumb != null)
                            AddToCache(req.Key, thumb);
                    }
                }
                catch { }

                // Deliver result on UI thread
                if (req.Invoker != null && req.Invoker.IsHandleCreated)
                {
                    req.Invoker.BeginInvoke(new Action(() =>
                    {
                        req.Callback(req.Key, thumb);
                    }));
                }
            }
        }

        private static Image ScaleDown(Image img, int maxSize)
        {
            if (img == null) return null;

            int w, h;
            if (img.Width > img.Height)
            {
                w = maxSize;
                h = (int)(img.Height * ((float)maxSize / img.Width));
            }
            else
            {
                h = maxSize;
                w = (int)(img.Width * ((float)maxSize / img.Height));
            }
            if (w < 1) w = 1;
            if (h < 1) h = 1;

            var thumb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(img, 0, 0, w, h);
            }
            return thumb;
        }

        private class ThumbnailRequest
        {
            public string Key;
            public SafeStorage Storage;
            public Control Invoker;
            public Action<string, Image> Callback;
        }

        public void Dispose()
        {
            _running = false;
            _signal.Set();
            if (_worker != null && _worker.IsAlive)
                _worker.Join(500);
            Clear();
            _signal.Dispose();
        }
    }
}



