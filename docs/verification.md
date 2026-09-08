# Verification — 2026-09-08

## v2.0.0-preview.1 — 跨平台迁移

共享界面迁移到 Avalonia 12.1.2，Core 的心率协议、历史与控制器继续共享。
Windows 使用 WinRT / Win32，Mac 使用 CoreBluetooth / AppKit，Linux 使用 BlueZ / X11。

四架构完整 CI 已通过：[运行 34192035328](https://github.com/neepoo/heartBeatDisplayer/actions/runs/34192035328)，
对应实现提交 `e57331bc7b2b327e028dec00480e3effc3788e91`。
四种通过启动验收的包均已生成；最终 Release 由相同门槛的版本标签工作流发布。

发布已完成：[v2.0.0-preview.1](https://github.com/neepoo/heartBeatDisplayer/releases/tag/v2.0.0-preview.1)，
对应提交 `bedd35c8db6ca53f724ea78ec5320e06f0a52b8e`。
[主分支检查](https://github.com/neepoo/heartBeatDisplayer/actions/runs/34192639756)和
[标签发布检查](https://github.com/neepoo/heartBeatDisplayer/actions/runs/34193131128)均全部通过。
已确认 Release 非草稿、标为预发布，四个包和四份 SHA256 共八个附件齐全；
下载的四份校验文件与 GitHub 已上传包的 SHA256 摘要逐一匹配。
`releases/latest` 仍指向 `v1.1.0` 稳定版。

### 自动检查

- Core：21 项；共享 Headless：33 项；另有 4 项禁用 JSON 反射后的配置格式、
  设备与偏好读写检查，以及损坏配置恢复后仍显示启动提示的回归。
- Windows 原生：共享与窗口检查 43 项，加 8 项外部 Win32 检查；
  发布 ZIP 解压后再次启动并检查 43 项。
- Linux：10 项蓝牙适配层生命周期检查；在 Ubuntu 24.04 的 Xvfb / Openbox
  会话中，解压实际 tar.gz 并通过 45 项共享与 X11 原生检查。
- Mac：4 项通知生命周期检查；Apple Silicon 与 Intel 的目标项目均在
  固定 Xcode 26.0.1 / macos workload set 10.0.100 上编译通过。
  两种架构的解压包均通过 49 项共享与原生窗口检查，并额外确认 LaunchServices
  启动及实际应用进程退出码为 0；直接启动和 .app 启动使用不同设置目录。
- 本地 Windows 回归通过；本地 NuGet 漏洞源不可达产生 NU1900，未出现编译错误。
  CI 的目标平台构建单独检查，不将本地交叉引用编译当作平台运行证据。
- 检查窗口实际达到 420×280 / 280×216 DIP 后再截图；Windows、Mac 和 Linux
  预览已人工查看，紧凑窗口保留 BPM、曲线和平均／最低／最高值。
- 所有发布包必须经过 SHA256、解压后启动、完整报告与退出检查。
  Mac 额外检查每份 dylib 的 Mach-O 格式、签名、直接进程退出码与 LaunchServices 启动。

### 云端发现并修复的问题

- Mac 工作负载的 AppBundleDir 独立于通用 publish 输出目录。
  固定 26.0 工作负载还会把绝对 AppBundleDir 拼进签名缓存路径，从而用签名参数
  覆盖 dylib。失败包的文本与官方 ComputeCodesignItems / Codesign 源码一致；
  改用项目内唯一相对路径，签名后再用 ditto 复制至发布目录，并增加原生库格式门槛。
- Mac 发布移除构造参数反射信息，旧配置序列化在启动时抛出异常；改用
  SettingsJsonContext 生成元数据。禁用反射的回归先验证旧实现失败，再验证新实现通过。
- Linux 原生缩放异步完成，立即截图会保存旧尺寸；现在等待实际窗口 Bounds
  收敛，超时会失败，避免将旧尺寸截图当作紧凑窗口验证结果。

### 验证边界

以上自动检查使用模拟心率，不初始化真实蓝牙。三端真实 Garmin 255 连接、
通知延迟、蓝牙权限拒绝／恢复、休眠、混合 DPI、多屏和游戏兼容仍需目标实机验收。
Mac key/main 焦点限制的原生查询不等于已验证解锁点击／拖动时的应用激活行为。
Linux 首轮限 Ubuntu 24.04 x64 X11；Wayland 未承诺完整悬浮能力。
Mac 包只做 ad hoc 签名，未公证。旧 v1.1.0 Windows 稳定版保留。

## v1.1.0 — resizing and heart-rate elements

- Before the fix, regression checks failed for native corner resize hit testing,
  persisted width/height, heart animation, and summary statistics.
- Core: **21/21 passed**. Added valid-window statistics, unavailable values and
  rounding fixtures with independently specified expectations.
- WPF application: **24/24 passed**. Added resize hit targets, locked resize
  exclusion, size persistence, summary labels, motion toggle/staleness, compact
  layout, invalid dimensions and migration from v1.0 settings.
- `dotnet build HeartBeat.slnx -c Release --no-restore`: **0 warnings, 0 errors**.
- Inspected 420×280 and 280×216 DIP rendered previews in
  `artifacts/v1.1-check/`. Full source review found no actionable important issue.
- Native resize tests exercise WM_NCHITTEST and WM_EXITSIZEMOVE against the real
  WPF window. Actual mouse dragging and mixed-DPI monitor behavior still require
  interactive testing on the target desktop.
- Release output is versioned to avoid overwriting the user's running v1.0.
- Published `HeartBeat-1.1.0-win-x64/HeartBeat.exe` was started in a separate
  test session and verified to load its bundled runtime. Its ZIP contains 477
  entries, including the program, runtime, Chinese guide and demo launcher.
  Only the test process was stopped; the user's running v1.0 process was preserved.

## v1.0 baseline

## Completed

- `dotnet run --project tests/HeartBeat.Tests -c Release`: **19/19 passed**.
  Includes 8/16-bit BLE packets, optional fields, malformed packets, no contact,
  stale data, second buckets, five-minute eviction, gaps, old session callbacks,
  serialized device switching, retry cancellation, startup disconnect, and
  disposal while a native connect is pending.
- `dotnet build HeartBeat.slnx -c Release --no-restore`: **0 warnings, 0 errors**.
- `dotnet run --project tests/HeartBeat.App.Tests -c Release --no-build -- --demo --settings-dir artifacts/smoke-release`:
  **14/14 passed** against the actual WPF application. Result file:
  `artifacts/smoke-release/smoke-test.txt`.
- Rendered and visually inspected the card and settings window using synthetic
  samples. PNG previews in `artifacts/smoke-release/`.
- Independent source review found a cold Windows discovery-cache reconnect
  issue. Fixed by exact-address/type discovery before retrying a null lookup;
  scoped re-review found no important regression.
- `scripts/publish.ps1` completed; published executable was launched with a
  separate test settings directory. Verified it remained running and loaded
  `coreclr.dll` from its own release folder, then stopped only that test process.
  This is a startup test; graceful application exit was exercised by WPF tests.
- ZIP entry check verified EXE, application DLL, .NET runtime, WPF framework,
  Chinese guide and demo launcher. **477 entries, 82,631,051 bytes**.

## Hardware observations and remaining acceptance

The BLE API probe returned no adapter. An elevated
`Get-PnpDevice -Class Bluetooth -PresentOnly` check also found no present Bluetooth
devices. Therefore **no real Garmin connection, real heart-rate latency, or game
compatibility result is claimed**.

The target computer still needs the checks in `ble-validation.md`: live 255
notifications, radio/broadcast recovery, startup after discovery-cache reset,
30-minute borderless-game use, mixed-DPI monitor movement/removal, and notification
to-display latency below the 500 ms target. The automated check covers native
click-through flags and nonactivation when showing the window, not a particular
game's handling of overlays.
