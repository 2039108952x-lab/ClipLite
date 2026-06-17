using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClipLite
{
    public class ClipboardEntry
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsPinned { get; set; }

        public ClipboardEntry()
        {
            Id = Guid.NewGuid().ToString("N");
            Timestamp = DateTime.Now;
        }

        public ClipboardEntry(string text) : this()
        {
            Text = text;
        }

        public string PreviewText
        {
            get
            {
                if (string.IsNullOrEmpty(Text)) return "";
                string s = Text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                return s.Length > 100 ? s.Substring(0, 100) + "..." : s;
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
    }

    public class JsonStorage
    {
        private readonly string _filePath;
        public const int MaxEntries = 500;

        public JsonStorage(string filePath)
        {
            _filePath = filePath;
        }

        public JsonStorage()
        {
            _filePath = Path.Combine(
                Path.GetDirectoryName(typeof(Program).Assembly.Location),
                "cliplite_history.json"
            );
        }

        public List<ClipboardEntry> Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new List<ClipboardEntry>();
                string json = File.ReadAllText(_filePath, Encoding.UTF8);
                return Deserialize(json);
            }
            catch
            {
                return new List<ClipboardEntry>();
            }
        }

        public void Save(List<ClipboardEntry> entries)
        {
            try
            {
                string json = Serialize(entries);
                File.WriteAllText(_filePath, json, Encoding.UTF8);
            }
            catch { }
        }

        private static string Serialize(List<ClipboardEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = entries[i];
                sb.Append("{\"id\":\"").Append(JsonEscape(e.Id)).Append("\"");
                sb.Append(",\"text\":\"").Append(JsonEscape(e.Text)).Append("\"");
                sb.Append(",\"time\":\"").Append(e.Timestamp.ToString("O")).Append("\"");
                sb.Append(",\"pinned\":").Append(e.IsPinned ? "true" : "false");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static List<ClipboardEntry> Deserialize(string json)
        {
            var list = new List<ClipboardEntry>();
            json = json.Trim();
            if (json.Length < 2 || json[0] != '[' || json[json.Length - 1] != ']')
                return list;

            string inner = json.Substring(1, json.Length - 2);
            int depth = 0;
            int objStart = -1;
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        var entry = ParseEntry(inner.Substring(objStart, i - objStart + 1));
                        if (entry != null) list.Add(entry);
                        objStart = -1;
                    }
                }
            }
            return list;
        }

        private static ClipboardEntry ParseEntry(string obj)
        {
            try
            {
                var entry = new ClipboardEntry();
                entry.Id = ExtractStr(obj, "id");
                if (entry.Id == null) entry.Id = Guid.NewGuid().ToString("N");
                entry.Text = ExtractStr(obj, "text");
                if (entry.Text == null) entry.Text = "";
                string timeStr = ExtractStr(obj, "time");
                DateTime t;
                if (DateTime.TryParse(timeStr, out t))
                    entry.Timestamp = t;
                string pinned = ExtractRaw(obj, "pinned");
                entry.IsPinned = (pinned == "true");
                return entry;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractStr(string json, string field)
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

        private static string ExtractRaw(string json, string field)
        {
            string search = "\"" + field + "\":";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ',' || c == '}' || char.IsWhiteSpace(c)) break;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string JsonEscape(string s)
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
    }
}

