# 三平台迁移实施记录

目标：2.0.0-preview.1，.NET 10 / Avalonia 12.1.2，共享 Core 与 UI，Windows x64、macOS 14+ arm64/x64、Ubuntu 24.04 x64 X11。保留现有功能、Windows 设置路径及命令行参数。macOS/Linux 真实蓝牙待实机验证，不承诺 Wayland。Mac 首轮仅临时签名，不做 Developer ID 公证。

## 任务

1. 共享 Avalonia UI 和 Windows 迁移，现有核心与窗口行为回归。
2. macOS CoreBluetooth / AppKit 接入，独立 net10.0-macos 启动项目及 .app。
3. Linux BlueZ / Tmds.DBus.Protocol 0.95.1 / X11 接入。
4. 三端构建、打包、CI、文档、Release；四种工件及 SHA256。

## 共享接口约定

共享项目 `src/HeartBeat.Desktop`，命名空间 `HeartBeat.Desktop`。保留 Core 的 IHeartRateTransport/IHeartRateConnection。

`DesktopBootstrap.Run(string[] args, Func<IHeartRateTransport> transportFactory, Func<IDesktopIntegration> desktopFactory)` 返回进程退出码。工厂必须惰性调用，--demo 不初始化蓝牙。

`IDesktopIntegration : IDisposable`：`string? Warning { get; }`，`bool SupportsClickThrough { get; }`，`void Attach(Avalonia.Controls.Window window)`，`void Apply(bool locked)`，`event Action? ToggleVisibility`，`event Action? ToggleLock`。共享层负责窗口尺寸/屏幕约束与托盘；平台负责原生置顶、穿透、不激活、快捷键。

三端入口：现有 HeartBeat.App 变成 Windows 入口；新增 HeartBeat.Mac 与 HeartBeat.Linux。版本迁移到 Directory.Build.props。平台入口只引用共享 UI 与相应本地 API。

## 验证

Core 21 项基线；共享 Headless 检查布局/设置/模拟数据/统计；各平台原生窗口 smoke（Linux 使用 Xvfb + 窗口管理器）；包启动、完整性与 SHA256；GitHub 四架构 CI；明确区分自动检查与真实硬件测试。

## 执行台账

- 起点：8a1d58a；在 feature/cross-platform 分支工作，原稳定标签不变。
- 交叉依赖检查：UI 输出 DesktopBootstrap/IDesktopIntegration，Mac/Linux 按以上固定契约消费；平台目录不交叉编辑；CI 等待各入口就绪。四项任务各自实现与验收一致。
- 发布前合并 main，标签必须与统一 Version 一致；不覆盖既有 Release。
- Windows：Core21、共享31、原生41+额外Win32 8通过；压缩包解压启动通过。Linux协议10、完整入口编译通过；Mac生命周期4、官方引用包编译通过。
- 审查：Mac NSWindow 与 NSPanel 差异需要针对实际窗口修复 key/main 焦点；共享设置恢复提示丢失及 Mac launcher退出码两项细节一并修正。三端云端验证未完成前不创建版本标签。
- 四架构完整 CI 已通过：34192035328（实现提交 e57331b）。共享33、配置无反射4、Windows原生43+额外8、Linux原生45、Mac两架构各49及双入口启动退出检查通过。
- 云端修复：Mac相对应用包目录避开工作负载签名缓存覆盖库文件；设置使用生成式JSON元数据兼容发布裁剪；截图等待实际窗口尺寸。最终限定复审未发现剩余重要问题。
- 验证细节与预览支持边界见 docs/verification.md；main合并及版本标签发布继续使用四架构门槛，保留v1.1.0。
