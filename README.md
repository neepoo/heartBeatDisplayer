# HeartBeat · 佳明实时心率悬浮窗

[![CI and Release](https://github.com/neepoo/heartBeatDisplayer/actions/workflows/ci-release.yml/badge.svg)](https://github.com/neepoo/heartBeatDisplayer/actions/workflows/ci-release.yml)

Windows 上的轻量心率悬浮工具：蓝牙直连 Garmin Forerunner 255，在桌面或无边框游戏上方显示当前 BPM、心跳动效、最近 5 分钟的真实心率趋势及平均／最低／最高心率。

支持 Windows 10 2004（19041）或更新版本 / Windows 11 x64。程序自带 .NET 运行时，真实心率连接需要电脑具有可用的 BLE 蓝牙适配器。

下载：[最新 Release](https://github.com/neepoo/heartBeatDisplayer/releases/latest)。仓库及发布包公开，无需登录 GitHub 即可浏览和下载。

## v1.1.0 更新

- 解锁后可拖动四边、四角调整大小；右下角提供缩放标记，重启后恢复尺寸。默认 320 × 240，最小 280 × 216，数字及图表随窗口调整布局。
- 新增按当前 BPM 推算节奏的心跳动效；数据失效或窗口隐藏时停止，可在设置中关闭动效。
- 新增最近 5 分钟有效样本的均值、最低值及最高值。没有有效样本时显示 `--`，旧版设置自动兼容。
- 发布包使用独立版本目录。更新时先从托盘退出旧版，再运行新版 EXE。

## 开始使用

1. 在 Release 的 **Assets** 下载 `HeartBeat-<版本号>-win-x64.zip`（不要下载 Source code），解压并保留全部文件，双击 `HeartBeat.exe`。更新前从托盘退出旧版，将新版解压到独立目录。
2. 电脑开启蓝牙；需要支持 Bluetooth Low Energy（BLE）的适配器及正常的 Windows 驱动。
3. 戴好手表，长按 **UP → 腕式心率 → 广播心率 → START**。部分固件把“腕式心率”放在健康相关菜单里，也可以从控制菜单开启心率广播。务必按 START 开始广播。
4. 程序中点击 **扫描设备**，等待约 8 秒，选择自己的手表并点击 **连接**。无需登录 Garmin 账号，也无需通过手机中转。
5. 将悬浮窗拖到合适位置，再启用 **锁定位置并开启鼠标穿透**。游戏使用 **无边框窗口 / 无边框全屏**。

官方参考：[255 心率广播操作](https://www8.garmin.com/manuals/webhelp/GUID-676967A0-1B23-4384-9BC9-76F3D643F1C8/EN-US/GUID-D8D363C2-0690-48D4-95E2-A3557E7D53C2.html)、[蓝牙广播兼容说明](https://support.garmin.com/nl-NL/?faq=Zj1947s6pqAHzBCAhLhrC9)。

## 日常操作

| 操作 | 入口 |
| --- | --- |
| 显示 / 隐藏悬浮窗 | `Ctrl + Alt + H`，或托盘菜单 |
| 锁定 / 解锁、鼠标穿透 | `Ctrl + Alt + L`，或设置 / 托盘菜单 |
| 调整悬浮窗大小 | 解锁后拖动窗口边缘或右下角；锁定时禁止缩放 |
| 开关心跳动效 | 连接与设置 → 心跳动效 |
| 调整背景透明度 | 连接与设置 → 背景不透明度（40%～95%） |
| 窗口移出屏幕后找回 | 托盘菜单 → 重置悬浮窗位置 |
| 打开设置 | 双击托盘图标，或右键菜单 |
| 完全退出 | 托盘菜单 → 退出 |

关闭设置窗口只隐藏设置，程序继续运行。快捷键被其他软件占用时会提示，可使用托盘菜单操作。再次启动已在运行的程序会提示从托盘打开。

首次默认允许拖动和缩放，之后记住位置、宽高、锁定状态、背景透明度、动效开关及最后成功连接的真实设备。重启后尝试连接上次的手表；主动点击“断开”会停止本次会话的重连。

## 数据如何显示

- 大号数字在收到手表通知后更新。实际测量及广播频率由手表决定。
- 曲线显示最近 5 分钟的 BPM 趋势，每秒保留最后一次测量，纵轴自动适配读数。
- 下方均值、最低值及最高值使用同一时间范围内收到的有效样本；缺失数据不参与统计。均值按整数 BPM 四舍五入。
- 心跳图标是依据当前 BPM 生成的节奏动效，不代表手表逐次心跳的精确时间。
- 5 秒没有有效数据时，当前数字显示 `--`。数据缺失处留空；没有收到的数据不会被补成假曲线。
- 蓝牙断开后自动重试，失败间隔依次为 2、4、8、15 秒，之后维持 15 秒。恢复连接后重新订阅心率数据。
- 心率只保留在内存，退出即清空。设置保存在 `%LOCALAPPDATA%\HeartBeatDisplayer\settings.json`。

## 没有手表时预览

在设置中点击 **试用模拟数据**，或双击发布包中的 `演示模式.cmd`。也可运行：

```powershell
.\HeartBeat.exe --demo
```

演示时卡片明确显示“演示 · 模拟数据”。连接真实设备即可退出演示；模拟设备不会覆盖记住的真实设备。若已有实例运行，先从托盘退出再用启动脚本打开。

## 连接排查

- **未检测到蓝牙适配器**：在 Windows 设置及设备管理器中确认蓝牙可用；若电脑没有蓝牙，需要接入支持 BLE 的适配器。
- **扫描为空**：确认手表已经开始广播而非只停留在设置页面；靠近电脑后重新扫描。
- **找到设备但无法连接**：确认选中的是自己的心率广播设备；关闭其他占用其蓝牙连接的软件后重试。Windows 中“已配对”不等于应用已经订阅心率广播。
- **电脑休眠或手表停止广播**：数字会变为 `--`；恢复蓝牙及广播后程序会重连。若手表变更了广播地址，重新扫描并选择它。
- **游戏中看不到**：检查游戏是无边框模式，使用快捷键显示或托盘重置位置；当前不支持独占全屏，其他置顶窗口也可能影响显示顺序。

## 从源码运行

需要 Windows 10 2004（19041）或更新版本 / Windows 11 x64，以及 .NET 10 SDK。

```powershell
dotnet run --project src/HeartBeat.App
dotnet run --project src/HeartBeat.App -- --demo
```

本地依赖缓存放在项目 `.packages`。自动化验证及发布命令：

```powershell
# 核心行为测试；失败返回非零退出码
dotnet run --project tests/HeartBeat.Tests -c Release

# 真实 WPF 应用集成检查，会短暂显示本程序窗口并自动退出
# 使用独立目录，不读取或覆盖正常用户设置
dotnet run --project tests/HeartBeat.App.Tests -c Release -- --demo --settings-dir artifacts/smoke-session

# 可追加 --probe-bluetooth，扫描结果及界面预览保存在上述目录

dotnet build HeartBeat.slnx -c Release

# 构建自带运行时的发布包，首次需要从 NuGet 下载官方依赖
.\scripts\publish.ps1
```

输出：`artifacts\HeartBeat-1.1.0-win-x64\HeartBeat.exe` 和 `artifacts\HeartBeat-1.1.0-win-x64.zip`，附 SHA256 校验文件。后续版本按项目中的 Version 自动命名。

测试项目是可执行的检查程序，使用 `dotnet run`；`dotnet test` 不会执行这些检查。`publish.ps1` 默认只运行核心测试，完整验证还须单独运行 WPF 集成检查。

## CI 与 GitHub Release

[CI and Release 工作流](https://github.com/neepoo/heartBeatDisplayer/actions/workflows/ci-release.yml) 在推送 `main`、提交面向 `main` 的 PR、推送 `v*` 标签时运行，也支持在 Actions 页面手动运行验证。

- Windows runner 安装 .NET 10，恢复依赖、Release 构建、执行核心测试和使用模拟心率的 WPF 集成检查，再调用同一个本地打包脚本。
- Actions 的 `windows-package` 工件包含免安装 ZIP 和 SHA256；`test-results` 包含检查报告和界面预览，保留 14 天。测试失败会阻止发布，并尽可能保留诊断文件。
- 只有推送版本标签才创建 Release；标签必须与 `src/HeartBeat.App/HeartBeat.App.csproj` 的 `Version` 完全对应，例如 `1.1.0` 对应 `v1.1.0`。带 `-rc.1` 等后缀的版本标为预发布。
- Release 上传本次 CI 生成并校验的 ZIP 和 SHA256，自动生成更新说明。使用 GitHub 自带 `GITHUB_TOKEN`，无需新增 PAT 或仓库 Secret；只有发布任务具有 `contents: write` 权限。
- 手动运行工作流只验证和生成工件。已发布的 Release 不自动覆盖；需要修改应用或发布包时，递增版本并创建新标签。

发布下一版示例（先将项目 `Version` 改成 `1.2.0`，同步更新说明并提交推送，等待该提交的 CI 通过）：

```powershell
git tag -a v1.2.0 -m "发布 v1.2.0"
git push origin v1.2.0
```

标签触发的工作流全部通过后，可在 [Releases](https://github.com/neepoo/heartBeatDisplayer/releases) 下载。校验下载文件时，运行 `Get-FileHash .\HeartBeat-<版本号>-win-x64.zip -Algorithm SHA256`，与随包 `.sha256` 中的哈希对照。

## 验证范围

核心测试覆盖协议解析、缓存、超时、重连与旧回调隔离；WPF 检查覆盖模拟数据流、穿透样式、隐藏/显示、不抢焦点、恢复位置、设置和退出，并输出预览图片。

CI 的模拟数据检查不验证蓝牙硬件。真实手表连接、通知到屏幕延迟、多屏混合缩放及具体游戏中持续运行，需要在目标硬件上验证。请按仓库的 [实机验证清单](https://github.com/neepoo/heartBeatDisplayer/blob/main/docs/ble-validation.md) 操作。当前不包含独占全屏注入、OBS 专用输出、历史导出和心率报警。
