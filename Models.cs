using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClipLite
{
    [Flags]
    public enum ClipboardFormatFlags
    {
        None = 0,
        Text = 1,
        RichText = 2,
        Html = 4,
        Image = 8,
        FileList = 16,
        Audio = 32,
        Vector = 64
    }

    public class ClipboardEntry
    {
        // Core fields (always present)
        public string Id { get; set; }           // SHA1 hex of primary content
        public string Type { get; set; }         // "text" | "image" | "filelist" | "richtext" | "html"
        public string Text { get; set; }         // Plain text fallback (always populated)
        public DateTime Timestamp { get; set; }
        public bool IsPinned { get; set; }
        public long Size { get; set; }           // Storage bytes occupied

        // Image specifics
        public string ImageFile { get; set; }    // "{hash}.png" in assets/
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }

        // File list specifics (paths joined with \0)
        public string FilePathsRaw { get; set; } // \0-separated

        // Rich Text
        public string RtfData { get; set; }      // Inline if <= 16KB
        public string RtfFile { get; set; }      // Asset file if > 16KB
        public string RtfPreview { get; set; }   // Plain text extracted

        // HTML
        public string HtmlData { get; set; }     // Full CF_HTML with format header
        public string HtmlFile { get; set; }
        public string AudioFile { get; set; }

        // ------ Computed properties ------

        public ClipboardEntry()
        {
            Id = Guid.NewGuid().ToString("N");
            Timestamp = DateTime.Now;
            Type = "text";
        }

        public static bool ShowFileDetails { get; set; }

        public string[] GetFilePaths()
        {
            if (string.IsNullOrEmpty(FilePathsRaw)) return new string[0];
            return FilePathsRaw.Split('\0');
        }

        public void SetFilePaths(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                FilePathsRaw = null;
            else
                FilePathsRaw = string.Join("\0", paths);
        }

        public string PreviewText
        {
            get
            {
                if (!string.IsNullOrEmpty(Text))
                {
                    string s = Text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                    return s.Length > 100 ? s.Substring(0, 100) + "..." : s;
                }
                switch (Type)
                {
                    case "image":
                        string dims = (ImageWidth > 0) ? " (" + ImageWidth + "x" + ImageHeight + ")" : "";
                        return "[图片]" + dims;
                    case "filelist":
                        var paths = GetFilePaths();
                        if (paths.Length == 0) return "[文件]";
                        string first = Path.GetFileName(paths[0]);
                        return paths.Length > 1
                            ? "[" + paths.Length + " 个文件] " + first + " ..."
                            : "[文件] " + first;
                    case "richtext":
                        if (!string.IsNullOrEmpty(RtfPreview))
                        {
                            string s = RtfPreview.Replace("\r\n", " ").Replace("\n", " ");
                            return s.Length > 100 ? s.Substring(0, 100) + "..." : s;
                        }
                        return "[富文本]";
                    case "audio": return "[音频]";
                    case "html":
                        if (!string.IsNullOrEmpty(Text))
                        {
                            string s = Text.Replace("\r\n", " ").Replace("\n", " ");
                            return s.Length > 100 ? s.Substring(0, 100) + "..." : s;
                        }
                        return "[HTML]";
                    default:
                        return "";
                }
            }
        }

        public string TypeDisplay
        {
            get
            {
                switch (Type)
                {
                    case "text": return "文本";
                    case "image": return "图片";
                    case "filelist": return "文件";
                    case "richtext": return "富文本";
                    case "audio": return "音频";
                    case "html": return "HTML";
                    default: return "未知";
                }
            }
        }

        public string TimeDisplay
        {
            get
            {
                var diff = DateTime.Now - Timestamp;
                if (diff.TotalMinutes < 1) return "刚刚";
                if (diff.TotalHours < 1) return ((int)diff.TotalMinutes).ToString() + " 分钟前";
                if (diff.TotalDays < 1) return ((int)diff.TotalHours).ToString() + " 小时前";
                return Timestamp.ToString("MM/dd HH:mm");
            }
        }

        public string TypeIcon
        {
            get
            {
                switch (Type)
                {
                    case "text": return "文";
                    case "image": return "图";
                    case "filelist": return "件";
                    case "richtext": return "富";
                    case "audio": return "音";
                    case "html": return "H";
                    default: return "?";
                }
            }
        }

        public static string ComputeSha1(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower().Substring(0, 40);
            }
        }

        public static string ComputeSha1(byte[] data)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLower().Substring(0, 40);
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  SafeStorage — JSON Lines format with atomic writes, 
    //  rolling backups, asset dedup, and V1 migration
    // ──────────────────────────────────────────────────────────

    public class SafeStorage
    {
        public const int MaxEntries = 500;
        public const long MaxTotalSize = 50L * 1024 * 1024;    // 50 MB
        public const long MaxFileSize = 5L * 1024 * 1024;      // 5 MB per asset
        public const int InlineThreshold = 16 * 1024;          // 16 KB inline
        public const int MaxBackups = 3;

        private readonly string _exeDir;
        private readonly string _dataDir;
        private readonly string _assetsDir;
        private readonly string _indexFile;

        public SafeStorage()
        {
            _exeDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            _dataDir = Path.Combine(_exeDir, "cliplite_data");
            _assetsDir = Path.Combine(_dataDir, "assets");
            _indexFile = Path.Combine(_dataDir, "index.jsonl");

            Directory.CreateDirectory(_assetsDir);
        }

        public string DataDir { get { return _dataDir; } }
        public string AssetsDir { get { return _assetsDir; } }
        public string EncryptionKey { get; set; }

        // ── Load ──

        public List<ClipboardEntry> Load()
        {
            // Try primary index, then backups
            for (int attempt = 0; attempt <= MaxBackups; attempt++)
            {
                string path = (attempt == 0) ? _indexFile
                            : IndexBakPath(attempt);
                if (File.Exists(path))
                {
                    try
                    {
                        string json = File.ReadAllText(path, Encoding.UTF8);
                        return DeserializeLines(json);
                    }
                    catch
                    {
                        // Corrupted — try next backup
                        continue;
                    }
                }
            }
            return new List<ClipboardEntry>();
        }

        // ── Save (full rewrite, atomic) ──

        public void Save(List<ClipboardEntry> entries)
        {
            string json = SerializeLines(entries);
            string tmpPath = _indexFile + ".tmp";

            byte[] saveData = Encoding.UTF8.GetBytes(json);
            saveData = EncryptBytes(saveData);
            File.WriteAllBytes(tmpPath, saveData);

            // Rolling backup
            for (int i = MaxBackups - 1; i >= 0; i--)
            {
                string bakPath = IndexBakPath(i + 1);
                string srcPath = (i == 0) ? _indexFile : IndexBakPath(i);
                if (File.Exists(srcPath))
                {
                    if (i == MaxBackups - 1)
                        File.Delete(bakPath);
                    else
                        if (File.Exists(bakPath)) File.Delete(bakPath); File.Move(srcPath, bakPath);
                }
            }

            // Atomic replace
            if (File.Exists(_indexFile)) File.Delete(_indexFile); File.Move(tmpPath, _indexFile);
        }

        // ── Append one entry (fast path for new clipboard items) ──

        public void AppendEntry(ClipboardEntry entry)
        {
            string line = SerializeEntry(entry);
            File.AppendAllText(_indexFile, line + "\n", Encoding.UTF8);
        }

        // ── Asset management ──

        public string SaveAsset(string hash, byte[] data, string extension)
        {
            if (data == null || data.Length > MaxFileSize) return null;
            string filename = hash + extension;  // e.g. "abc123.png"
            string path = Path.Combine(_assetsDir, filename);
            if (!File.Exists(path))
                File.WriteAllBytes(path, data);
            return filename;
        }

        public byte[] LoadAsset(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return null;
            string path = Path.Combine(_assetsDir, filename);
            if (!File.Exists(path)) return null;
            return File.ReadAllBytes(path);
        }

        public void DeleteAsset(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return;
            string path = Path.Combine(_assetsDir, filename);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // ── Orphan cleanup (call after startup load) ──

        public void CleanupOrphans(List<ClipboardEntry> entries)
        {
            var referenced = new HashSet<string>();
            foreach (var e in entries)
            {
                if (!string.IsNullOrEmpty(e.ImageFile)) referenced.Add(e.ImageFile);
                if (!string.IsNullOrEmpty(e.HtmlFile)) referenced.Add(e.HtmlFile);
                if (!string.IsNullOrEmpty(e.RtfFile)) referenced.Add(e.RtfFile);
                if (!string.IsNullOrEmpty(e.AudioFile)) referenced.Add(e.AudioFile);
            }
            if (Directory.Exists(_assetsDir))
            {
                foreach (string f in Directory.GetFiles(_assetsDir))
                {
                    string name = Path.GetFileName(f);
                    if (!referenced.Contains(name))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
        }

        // ── V1 → V2 migration ──

        public bool HasV1Data()
        {
            return File.Exists(Path.Combine(_exeDir, "cliplite_history.json"));
        }

        public List<ClipboardEntry> MigrateFromV1()
        {
            string v1Path = Path.Combine(_exeDir, "cliplite_history.json");
            if (!File.Exists(v1Path)) return null;

            try
            {
                // Backup V1
                string bak = v1Path + ".v1.bak";
                File.Copy(v1Path, bak, overwrite: true);

                // Read V1 (JSON array)
                string json = File.ReadAllText(v1Path, Encoding.UTF8);
                var oldStorage = new JsonStorage(v1Path);
                // We load using the old JsonStorage format
                var entries = oldStorage.Load();

                // Convert to V2 format
                foreach (var e in entries)
                {
                    e.Type = "text";
                    if (string.IsNullOrEmpty(e.Id))
                        e.Id = Guid.NewGuid().ToString("N");
                    e.Size = Encoding.UTF8.GetByteCount(e.Text ?? "");
                }

                // Save as V2
                Save(entries);

                // Keep V1 backup, rename original
                File.Move(v1Path, v1Path + ".migrated");

                return entries;
            }
            catch
            {
                return null;
            }
        }

        // ── Total storage size ──

        public long GetTotalSize()
        {
            long size = 0;
            if (Directory.Exists(_assetsDir))
            {
                foreach (string f in Directory.GetFiles(_assetsDir))
                {
                    try { size += new FileInfo(f).Length; } catch { }
                }
            }
            if (File.Exists(_indexFile))
            {
                try { size += new FileInfo(_indexFile).Length; } catch { }
            }
            return size;
        }

        // ── JSON Lines serialize ──

        private static string SerializeLines(List<ClipboardEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                sb.AppendLine(SerializeEntry(e));
            }
            return sb.ToString();
        }

        internal static string SerializeEntry(ClipboardEntry e)
        {
            var sb = new StringBuilder();
            sb.Append("{\"id\":\"").Append(JsonEscape(e.Id ?? "")).Append("\"");
            sb.Append(",\"type\":\"").Append(JsonEscape(e.Type ?? "text")).Append("\"");
            sb.Append(",\"text\":\"").Append(JsonEscape(e.Text ?? "")).Append("\"");
            sb.Append(",\"time\":\"").Append(e.Timestamp.ToString("O")).Append("\"");
            sb.Append(",\"pinned\":").Append(e.IsPinned ? "true" : "false");
            sb.Append(",\"size\":").Append(e.Size);

            if (!string.IsNullOrEmpty(e.ImageFile))
                sb.Append(",\"imageFile\":\"").Append(JsonEscape(e.ImageFile)).Append("\"");
            if (e.ImageWidth > 0)
                sb.Append(",\"imageWidth\":").Append(e.ImageWidth);
            if (e.ImageHeight > 0)
                sb.Append(",\"imageHeight\":").Append(e.ImageHeight);

            if (!string.IsNullOrEmpty(e.FilePathsRaw))
                sb.Append(",\"filePaths\":\"").Append(JsonEscape(e.FilePathsRaw)).Append("\"");

            if (!string.IsNullOrEmpty(e.RtfData))
                sb.Append(",\"rtfData\":\"").Append(JsonEscape(e.RtfData)).Append("\"");
            if (!string.IsNullOrEmpty(e.RtfFile))
                sb.Append(",\"rtfFile\":\"").Append(JsonEscape(e.RtfFile)).Append("\"");
            if (!string.IsNullOrEmpty(e.RtfPreview))
                sb.Append(",\"rtfPreview\":\"").Append(JsonEscape(e.RtfPreview)).Append("\"");

            if (!string.IsNullOrEmpty(e.HtmlData))
                sb.Append(",\"htmlData\":\"").Append(JsonEscape(e.HtmlData)).Append("\"");
            if (!string.IsNullOrEmpty(e.AudioFile))
                sb.Append(",\"audioFile\":\"").Append(JsonEscape(e.AudioFile)).Append("\"");
                if (!string.IsNullOrEmpty(e.HtmlFile))
                sb.Append(",\"htmlFile\":\"").Append(JsonEscape(e.HtmlFile)).Append("\"");

            sb.Append("}");
            return sb.ToString();
        }

        internal static List<ClipboardEntry> DeserializeLines(string json)
        {
            var list = new List<ClipboardEntry>();
            using (var reader = new StringReader(json))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    var entry = ParseEntry(line);
                    if (entry != null)
                        list.Add(entry);
                }
            }
            return list;
        }

        private static ClipboardEntry ParseEntry(string json)
        {
            try
            {
                DateTime t;
                var e = new ClipboardEntry
                {
                    Id = ExtractStr(json, "id") ?? Guid.NewGuid().ToString("N"),
                    Type = ExtractStr(json, "type") ?? "text",
                    Text = ExtractStr(json, "text") ?? "",
                    Timestamp = DateTime.TryParse(ExtractStr(json, "time"), out t) ? t : DateTime.Now,
                    IsPinned = ExtractRaw(json, "pinned") == "true",
                    Size = ExtractLong(json, "size"),
                    ImageFile = ExtractStr(json, "imageFile"),
                    ImageWidth = (int)ExtractLong(json, "imageWidth"),
                    ImageHeight = (int)ExtractLong(json, "imageHeight"),
                    FilePathsRaw = ExtractStr(json, "filePaths"),
                    RtfData = ExtractStr(json, "rtfData"),
                    RtfFile = ExtractStr(json, "rtfFile"),
                    RtfPreview = ExtractStr(json, "rtfPreview"),
                    HtmlData = ExtractStr(json, "htmlData"),
                    AudioFile = ExtractStr(json, "audioFile"),
                    HtmlFile = ExtractStr(json, "htmlFile")
                };
                return e;
            }
            catch
            {
                return null;
            }
        }

        private string IndexBakPath(int n)
        {
            // returns index.bak1.jsonl, index.bak2.jsonl, etc.
            return Path.Combine(
                _exeDir,
                "cliplite_data", "index.bak" + n + ".jsonl");
        }

        // ── JSON helpers ──

        internal static string ExtractStr(string json, string field)
        {
            string search = "\"" + field + "\":\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else sb.Append(n);
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        internal static string ExtractRaw(string json, string field)
        {
            string search = "\"" + field + "\":";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ',' || c == '}' || c == '\n' || char.IsWhiteSpace(c)) break;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        internal static long ExtractLong(string json, string field)
        {
            string raw = ExtractRaw(json, field);
            if (string.IsNullOrEmpty(raw)) return 0;
            long result;
            long.TryParse(raw, out result);
            return result;
        }

        internal static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }


        // ── Encryption ──

        private static readonly byte[] MagicEncrypt = Encoding.ASCII.GetBytes("ENCR");

        private byte[] EncryptBytes(byte[] plaintext)
        {
            if (string.IsNullOrEmpty(EncryptionKey)) return plaintext;
            try
            {
                byte[] key = Convert.FromBase64String(EncryptionKey);
                using (var aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Key = key;
                    aes.GenerateIV();
                    byte[] iv = aes.IV;
                    using (var transform = aes.CreateEncryptor())
                    using (var ms = new System.IO.MemoryStream())
                    {
                        ms.Write(MagicEncrypt, 0, MagicEncrypt.Length);
                        ms.Write(iv, 0, iv.Length);
                        using (var cs = new System.Security.Cryptography.CryptoStream(ms, transform, System.Security.Cryptography.CryptoStreamMode.Write))
                        {
                            cs.Write(plaintext, 0, plaintext.Length);
                        }
                        return ms.ToArray();
                    }
                }
            }
            catch { return plaintext; }
        }

        private byte[] DecryptBytes(byte[] data)
        {
            if (data == null || data.Length < 4 || string.IsNullOrEmpty(EncryptionKey)) return data;
            if (data[0] == MagicEncrypt[0] && data[1] == MagicEncrypt[1] && data[2] == MagicEncrypt[2] && data[3] == MagicEncrypt[3])
            {
                try
                {
                    byte[] key = Convert.FromBase64String(EncryptionKey);
                    using (var aes = System.Security.Cryptography.Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.Key = key;
                        byte[] iv = new byte[16];
                        Array.Copy(data, 4, iv, 0, 16);
                        aes.IV = iv;
                        using (var transform = aes.CreateDecryptor())
                        using (var ms = new System.IO.MemoryStream())
                        {
                            using (var cs = new System.Security.Cryptography.CryptoStream(ms, transform, System.Security.Cryptography.CryptoStreamMode.Write))
                            {
                                cs.Write(data, 20, data.Length - 20);
                            }
                            return ms.ToArray();
                        }
                    }
                }
                catch { return data; }
            }
            return data;
        }
    // ── V1 backward-compatible storage (kept for migration) ──
    }

    public class JsonStorage
    {
        private readonly string _filePath;

        public JsonStorage(string filePath)
        {
            _filePath = filePath;
        }

        public List<ClipboardEntry> Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new List<ClipboardEntry>();
                string json = File.ReadAllText(_filePath, Encoding.UTF8);
                if (json.TrimStart().StartsWith("["))
                    return DeserializeArray(json);
                // JSON Lines (V2) — reuse SafeStorage parser
                return SafeStorage.DeserializeLines(json);
            }
            catch
            {
                return new List<ClipboardEntry>();
            }
        }

        private static List<ClipboardEntry> DeserializeArray(string json)
        {
            var list = new List<ClipboardEntry>();
            json = json.Trim();
            if (json.Length < 2 || json[0] != '[' || json[json.Length - 1] != ']')
                return list;

            string inner = json.Substring(1, json.Length - 2);
            int depth = 0, objStart = -1;
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (c == '{') { if (depth == 0) objStart = i; depth++; }
                else if (c == '}') { depth--; if (depth == 0 && objStart >= 0) { var entry = ParseV1Entry(inner.Substring(objStart, i - objStart + 1)); if (entry != null) list.Add(entry); objStart = -1; } }
            }
            return list;
        }

        private static ClipboardEntry ParseV1Entry(string obj)
        {
            try
            {
            DateTime t;
                return new ClipboardEntry
                {
                    Id = SafeStorage.ExtractStr(obj, "id") ?? Guid.NewGuid().ToString("N"),
                    Text = SafeStorage.ExtractStr(obj, "text") ?? "",
                    Type = "text",
                    Timestamp = DateTime.TryParse(SafeStorage.ExtractStr(obj, "time"), out t) ? t : DateTime.Now,
                    IsPinned = SafeStorage.ExtractRaw(obj, "pinned") == "true"
                };
            }
            catch { return null; }
        }
    }
}






