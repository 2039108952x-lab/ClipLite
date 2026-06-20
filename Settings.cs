using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClipLite
{
    public class ClipLiteSettings
    {
        public List<string> ExcludedApps { get; set; }
        public bool ShowFileDetails { get; set; }
        public bool AutoStart { get; set; }
        public string CaptureMode { get; set; }
        public uint HotkeyModifiers { get; set; }
        public uint HotkeyKey { get; set; }
        public bool EnableEncryption { get; set; }
        public string EncryptionKey { get; set; }   // Base64, empty = auto-gen on enable
        public int MaxEntries { get; set; }
        public long MaxTotalSize { get; set; }
        public int Version { get; set; }

        private static readonly string _settingsPath = Path.Combine(
            Path.GetDirectoryName(typeof(Program).Assembly.Location),
            "cliplite_settings.json"
        );

        public ClipLiteSettings()
        {
            ExcludedApps = new List<string>();
            EnableEncryption = false;
            EncryptionKey = "";
            MaxEntries = 500;
            MaxTotalSize = 50L * 1024 * 1024;
            Version = 2;
        }

        public static ClipLiteSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return new ClipLiteSettings();
                string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                return Deserialize(json);
            }
            catch
            {
                return new ClipLiteSettings();
            }
        }

        public void Save()
        {
            try
            {
                string json = Serialize();
                File.WriteAllText(_settingsPath, json, Encoding.UTF8);
            }
            catch { }
        }

        public string EnsureEncryptionKey()
        {
            if (string.IsNullOrEmpty(EncryptionKey))
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.GenerateKey();
                    EncryptionKey = Convert.ToBase64String(aes.Key);
                }
                Save();
            }
            return EncryptionKey;
        }

        private string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"version\":").Append(Version);
            sb.Append(",\"maxEntries\":").Append(MaxEntries);
            sb.Append(",\"maxTotalSize\":").Append(MaxTotalSize);
            
            sb.Append(",\"showFileDetails\":").Append(ShowFileDetails ? "true" : "false");
            sb.Append(",\"captureMode\":\"").Append(CaptureMode ?? "full").Append("\"");
            sb.Append(",\"hotkeyModifiers\":").Append(HotkeyModifiers);
            sb.Append(",\"hotkeyKey\":").Append(HotkeyKey);
            sb.Append(",\"enableEncryption\":").Append(EnableEncryption ? "true" : "false");
            sb.Append(",\"encryptionKey\":\"").Append(SafeStorage.JsonEscape(EncryptionKey ?? "")).Append("\"");
            sb.Append(",\"excludedApps\":[");
            for (int i = 0; i < ExcludedApps.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(SafeStorage.JsonEscape(ExcludedApps[i])).Append("\"");
            }
            sb.Append("]");
            sb.Append("}");
            return sb.ToString();
        }

        private static ClipLiteSettings Deserialize(string json)
        {
            var s = new ClipLiteSettings();
            s.MaxEntries = (int)SafeStorage.ExtractLong(json, "maxEntries");
            if (s.MaxEntries <= 0) s.MaxEntries = 500;

            s.MaxTotalSize = SafeStorage.ExtractLong(json, "maxTotalSize");
            if (s.MaxTotalSize <= 0) s.MaxTotalSize = 50L * 1024 * 1024;

            s.Version = (int)SafeStorage.ExtractLong(json, "version");
            s.ShowFileDetails = SafeStorage.ExtractRaw(json, "showFileDetails") != "false";
            s.AutoStart = SafeStorage.ExtractRaw(json, "autoStart") != "false";
            s.CaptureMode = SafeStorage.ExtractStr(json, "captureMode") ?? "full";
            s.HotkeyModifiers = (uint)SafeStorage.ExtractLong(json, "hotkeyModifiers");
            s.HotkeyKey = (uint)SafeStorage.ExtractLong(json, "hotkeyKey");
            s.EnableEncryption = SafeStorage.ExtractRaw(json, "enableEncryption") == "true";
            s.EncryptionKey = SafeStorage.ExtractStr(json, "encryptionKey") ?? "";

            // Parse excluded apps array
            string appsRaw = SafeStorage.ExtractRaw(json, "excludedApps");
            if (!string.IsNullOrEmpty(appsRaw) && appsRaw.StartsWith("["))
            {
                string inner = appsRaw.Substring(1, appsRaw.Length - 2);
                int start = -1;
                for (int i = 0; i < inner.Length; i++)
                {
                    if (inner[i] == '"')
                    {
                        if (start == -1) start = i + 1;
                        else
                        {
                            s.ExcludedApps.Add(inner.Substring(start, i - start));
                            start = -1;
                        }
                    }
                }
            }
            return s;
        }
    }
}


