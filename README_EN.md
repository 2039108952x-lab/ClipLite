
# ClipLite — Ultra-lightweight Clipboard Manager for Windows

[![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-blue)](https://dotnet.microsoft.com/download/dotnet-framework/net48)
[![License: MIT](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![Downloads](https://img.shields.io/github/v/release/2039108952x-lab/ClipLite)](https://github.com/2039108952x-lab/ClipLite/releases)

**ClipLite** is an ultra-lightweight, portable clipboard manager for Windows. One single EXE — **25 KB** — zero dependencies, zero ads, instant launch.

![screenshot](screenshot.png)

> [中文版](README.md) | [English](README_EN.md)

---

## Highlights

| Metric | Value |
|--------|-------|
| EXE Size | **25 KB** (single file) |
| Memory | ~8–10 MB (after GC settle) |
| Launch Time | **< 50 ms** |
| Hotkey Response | **Instant** (pre-created window) |
| CPU Usage | **0%** (event-driven, no polling) |
| Language | C# |
| Framework | .NET Framework 4.8 (built into Windows) |
| UI | WinForms |
| License | MIT |

---

## Features

### 1. Automatic Clipboard Monitoring
- Uses system-level `WM_CLIPBOARDUPDATE` event — **zero CPU polling**
- Text is auto-saved to history when copied
- Stores up to **500 entries**, auto-removes oldest when full

### 2. Smart Dedup + Anti Loopback
- Each entry gets a **SHA256 fingerprint** (first 16 chars)
- Duplicate content is moved to top instead of re-added
- Auto-skips loopback when pasting from ClipLite itself

### 3. Global Hotkey
- `Ctrl + Shift + V` — toggle history panel
- `↑ ↓` — navigate items · `Enter` — copy & close · `Delete` — remove · `Esc` — close
- Registered via Win32 `RegisterHotKey`, works system-wide

### 4. One-click Reuse
- Click or press Enter on any item → auto-copy to clipboard → panel hides → `Ctrl + V` to paste

### 5. Full-text Search
- Real-time filter bar on top of panel
- Case-insensitive, matches any text fragment

### 6. Pin Management
- Pin important items (`IsPinned` field) — they stay at top permanently with an orange badge

### 7. System Tray Background
- Runs silently in system tray after launch
- Right-click menu: Show History / Pause / Resume / Clear / Quit
- Double-click tray icon to open history panel

### 8. JSON Persistence
- History saved to `cliplite_history.json` next to EXE
- Pure text, human-readable, lightweight
- Fields: `id` (hash), `text`, `time` (ISO), `pinned`

---

## Project Structure

```
ClipLite/
├── Program.cs           # Entry point + app orchestration
├── Models.cs            # Data models + JSON storage
├── Services.cs          # Clipboard monitor + hotkey manager
├── HistoryForm.cs       # History panel UI
├── build.bat            # One-click build script
├── ClipLite.exe         # Build output (25 KB)
└── README.md            # Documentation (Chinese)
```

### Architecture (3-Tier)

| Layer | Components |
|-------|-----------|
| Presentation | `HistoryForm.cs` — borderless popup, search, custom-drawn list |
| Business Logic | `Services.cs` — `ClipboardMonitor` (Win32 message window), `HotkeyManager` (RegisterHotKey) |
| Data | `Models.cs` — `ClipboardEntry` model + `JsonStorage` (manual serialize, no external deps, max 500) |

---

## Quick Start

### Option 1: Download & Run

Download `ClipLite.exe` (25 KB) from [Releases](https://github.com/2039108952x-lab/ClipLite/releases), double-click to run.

Press `Ctrl + Shift + V` to open the history panel.

### Option 2: Build from Source

```bat
cd ClipLite
build.bat
```

Output: `ClipLite.exe` (25 KB).

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
| Storage | JSON file |
| Build | Single `build.bat` command |

---

## FAQ

**Q: How to change the hotkey?**  
A: Edit `VK_V` and `MOD_CONTROL | MOD_SHIFT` constants in `Services.cs` → `HotkeyManager`, then rebuild.

**Q: Max history entries?**  
A: 500 by default. Change `MaxEntries` in `Models.cs`.

**Q: Where is the JSON history file?**  
A: Same directory as `ClipLite.exe`, file named `cliplite_history.json`. Delete it to clear history.

**Q: How to auto-start on boot?**  
A: Create a shortcut to `ClipLite.exe` and place it in `shell:startup`.

---

## License

MIT
