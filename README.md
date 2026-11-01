# Breaks for Eyes（Windows 版）

基于 **20-20-20 法则**的护眼休息提醒工具：每工作一段时间，提醒你看向远处、起身活动，帮助缓解视疲劳、干眼与头痛。常驻系统托盘，安静、低占用、本地运行。

> 本项目参考 macOS 版 [Breaks for Eyes / Intermission](https://apps.apple.com/us/app/intermission-breaks-for-eyes/id1439431081)（Two People Software）的功能设计，并吸收同类应用的**空闲检测**、**短/长两类休息**、**本地统计**等增强项。
>
> ⚠️ 本项目为独立实现，与 Two People Software 及其产品**无任何关联**，未使用其代码、图标或文案，仅借鉴功能理念。

## 功能特性

**休息调度**
- 短休息（20-20-20）：默认工作 20 分钟 / 休息 20 秒，可独立开关、自定义间隔与时长
- 长休息（起身活动）：默认关闭，60 分钟 / 90 秒，独立开关与时长
- 空闲检测：仅「活跃使用」时间计入工作计时，人离开电脑不累计

**提醒方式**
- 全屏遮罩（阻断式）或轻量通知（不阻断），二选一
- 提前预警：休息前 5 / 20 秒弹出提示，可跳过 / 推迟
- 跳过策略：立即跳过 / 延迟 N 秒后跳过 / 禁止跳过

**遮罩外观**
- 多显示器：每屏独立遮罩，覆盖所有屏幕
- 自定义背景图片：未设置时使用默认深色背景；支持「每屏同图」或「跨屏长条平铺」两种填充模式；图片透明度 0–100% 可调（文字 / 按钮保持清晰）

**托盘与自动化**
- 托盘常驻，实时显示距下次休息的倒计时
- 贪睡 / 暂停：30 分钟 / 1 小时 / 2 小时 / 4 小时 / 当天
- 开机自启（默认关闭，可在设置中开启）
- 自动暂停：检测到全屏应用 / 会议通话 / 视频播放时自动暂停计时（启发式，误判可手动覆盖）

**其他**
- 自定义休息文案与提示音
- 本地统计：活跃时长、应休 / 已休 / 跳过 / 贪睡次数（仅存本地，不上传）
- 单实例运行

## 系统要求

- Windows 10 1903 及以上 / Windows 11
- 发布版为自包含单文件，**无需安装 .NET 运行时**

## 构建与运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```bash
# 源码运行
dotnet run --project src/BreaksForEyes

# 构建
dotnet build src/BreaksForEyes -c Release

# 发布为单文件 exe（输出到 bin/Release/net10.0-windows/win-x64/publish/）
dotnet publish src/BreaksForEyes -c Release
```

发布产物为单个 `BreaksForEyes.exe`（win-x64 自包含，约 170 MB）。

## 使用说明

- **托盘图标**：悬浮显示倒计时，左键打开菜单。
- **托盘菜单**：暂停休息（30 分钟 / 1 小时 / 2 小时 / 4 小时 / 当天）、立即休息、恢复计时、设置、关于、退出。
- **设置页**：休息节奏、提醒方式、遮罩外观、跳过策略、自动暂停、通用等分组。
- **数据目录**：`%APPDATA%\BreaksForEyes\settings.json`（配置，首次运行生成）、`stats.json`（统计）。

## 项目结构

```
.
├─ DESIGN.md                      # 软件设计文档
└─ src/BreaksForEyes/
   ├─ BreaksForEyes.csproj        # net10.0-windows, WPF + WinForms, 零第三方依赖
   ├─ app.manifest                # PerMonitorV2 高 DPI
   ├─ App.xaml / App.xaml.cs      # 应用入口、单实例、服务接线、1s 计时
   ├─ Models/                     # Settings / Stats / 调度模型
   ├─ Core/                       # BreakScheduler（短/长双计时器状态机）、SnoozeController
   ├─ Services/                   # 配置 / 统计 / 空闲检测 / 自动暂停 / 自启 / 单实例 / 声音
   └─ UI/                         # 托盘、遮罩、通知、设置窗口、关于
```

## 技术栈

- C# / .NET 10（WPF）
- **零第三方 NuGet 依赖**：托盘与通知使用 `System.Windows.Forms.NotifyIcon` 与轻量 WPF 窗口；空闲检测用 Win32 `GetLastInputInfo`
- 数据仅本地存储（`%APPDATA%\BreaksForEyes\`）

## 已知限制 / 路线图

- 界面本地化（FR-14）：目前界面固定简体中文，语言选项为预留，属 P2
- 自动暂停为启发式检测，UWP / 宿主进程等场景可能漏判
- 独占全屏游戏下的降级提醒尚未实现
- 仓库内自动化测试待补充

## 致谢

功能设计参考 macOS 版 [Breaks for Eyes / Intermission](https://apps.apple.com/us/app/intermission-breaks-for-eyes/id1439431081)（Two People Software），并借鉴 [Eye Break](https://davideagostini.github.io/eye-break/) 的空闲检测与双休息理念。

## License

[MIT](./LICENSE) © 2026 HeHouliangJLU
