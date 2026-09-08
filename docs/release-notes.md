## 跨平台预览版

- 统一 Avalonia 界面，保留实时 BPM、五分钟曲线及统计、心跳动效、窗口缩放和 Windows 旧设置兼容。
- 新增 macOS Apple Silicon / Intel 和 Ubuntu x64 X11 入口，分别接入 CoreBluetooth 和 BlueZ。
- 四种自带运行时的发布包，均附 SHA256；CI 检查核心行为、共享界面、原生窗口与打包启动。

Windows 用户下载 win-x64.zip；Mac 按芯片选择 osx-arm64.zip 或 osx-x64.zip；Linux 下载 linux-x64.tar.gz。解压后保留全部文件，先退出旧版再启动。

macOS/Linux 当前为预览支持，真实手表连接、休眠恢复和多屏操作尚未实机验收。Linux 首轮针对 Ubuntu 24.04 X11；Wayland 不保证完整悬浮能力。Mac 包仅临时签名，未做 Apple Developer ID 签名或公证，首次启动可能需要在系统设置中允许；真实蓝牙连接需要授权。

原 Windows 稳定版 v1.1.0 保留。完整操作、Linux 依赖、Mac 首次启动与验证范围见仓库 README。
