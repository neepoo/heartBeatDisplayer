# 项目工作约定

用户已授权：每次完成本项目的一次修改后，完成相关验证，创建 Git 提交并推送至本仓库的 GitHub 远程。继续执行该流程，无需重复询问是否提交或推送。

- Git 提交作者：`neepoo <neepoowzk@gmail.com>`。仅设置本仓库的 Git identity，不修改全局身份。
- 远程：`https://github.com/neepoo/heartBeatDisplayer.git`，私有仓库。
- 提交信息使用简洁中文，说明具体行为变化；可使用 `fix:`、`feat:`、`docs:` 等前缀。
- 完成后运行相关核心测试、WPF 检查及 Release 构建；报告实际通过项和未验证的硬件场景。
- 提交源码、测试及文档，不提交 `.packages`、`.cli`、`bin`、`obj`、发布包、实际心率数据或凭据。
- 推送后核对本地与远程提交一致；禁止强制推送、覆盖他人修改或顺带提交无关改动。
- 本机 SSH 代理不可用时，本仓库通过 HTTPS 和 `gh auth git-credential` 认证；不得把 token 写入文件或命令输出。
- 用户可能正在运行旧版。生成独立版本目录的发布包，不终止用户进程，不覆盖运行中的 EXE/DLL。

常用验证：

```powershell
dotnet run --project tests/HeartBeat.Tests -c Release
dotnet run --project tests/HeartBeat.App.Tests -c Release -- --demo --settings-dir artifacts/smoke-session
dotnet build HeartBeat.slnx -c Release
.\scripts\publish.ps1
```
