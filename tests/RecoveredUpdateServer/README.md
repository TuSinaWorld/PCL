# 禁用后端行为检查

运行 `./Run.ps1`，或使用统一入口 `./tests/Run-RecoveredChecks.ps1`。

本组直接编译生产 `ModRecoveredUpdate.vb` 与 `ModRecoveredServer.vb`，Option Strict On，不转换生产源码。使用合成旧服务 URL、带公告/更新的缓存、当前 EXE 夹具和替换目标，验证即使旧配置存在也不会请求、修改设置、消费更新文件或替换程序；保留的导出兼容入口必须逐字节复制当前 EXE，并覆盖旧导出缓存。

共 18 项行为断言。网络计数器和禁止执行/浏览器/原生操作的 stub 用于发现意外动作。没有真实 HTTP、PCL 执行或 native 操作。编译日志、源码散列与 fixture 均写入被忽略的 `bin/`。这组替代此前支持自建配置和更新服务器的测试。
