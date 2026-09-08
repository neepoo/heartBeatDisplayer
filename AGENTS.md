# 项目工作约定

## 项目与文档

- 本项目为 Windows WPF / .NET 10 心率悬浮工具，面向 Garmin Forerunner 255 BLE 广播；发布目标为自带运行时的 `win-x64`。
- 应用支持窗口缩放、心跳动效及最近 5 分钟统计。功能、设置、命令或发布方式发生变化时，同步修改 README.md；不要把历史计划或旧验证记录当作当前功能说明。
- 版本的唯一来源是 `src/HeartBeat.App/HeartBeat.App.csproj` 的 `Version`。`scripts/get-package-info.ps1` 统一生成包名并验证标签；不要在打包逻辑中硬编码版本。
- 本文件使用规范名称 `AGENTS.md`；更新工作约定时修改此文件，不另建 `agent.md`。

## 提交与推送

用户已授权：每次完成本项目的一次修改后，完成相关验证，创建 Git 提交并推送至本仓库的 GitHub 远程。继续执行该流程，无需重复询问是否提交或推送。

- Git 提交作者：`neepoo <neepoowzk@gmail.com>`。仅设置本仓库的 Git identity，不修改全局身份。
- 远程：`https://github.com/neepoo/heartBeatDisplayer.git`，私有仓库。
- 提交信息使用简洁中文，说明具体行为变化；可使用 `fix:`、`feat:`、`docs:` 等前缀。
- 完成后运行相关核心测试、WPF 检查及 Release 构建；报告实际通过项和未验证的硬件场景。仅改文档可按影响范围验证链接与内容；CI／打包修改必须实际验证相关流程。
- 提交源码、测试及文档，不提交 `.packages`、`.cli`、`bin`、`obj`、发布包、实际心率数据或凭据。
- 推送后核对本地与远程提交一致；禁止强制推送、覆盖他人修改或顺带提交无关改动。
- 本机 SSH 代理不可用时，本仓库通过 HTTPS 和 `gh auth git-credential` 认证；不得把 token 写入文件或命令输出。
- 用户可能正在运行旧版。生成独立版本目录的发布包，不终止用户进程，不覆盖运行中的 EXE/DLL。

## 验证与发布

- 两个测试项目都是可执行程序，必须使用 `dotnet run`，不能用 `dotnet test` 代替。WPF 检查使用 `--demo` 和独立 `--settings-dir`，不依赖真实蓝牙，也不读取正常用户设置。
- `.github/workflows/ci-release.yml` 在 main 推送、面向 main 的 PR、`v*` 标签及手动触发时构建、运行两组检查并打包。测试报告、预览和 Windows 包作为 Actions 工件保留 14 天。
- 普通提交推送不等于版本发布。需要交付新版本时更新 `Version` 与 README，提交并等对应 CI 通过，再创建匹配的 `v<Version>` 标签并推送；标签工作流成功后才算 Release 发布完成。
- Release 任务只使用内置 `GITHUB_TOKEN`，上传同次通过测试的 ZIP 和 SHA256。不要添加个人 token、扩大整个工作流的写权限或在 PR 工作流发布。
- 保持官方 Actions 固定到已核实的提交 SHA。修改工作流后检查真实 Actions 运行结果，发布后核对标签、Release 附件与提交。
- 不覆盖已发布版本，不移动或强推版本标签；更正已交付包时使用新版本。`workflow_dispatch` 只用于验证，不创建 Release。
- 本地 `publish.ps1` 默认只运行核心测试，不能代替 WPF 检查；`-SkipTests` 仅在相关测试已经通过的同一构建流程中使用。

常用验证（Windows PowerShell；受限环境可将 `DOTNET_CLI_HOME` 指向仓库 `.cli`）：

```powershell
dotnet run --project tests/HeartBeat.Tests -c Release
dotnet run --project tests/HeartBeat.App.Tests -c Release -- --demo --settings-dir artifacts/smoke-session
dotnet build HeartBeat.slnx -c Release
.\scripts\publish.ps1
```
