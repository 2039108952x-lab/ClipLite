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
    /// Async thumbnail cache with LRU eviction. The worker owns the cached bitmaps;
    /// callers always receive a clone so eviction cannot dispose an image mid-paint.
    /// </summary>
    public class ThumbnailCache : IDisposable
    {
        private const int MaxCacheCount = 120;
        private const int ThumbnailSize = 64;

        private readonly object _sync = new object();
        private readonly Dictionary<string, Image> _cache = new Dictionary<string, Image>();
        private readonly LinkedList<string> _lru = new LinkedList<string>();
        private readonly Queue<ThumbnailRequest> _pending = new Queue<ThumbnailRequest>();
        private readonly HashSet<string> _pendingKeys = new HashSet<string>();
        private readonly Thread _worker;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private volatile bool _running = true;
        private readonly float _dpiScale = 1.0f;

        public ThumbnailCache()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    _dpiScale = g.DpiX / 96.0f;
            }
            catch { _dpiScale = 1.0f; }

            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Start();
        }

        /// <summary>Request a thumbnail; callback runs on the UI thread with a caller-owned image.</summary>
        public void GetThumbnail(string key, SafeStorage storage, Control invoker,
            Action<string, Image> callback)
        {
            if (string.IsNullOrEmpty(key) || storage == null || callback == null) return;

            Image copy = null;
            lock (_sync)
            {
                Image cached;
                if (_cache.TryGetValue(key, out cached))
                {
                    TouchLruLocked(key);
                    try { copy = new Bitmap(cached); }
                    catch { copy = null; }
                }
                else if (!_pendingKeys.Contains(key))
                {
                    _pendingKeys.Add(key);
                    _pending.Enqueue(new ThumbnailRequest
                    {
                        Key = key,
                        Storage = storage,
                        Invoker = invoker,
                        Callback = callback
                    });
                }
            }

            if (copy != null)
            {
                Deliver(invoker, key, callback, copy);
                return;
            }
            _signal.Set();
        }

        public bool TryGetFromCache(string key, out Image image)
        {
            image = null;
            if (string.IsNullOrEmpty(key)) return false;

            lock (_sync)
            {
                Image cached;
                if (!_cache.TryGetValue(key, out cached)) return false;
                TouchLruLocked(key);
                try
                {
                    image = new Bitmap(cached);
                    return true;
                }
                catch
                {
                    image = null;
                    return false;
                }
            }
        }

        public static Image GenerateThumbnailFromFile(string filePath, int maxSize)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
            try
            {
                using (var img = Image.FromFile(filePath))
                    return ScaleDown(img, maxSize);
            }
            catch { return null; }
        }

        public static Image GenerateThumbnailFromBytes(byte[] data, int maxSize)
        {
            if (data == null) return null;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms))
                    return ScaleDown(img, maxSize);
            }
            catch { return null; }
        }

        /// <summary>Downscaled PNG used only to hash an image without keeping a full copy.</summary>
        public static byte[] GetImageHashData(Image img)
        {
            if (img == null) return null;
            using (var thumb = ScaleDown(img, 32))
            {
                if (thumb == null) return null;
                using (var ms = new MemoryStream())
                {
                    thumb.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        public void Clear()
        {
            lock (_sync)
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
            if (string.IsNullOrEmpty(key)) return;
            lock (_sync)
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

        private void TouchLruLocked(string key)
        {
            _lru.Remove(key);
            _lru.AddFirst(key);
        }

        private bool AddToCacheLocked(string key, Image thumbnail)
        {
            if (_cache.ContainsKey(key)) return false;

            if (_cache.Count >= MaxCacheCount && _lru.Last != null)
            {
                string last = _lru.Last.Value;
                _lru.RemoveLast();
                Image old;
                if (_cache.TryGetValue(last, out old))
                {
                    try { old.Dispose(); } catch { }
                    _cache.Remove(last);
                }
            }

            _cache[key] = thumbnail;
            _lru.AddFirst(key);
            return true;
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                try { _signal.WaitOne(1000); }
                catch (ObjectDisposedException) { break; }

                while (_running)
                {
                    ThumbnailRequest request = null;
                    lock (_sync)
                    {
                        if (_pending.Count > 0)
                            request = _pending.Dequeue();
                    }
                    if (request == null) break;

                    Image generated = null;
                    try
                    {
                        byte[] assetData = request.Storage.LoadAsset(request.Key);
                        if (assetData != null)
                            generated = GenerateThumbnailFromBytes(assetData,
                                Math.Max(32, (int)(ThumbnailSize * _dpiScale)));
                    }
                    catch { generated = null; }

                    Image delivery = null;
                    bool stored = false;
                    lock (_sync)
                    {
                        if (generated != null)
                        {
                            stored = AddToCacheLocked(request.Key, generated);
                            Image cached;
                            if (_cache.TryGetValue(request.Key, out cached))
                            {
                                try { delivery = new Bitmap(cached); }
                                catch { delivery = null; }
                            }
                        }
                        _pendingKeys.Remove(request.Key);
                    }

                    if (generated != null && !stored)
                        generated.Dispose();

                    if (delivery != null)
                        Deliver(request.Invoker, request.Key, request.Callback, delivery);
                }
            }
        }

        private static void Deliver(Control invoker, string key, Action<string, Image> callback,
            Image image)
        {
            if (image == null) return;

            if (invoker == null || invoker.IsDisposed || !invoker.IsHandleCreated)
            {
                image.Dispose();
                return;
            }

            try
            {
                invoker.BeginInvoke(new Action(delegate
                {
                    if (invoker.IsDisposed)
                    {
                        image.Dispose();
                        return;
                    }
                    try { callback(key, image); }
                    catch { /* The callback takes ownership; never dispose after it ran. */ }
                }));
            }
            catch
            {
                image.Dispose();
            }
        }

        private static Bitmap ScaleDown(Image img, int maxSize)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0) return null;
            if (maxSize < 1) maxSize = 1;

            int width;
            int height;
            if (img.Width > img.Height)
            {
                width = maxSize;
                height = (int)(img.Height * ((float)maxSize / img.Width));
            }
            else
            {
                height = maxSize;
                width = (int)(img.Width * ((float)maxSize / img.Height));
            }
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            var thumb = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(img, 0, 0, width, height);
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
            if (_worker == null || !_worker.IsAlive)
                _signal.Dispose();
        }
    }
}
