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
        public bool EnableEncryption { get; set; }
        public string EncryptionKey { get; set; }   // Base64, empty = auto-gen on enable
        public int MaxEntries { get; set; }
        public long MaxTotalSize { get; set; }
        public int Version { get; set; }

        // Win32 modifier mask (Alt=1, Ctrl=2, Shift=4) and virtual-key code.
        // Zero in either field disables the global hotkey.
        public int HotkeyModifiers { get; set; }
        public int HotkeyKey { get; set; }
        public bool ShowCopyToast { get; set; }
        public string ToastPosition { get; set; }   // "cursor" | "bottom"

        private static readonly string _settingsPath = Path.Combine(
            Path.GetDirectoryName(typeof(Program).Assembly.Location),
            "cliplite_settings.json"
        );

        public ClipLiteSettings()
        {
            ExcludedApps = new List<string>();
            ShowFileDetails = false;
            AutoStart = true;
            CaptureMode = "full";
            EnableEncryption = false;
            EncryptionKey = "";
            MaxEntries = 500;
            MaxTotalSize = 50L * 1024 * 1024;
            HotkeyModifiers = 2 | 4; // Ctrl + Shift
            HotkeyKey = 0x56;        // V
            ShowCopyToast = true;
            ToastPosition = "cursor";
            Version = 3;
        }

        /// <summary>SettingsForm edits a copy so cancel is side-effect free and
        /// Program can compare the previous encryption/hotkey values on save.</summary>
        public ClipLiteSettings Clone()
        {
            return new ClipLiteSettings
            {
                ExcludedApps = new List<string>(ExcludedApps ?? new List<string>()),
                ShowFileDetails = ShowFileDetails,
                AutoStart = AutoStart,
                CaptureMode = CaptureMode,
                EnableEncryption = EnableEncryption,
                EncryptionKey = EncryptionKey,
                MaxEntries = MaxEntries,
                MaxTotalSize = MaxTotalSize,
                Version = Version,
                HotkeyModifiers = HotkeyModifiers,
                HotkeyKey = HotkeyKey,
                ShowCopyToast = ShowCopyToast,
                ToastPosition = ToastPosition
            };
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
            sb.Append(",\"autoStart\":").Append(AutoStart ? "true" : "false");
            sb.Append(",\"captureMode\":\"").Append(CaptureMode ?? "full").Append("\"");
            sb.Append(",\"enableEncryption\":").Append(EnableEncryption ? "true" : "false");
            sb.Append(",\"encryptionKey\":\"").Append(SafeStorage.JsonEscape(EncryptionKey ?? "")).Append("\"");
            sb.Append(",\"hotkeyModifiers\":").Append(HotkeyModifiers);
            sb.Append(",\"hotkeyKey\":").Append(HotkeyKey);
            sb.Append(",\"showCopyToast\":").Append(ShowCopyToast ? "true" : "false");
            sb.Append(",\"toastPosition\":\"").Append(SafeStorage.JsonEscape(ToastPosition ?? "cursor")).Append("\"");
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
            if (s.Version <= 0) s.Version = 3;
            s.ShowFileDetails = SafeStorage.ExtractRaw(json, "showFileDetails") != "false";

            // Older settings files have no autoStart key at all; keep the historical
            // default (enabled) for those, but honour an explicit "false".
            string autoStartRaw = SafeStorage.ExtractRaw(json, "autoStart");
            s.AutoStart = (autoStartRaw == null) ? true : autoStartRaw == "true";

            s.CaptureMode = SafeStorage.ExtractStr(json, "captureMode") ?? "full";
            s.EnableEncryption = SafeStorage.ExtractRaw(json, "enableEncryption") == "true";
            s.EncryptionKey = SafeStorage.ExtractStr(json, "encryptionKey") ?? "";

            string hotkeyModifiersRaw = SafeStorage.ExtractRaw(json, "hotkeyModifiers");
            if (hotkeyModifiersRaw != null)
                s.HotkeyModifiers = (int)SafeStorage.ExtractLong(json, "hotkeyModifiers");
            string hotkeyKeyRaw = SafeStorage.ExtractRaw(json, "hotkeyKey");
            if (hotkeyKeyRaw != null)
                s.HotkeyKey = (int)SafeStorage.ExtractLong(json, "hotkeyKey");
            s.HotkeyModifiers &= 1 | 2 | 4;
            if (s.HotkeyModifiers == 0 || s.HotkeyKey <= 0 || s.HotkeyKey > 255
                || (s.HotkeyModifiers & (1 | 2)) == 0)
            {
                // Shift-only is rejected by the capture UI; keep hand-edited files consistent.
                s.HotkeyModifiers = 0;
                s.HotkeyKey = 0;
            }
            s.ShowCopyToast = SafeStorage.ExtractRaw(json, "showCopyToast") != "false";
            s.ToastPosition = SafeStorage.ExtractStr(json, "toastPosition");
            if (s.ToastPosition != "bottom") s.ToastPosition = "cursor";

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
