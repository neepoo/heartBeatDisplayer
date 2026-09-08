# 项目工作约定

## 产品与架构

- HeartBeat 是 .NET 10 / Avalonia 12.1.2 桌面心率悬浮工具，接收 Garmin Forerunner 255 的 BLE 心率广播。Core 为无平台依赖的协议、历史、统计和重连逻辑。
- 共享 UI 位于 HeartBeat.Desktop；HeartBeat.App 为 Windows 入口，HeartBeat.Mac 为 macOS 入口，HeartBeat.Linux 为 Linux 入口。平台 API 不得进入共享项目。
- 当前跨平台版本为预览阶段：Windows x64；macOS 14+ arm64/x64；Ubuntu 24.04 x64 X11。不承诺 Wayland、独占全屏和未实测的蓝牙硬件行为。
- 保留窗口缩放、动效、五分钟统计、模拟模式、Windows 1.x 设置兼容。`--demo` 不得初始化真实蓝牙或触发权限请求。
- 版本唯一来源是 `Directory.Build.props`。`get-package-info.ps1` 负责统一版本、RID、入口和包名；禁止各平台硬编码独立版本。
- 行为、设置、命令、依赖或发布方式改变时，同步 README.md 与本文件；旧计划和旧验证记录不是当前功能说明。文件名保持 `AGENTS.md`，不另建 agent.md。

## 提交与推送

用户已授权每次完成一次修改后验证、提交并推送，无需重复确认。

- 作者：`neepoo <neepoowzk@gmail.com>`，仅设置本仓库身份；提交信息使用简洁中文。
- 公开远程：`https://github.com/neepoo/heartBeatDisplayer.git`；使用现有 gh 认证和 HTTPS credential helper，不输出或保存 token。
- 源码、测试、脚本和文档入库；缓存、bin/obj、artifacts、用户设置、实际心率数据、设备扫描结果及签名凭据不入库。
- 不强推、不覆盖他人工作；核对远程与本地提交一致。迁移期间可在功能分支提交推送，CI 通过后合并 main。
- 保留正在运行的用户程序。打包写入唯一暂存目录，不清空旧发布目录或终止用户进程。

## 验证与分发

- .NET SDK 使用 global.json 指定的 10.0.1xx 补丁线，PowerShell 脚本要求 7+。Mac CI 固定 Xcode 26.0.1 与 macos workload set 10.0.100。
- 所有测试项目以可执行程序运行，使用 `dotnet run`。`scripts/test.ps1` 执行元数据、核心、共享 Headless、相关平台检查和平台构建；不能用 `dotnet test` 替代。
- Headless 检查不证明原生桌面能力。`scripts/smoke-package.ps1` 校验并解压本次发布包，再启动实际窗口，使用 `--demo --smoke-test --settings-dir` 独立目录；必须检查报告完整标记与退出码。
- Linux 原生窗口检查需要 X11 与窗口管理器，CI 使用 Xvfb/Openbox；Mac 使用 .app 启动。手表连接、权限弹窗、休眠、多屏及游戏需要目标实机，未验证时明确记录。
- `.github/workflows/ci-release.yml` 运行四架构矩阵；报告、预览和包保留 14 天。官方 Actions 固定已核实的 SHA，修改后检查真实云端运行。
- `publish.ps1` 默认先运行当前平台检查；`-SkipTests` 仅允许前置检查已经通过的同一流程。Win ZIP、Mac .app ZIP、Linux tar.gz 均自带运行时并有 SHA256，Mac/Linux 保留执行权限。
- 只有推送匹配统一 Version 的 `v<Version>` 标签才发布 Release；四个任务都通过才上传。预览版用后缀版本，保留原稳定版。不移动标签或覆盖已交付附件。
- 发布任务使用内置 GITHUB_TOKEN 的 contents:write；PR/普通构建只读。当前 Mac 仅 ad hoc 签名，未公证；不擅自加入付费签名流程。
- 发布后核对版本、四个包、四份校验文件、预发布状态和对应提交。同步 docs/verification.md 中真实验证证据。

常用命令（在目标操作系统执行）：

```powershell
pwsh -File scripts/test.ps1
pwsh -File scripts/publish.ps1
# 只针对已完成当前检查的构建：
pwsh -File scripts/publish.ps1 -SkipTests -RuntimeIdentifier win-x64
pwsh -File scripts/smoke-package.ps1 -RuntimeIdentifier win-x64
```
