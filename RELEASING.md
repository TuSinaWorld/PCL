# 开发分支与运行文件发布

后续开发使用 `dev/local-client`，完成已授权改动后提交并推送到 `origin/dev/local-client`。`main` 与原 `restore/decompiled-client` 保留；本地工作流草稿不会随分支切换自动提交。

GitHub Releases 放置已构建的 `Plain Craft Launcher 2.exe`，并附 SHA-256 与使用说明。托管依赖通过程序集资源加载，WebP 等原生库在使用时提取，因此发布目录不需要旁置 PCLCS、MeloongCore 等 DLL。单 EXE 已通过隔离依赖加载和 WPF 全局资源初始化检查。

`Plain Craft Launcher 2.exe.config` 可作为独立的可选资产提供：它保留原有绑定重定向和宽松 HTTP 响应头解析设置，不含凭据。审计验证没有该文件也能初始化；遇到旧服务器兼容问题时，可把它放在 EXE 同目录。

运行前提与边界：

- Windows 和 .NET Framework 4.8；首轮验证平台为 Windows 11 x64。
- EXE 首次运行会生成本地设置、缓存和必要文件，不能将“单 EXE 分发”理解为运行时不产生文件。
- Minecraft、Java 和 EasyTier 不打包进启动器；按需下载或使用本机已有文件。EasyTier 使用固定官方版本和 SHA-256。
- 用户已授权 fork-3.0.1 在构建时内嵌 Client ID 与有权公开分发的 CurseForge Key；只从显式私有输入生成两字段资源，CDN 材料不内嵌，真实值不进入 Git。环境变量与本地 JSON 仍可覆盖，配置方式见 `RESTORATION.md`。
- 遵循用户指定的发布状态：`fork-3.0.1` 为正式版，Release 说明保持简洁。删除用户明确要求移除的旧 Release；不把发布状态当作完整 GUI 或公网两机验收结论。

发布使用显式标签和目标提交，禁止让 GitHub 默认从 `main` 创建错误标签。发布后核对远程标签、Release 状态、资产大小和 SHA-256。
