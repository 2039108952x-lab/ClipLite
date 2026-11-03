# ClipLite — Ultra-lightweight Clipboard Manager for Windows

[![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-blue)](https://dotnet.microsoft.com/download/dotnet-framework/net48)
[![License: MIT](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![Downloads](https://img.shields.io/github/v/release/2039108952x-lab/ClipLite)](https://github.com/2039108952x-lab/ClipLite/releases)

**ClipLite** is a lightweight, portable clipboard manager for Windows. One single EXE — **about 82 KB** — no installer, no ads, instant launch.

![screenshot](screenshot.png)

> [中文版](README.md) | [English](README_EN.md)

---

## Highlights

| Metric | Value |
|--------|-------|
| EXE Size | **~82 KB** (single file) |
| Launch Time | **< 50 ms** |
| Hotkey Response | **Instant** (pre-created window) |
| CPU Usage | **0%** (event-driven, no polling) |
| Language | C# 5 |
| Framework | .NET Framework 4.8 (built into Windows) |
| UI | WinForms |
| License | MIT |

---

## Features

### 1. Automatic Clipboard Monitoring
- Uses the system-level `WM_CLIPBOARDUPDATE` event — **zero CPU polling**
- Captures text, images, file lists, RTF and HTML
- Stores up to **500 entries**, removing the oldest when full

### 2. Smart Dedup + Anti Loopback
- Each entry gets a **SHA256 fingerprint** (first 16 chars)
- Duplicate content is moved to the top instead of re-added
- Copying from ClipLite itself does not create a feedback loop

### 3. Configurable Global Hotkey
- `Ctrl + Shift + V` by default — change or disable it in **Settings → Global Hotkey**
- Registered through Win32 `RegisterHotKey`, active system-wide
- A tray balloon warns if the chosen combination is already taken
- Keyboard: `↑ ↓` navigate · `Enter` copy & close · `Delete` remove · `Ctrl + P` pin · `Esc` close

### 4. Single-click Reuse
- **One click** on any row copies it — no double-click required
- Hover actions: copy / delete; right-click menu: copy, pin, delete
- The panel hides automatically; paste with `Ctrl + V`
- Optional lightweight in-app toast on every successful copy (normal Ctrl+C and history reuse), not affected by Windows notification settings
- Toast position can be switched between **near the content** and **bottom of the screen**; the choice is applied and saved immediately

### 5. Search & Filter
- Real-time search over text, file paths, RTF previews and type names
- Case-insensitive with yellow match highlighting
- Type filter: All / Text / Image / Files / Rich text / HTML

### 6. Thumbnails & Pinning
- Image entries render async thumbnails without blocking the UI
- Pinned items stay at the top with an orange badge
- Pin or unpin from the right-click menu / `Ctrl + P`

### 7. System Tray
- Runs silently in the system tray
- Right-click menu: Show History / Pause / Clear / Settings / Quit
- Double-click the tray icon to open the history panel

### 8. Settings
- Global hotkey capture
- Capture mode: all formats or text only
- Start with Windows
- Excluded applications
- Copy-success notification
- Toast position: near the copied content or bottom-center of the screen (applied immediately)
- AES-256 history encryption
- Storage usage, entry count and data location

### 9. Data Persistence
- History lives in `cliplite_data/` next to the EXE
- `index.jsonl` stores the index; `assets/` stores images and large RTF/HTML payloads
- Atomic writes with `index.bak1~3.jsonl` rolling backups
- The index is AES-256 encrypted when encryption is enabled
- Settings are stored in `cliplite_settings.json`

---

## Project Structure

```
ClipLite/
├── Program.cs           # Entry point + app orchestration
├── Models.cs            # Data models + encrypted JSONL storage
├── Services.cs          # Clipboard monitor + hotkey manager
├── HistoryForm.cs       # History panel UI
├── Settings.cs          # Settings model
├── SettingsForm.cs      # Settings window UI
├── ThumbnailCache.cs    # Async thumbnail cache (LRU)
├── UiTheme.cs           # Palette, rounded panels, cue text box
├── ToastForm.cs         # In-app copy-success toast
├── build.bat            # One-click build script
├── ClipLite.exe         # Build output (~82 KB)
└── cliplite_data/       # Runtime data (generated)
```

### Architecture (3-Tier)

| Layer | Components |
|-------|-----------|
| Presentation | `HistoryForm.cs`, `SettingsForm.cs`, `ToastForm.cs`, `UiTheme.cs` |
| Business Logic | `Services.cs` — `ClipboardMonitor`, `HotkeyManager`; `Program.cs` — lifecycle |
| Data | `Models.cs` — `ClipboardEntry`, `SafeStorage`; `Settings.cs` — settings persistence |

---

## Quick Start

### Option 1: Download & Run

Download `ClipLite.exe` from [Releases](https://github.com/2039108952x-lab/ClipLite/releases), then run it.

Press `Ctrl + Shift + V` to open the history panel.

### Option 2: Build from Source

```bat
cd ClipLite
build.bat
```

Output: `ClipLite.exe` (~82 KB).

**Requirements:** Windows 10/11 with .NET Framework 4.8 (pre-installed). Nothing else needed.

---

## Tech Stack

| Area | Choice |
|------|--------|
| Language | C# 5.0 |
| Framework | .NET Framework 4.8 |
| UI | WinForms |
| Graphics | System.Drawing |
| Compiler | csc.exe (built into Windows) |
| Storage | JSON Lines + asset files |
| Build | Single `build.bat` command |

---

## FAQ

**Q: How do I change the hotkey?**
Open the tray menu **Settings → Global Hotkey**, click the field and press the new combination. Press `Backspace` to disable the global hotkey.

**Q: Max history entries?**
500 by default. Change `MaxEntries` in `Models.cs` or the settings model if you need a different limit.

**Q: Where is the history stored?**
Next to `ClipLite.exe`, in `cliplite_data/` (`index.jsonl` + `assets/`). Delete that folder to clear history.

**Q: How do I auto-start on boot?**
Enable **Start with Windows** in Settings; no manual shortcut needed.

---

## License

MIT
