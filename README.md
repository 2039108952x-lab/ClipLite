# ClipLite

> [English](README_EN.md) | **简体中文**

<p align="center">
  <img src="docs/images/cliplite-hero.png" alt="ClipLite 产品预览" width="100%">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 / 11">
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4" alt=".NET Framework 4.8">
  <img src="https://img.shields.io/badge/UI-WinForms-0F6CBD" alt="WinForms">
  <img src="https://img.shields.io/badge/License-MIT-2EA44F" alt="MIT License">
</p>

ClipLite 是一款专为 Windows 打造的轻量剪贴板历史工具。它用原生 WinForms 提供快速唤起、多格式记录、全文搜索、置顶管理和本地存储，不需要第三方 UI 库或额外安装器。

## 为什么选择 ClipLite

- **多格式历史**：文本、富文本、HTML、图片和文件列表统一保存。
- **快速工作流**：默认 `Ctrl + Shift + V` 唤起，点击记录即可回写剪贴板。
- **本地优先**：历史、图片资产和设置均保存在程序目录，不依赖云服务。
- **原生且小巧**：约 90 KB 单文件 EXE，基于 Windows 消息监听，无轮询。
- **安全控制**：支持排除密码管理器等程序，并可选开启 AES-256 历史加密。

## 界面预览

<table>
  <tr>
    <td align="center"><strong>剪贴板历史</strong></td>
    <td align="center"><strong>设置中心</strong></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/history-panel.png" alt="ClipLite 历史面板" width="300"></td>
    <td align="center"><img src="docs/images/settings-panel.png" alt="ClipLite 设置面板" width="620"></td>
  </tr>
</table>

## 核心功能

### 历史与搜索

- 通过 `WM_CLIPBOARDUPDATE` 监听系统剪贴板，最多保留 500 条记录。
- 相同内容自动去重并移至顶部，置顶记录始终优先。
- 输入即时搜索，支持长文本、路径和 HTML 预览。
- 条目悬停时可直接置顶、复制或删除。

### 快捷键与复制反馈

- 在设置中直接录入全局快捷键，保存时检测冲突。
- 新快捷键注册失败时自动保留原快捷键，不会让两者同时失效。
- 复制成功后可在屏幕底部显示不抢焦点的圆角胶囊，也可在设置中完全关闭。
- 剪贴板被其他程序占用时会重试，只在真正写入成功后报告成功。

### 设置与存储

- 全功能抓取 / 纯文本抓取模式。
- 开机自启、复制提示、文件详情和排除程序统一配置。
- JSONL 索引、独立资产目录、滚动备份与孤立文件清理。
- 默认最多 500 条、50 MB 总存储，可在设置页查看占用和记录数。

## 快速开始

### 直接使用

1. 从 [Releases](https://github.com/2039108952x-lab/ClipLite/releases) 下载 `ClipLite.exe`。
2. 运行后 ClipLite 会驻留在系统托盘。
3. 按 `Ctrl + Shift + V` 打开历史面板，或通过托盘菜单打开。

> 现有用户的快捷键以本地设置为准；可在设置页重新录入或禁用。

### 从源码构建

```bat
git clone https://github.com/2039108952x-lab/ClipLite.git
cd ClipLite
build.bat
```

需要 Windows 10/11 和 .NET Framework 4.8。构建脚本直接调用系统 `csc.exe`，不需要 NuGet 包或 Visual Studio 工程。

## 快捷操作

| 操作 | 方式 |
| --- | --- |
| 打开 / 隐藏历史 | 默认 `Ctrl + Shift + V`，可在设置中修改 |
| 搜索 | 打开面板后直接输入 |
| 选择记录 | `↑` / `↓` |
| 复制并关闭 | `Enter` 或点击条目 |
| 删除记录 | `Delete` 或悬停删除按钮 |
| 关闭面板 | `Esc` |

## 数据与隐私

所有内容均保存在 `ClipLite.exe` 同级目录：

```text
cliplite_settings.json
cliplite_data/
├── index.jsonl
├── index.jsonl.bak1 ... bak3
└── assets/
```

ClipLite 不需要账号，不上传剪贴板内容，也不依赖远程服务。如果密码管理器或其他敏感程序不应被记录，请将其可执行文件名加入“排除程序”。

## 项目结构

```text
Program.cs             应用生命周期与事件编排
Services.cs            剪贴板监听与事务式快捷键注册
Models.cs              记录模型、JSONL 存储与资产管理
HistoryForm.cs         剪贴板历史面板
SettingsForm.cs        设置窗口
CopyFeedback.cs        非激活式复制反馈胶囊
UiTheme.cs             颜色、字体、圆角和 DWM 效果
build.bat              单文件构建脚本
```

## License

MIT
