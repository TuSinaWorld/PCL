# 更新与联网配置隔离检查

Windows / .NET Framework 4.8 环境中运行 `./Run.ps1` 执行本组 25 项断言，或从仓库根目录运行 `./tests/Run-RecoveredChecks.ps1` 执行全部三组检查。统一入口为各组创建独立进程，只保留 Windows 必需环境变量，并使用空配置夹具隔离继承凭据；详细说明见 `../README.md`。

本组检查直接编译生产 `ModRecoveredUpdate.vb` / `ModRecoveredServer.vb`，Option Strict On，无源码转换。UI、网络、Loader 与应用上下文使用 stub；验证默认不联网及不自动更新、版本与设置条件、自建 JSON、同来源缓存回退及换来源隔离、相对 URL、ZIP entry 固定输出和错误 SHA256 拒绝。

没有真实 HTTP、进程替换、native 补丁或目标 EXE 执行。用于 ZIP 和散列检查的程序集是测试自身生成的 checks.dll。依赖只使用仓库已有 Newtonsoft.Json.dll；不依赖 ILSpy、反编译输出或外部目录。

编译日志、源文件散列、结果、程序集及测试 fixture 全部写入被忽略的 `bin/`。这是模块隔离验证，不代表真实应用整体构建或更新部署验证。
