# HeartBeat · 佳明实时心率悬浮窗

[![CI and Release](https://github.com/neepoo/heartBeatDisplayer/actions/workflows/ci-release.yml/badge.svg)](https://github.com/neepoo/heartBeatDisplayer/actions/workflows/ci-release.yml)

桌面心率悬浮工具：通过 BLE 接收 Garmin Forerunner 255 广播，在屏幕上显示当前 BPM、心跳动效、最近 5 分钟曲线及平均／最低／最高心率。界面使用 Avalonia，心率与重连逻辑由三端共享。

**跨平台预览版：[2.0.0-preview.1](https://github.com/neepoo/heartBeatDisplayer/releases/tag/v2.0.0-preview.1)**。原 Windows WPF 稳定版仍可下载：[1.1.0](https://github.com/neepoo/heartBeatDisplayer/releases/tag/v1.1.0)。仓库和发布包公开，无需登录即可下载；`releases/latest` 指向稳定版，不包含预览版。

## 下载与启动

在 Release 的 **Assets** 下载对应系统的包，勿选 Source code。每个包均自带 .NET 运行时，并附 `.sha256` 校验文件。解压时保留全部文件，更新时从托盘退出旧版，再将新版解压到独立目录。

| 系统 | 包名后缀 | 启动方式 |
| --- | --- | --- |
| Windows 10 2004+ / Windows 11，x64 | `win-x64.zip` | 双击 `HeartBeat.exe` |
| macOS 14+，Apple Silicon | `osx-arm64.zip` | 解压后打开 `HeartBeat.app` |
| macOS 14+，Intel | `osx-x64.zip` | 解压后打开 `HeartBeat.app` |
| Ubuntu 24.04，x64、X11 桌面 | `linux-x64.tar.gz` | 解压后运行 `./HeartBeat` |

Mac 包仅做临时签名，未使用 Developer ID 签名或 Apple 公证。首次打开可能被系统拦截；确认下载来源后，在系统设置的“隐私与安全性”中允许打开。真实连接需要同意蓝牙权限；拒绝后可在该设置中重新允许。

Linux 需要运行中的 BlueZ、系统 D-Bus、可用的 BLE 适配器和桌面图形依赖。在 Ubuntu 中安装：

```bash
sudo apt install bluez libx11-6 libxext6 libice6 libsm6 libfontconfig1 libgl1 libxcursor1 libxrandr2 libxi6 fonts-noto-cjk
tar -xzf HeartBeat-2.0.0-preview.1-linux-x64.tar.gz
./HeartBeat
```

程序以普通用户身份运行。首轮针对 X11；Wayland 暂不保证置顶、鼠标穿透和全局快捷键，建议登录 X11 会话。某些桌面没有托盘区域，程序会保留设置入口，不依赖安装托盘扩展才能退出。

## 连接手表

1. 确认电脑蓝牙可用并戴好手表。
2. Forerunner 255 长按 **UP → 腕式心率 → 广播心率 → START**。部分固件的菜单位置不同；务必开始广播，不能只停留在广播页面。
3. 在程序中扫描设备，选择自己的手表并连接。无需 Garmin 账号或手机中转。
4. 调整悬浮窗位置、尺寸和透明度，然后锁定以开启鼠标穿透。Windows 游戏使用无边框模式；独占全屏不在支持范围。

官方参考：[255 心率广播操作](https://www8.garmin.com/manuals/webhelp/GUID-676967A0-1B23-4384-9BC9-76F3D643F1C8/EN-US/GUID-D8D363C2-0690-48D4-95E2-A3557E7D53C2.html)。

## 操作与数据

| 操作 | 入口 |
| --- | --- |
| 显示／隐藏 | `Ctrl + Alt + H` 或托盘菜单 |
| 锁定／解锁 | `Ctrl + Alt + L` 或设置／托盘菜单；Mac 的 Alt 对应 Option |
| 调整尺寸 | 解锁后拖动边缘或右下角 |
| 透明度、心跳动效 | 设置窗口 |
| 找回移出屏幕的窗口 | 重置位置 |
| 完全退出 | 设置或托盘的退出入口 |

快捷键占用或平台不支持时显示提示，可通过界面操作。默认窗口 320 × 240，最小 280 × 216；记住窗口位置、宽高、锁定状态、透明度和动效偏好。

- 当前读数超过 5 秒未更新时显示 `--`，曲线缺失处留空，不补造数据。
- 曲线和统计采用最近 5 分钟有效样本，每秒保留最后一次测量；均值按整数 BPM 四舍五入。
- 心跳图标依据 BPM 生成节奏动效，并非精确的逐次心跳时刻；数据失效、隐藏或关闭动效时停止。
- 意外断开后按 2、4、8、15 秒间隔重试；主动断开停止本次重连。恢复连接后重新订阅通知。
- 心率只保留在内存，退出即清空。设备标识只用于当前系统重连，不能跨系统复制使用。

设置位置：Windows `%LOCALAPPDATA%\HeartBeatDisplayer`（兼容 1.x）；macOS `~/Library/Application Support/HeartBeatDisplayer`；Linux `$XDG_CONFIG_HOME/HeartBeatDisplayer`，未设置时为 `~/.config/HeartBeatDisplayer`。损坏或过期的设置会恢复可用默认值。

## 无手表预览与排查

设置中选择“试用模拟数据”，或运行 `--demo`。Windows 包附 `演示模式.cmd`，Linux 包附 `demo.sh`；Mac 可运行：

```bash
open HeartBeat.app --args --demo
```

模拟模式明确标注模拟数据，不初始化蓝牙，也不覆盖记住的真实设备。若已有实例，先退出再以其他启动参数运行。

扫描为空时，确认手表已开始广播并靠近电脑。没有适配器、蓝牙关闭、权限拒绝或 BlueZ 不可用时，根据程序提示修正；“已配对”不代表已订阅心率通知。手表广播地址变化后可重新扫描。新平台需要按[实机验证清单](https://github.com/neepoo/heartBeatDisplayer/blob/main/docs/ble-validation.md)检查。

## 开发与验证

需要 **.NET SDK 10.0.1xx** 和 **PowerShell 7**。`global.json` 在此 SDK 功能版本内采用最新补丁，NuGet 缓存放在项目 `.packages`。

```powershell
# Windows；其他平台的入口分别为 src/HeartBeat.Mac、src/HeartBeat.Linux
dotnet run --project src/HeartBeat.App -- --demo

# 当前系统的核心、共享 UI、平台检查与构建
pwsh -File scripts/test.ps1

# 完整测试通过后生成当前系统的独立运行包
pwsh -File scripts/publish.ps1
```

Mac 构建需要 Xcode 26.0.1 和 `dotnet workload install macos --version 10.0.100`，使用 `-RuntimeIdentifier osx-arm64` 或 `osx-x64` 打包。Windows 和 Linux 的 RID 分别为 `win-x64`、`linux-x64`。必须在目标操作系统上发布。

`HeartBeat.slnx` 是共享代码、Windows 入口和相关检查的开发解决方案；macOS/Linux 按平台项目或 `scripts/test.ps1` 构建，避免其他系统被要求安装 Windows/macOS 工作负载。

测试项目使用 `dotnet run`，不能以 `dotnet test` 代替。共享 Headless 检查只覆盖布局、设置和数据流；原生窗口检查使用发布包的 `--demo --smoke-test --settings-dir <独立目录>`，输出报告、预览并自动退出。CI 的 Linux 窗口检查使用 Xvfb 配合窗口管理器。

**验证边界**：macOS/Linux 为预览支持，当前未在真实手表和目标桌面上验收蓝牙连接、休眠恢复及多屏操作；Mac 解锁点击／拖动时的应用激活行为仍需实机验证，禁止窗口成为 key/main 的原生检查不等于该场景实测。自动检查不替代硬件测试。Windows 历史开发环境也未检测到可用蓝牙适配器，因此不宣称完成三端真实连接、实际延迟或游戏适配验证。

## CI 与 Release

[CI 工作流](https://github.com/neepoo/heartBeatDisplayer/actions/workflows/ci-release.yml) 为 Windows x64、Mac arm64/x64、Linux x64 分别构建、测试、打包和启动检查。报告、预览和经过检查的包保留 14 天。

版本唯一来源为 `Directory.Build.props`。更新版本和文档，提交推送并等待 CI 通过，再推送对应标签，例如：

```powershell
git tag -a v2.0.0-preview.1 -m "发布跨平台预览版"
git push origin v2.0.0-preview.1
```

示例标签已经存在时不可重复创建或移动；下次发布递增版本号。只有版本标签触发 Release，四个任务全部通过后统一上传四个包及 SHA256；带后缀的版本标为预发布，不替换稳定版。普通提交和手动工作流只生成 CI 工件。

打包使用唯一暂存目录，路径由脚本输出，避免覆盖运行中的程序和混入旧文件。ZIP/TAR 位于 `artifacts/HeartBeat-<版本>-<RID>.*`。可使用 `Get-FileHash <包路径> -Algorithm SHA256`，与随包 `.sha256` 内容对照。
