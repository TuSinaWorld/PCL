# 本地功能与恢复模块检查

Windows / .NET Framework 4.8 的统一入口，可从任意工作目录运行：

```powershell
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\tests\Run-RecoveredChecks.ps1
```

入口也可由 PowerShell 7 执行，各组在独立隐藏的 Windows PowerShell 进程中运行。非零退出码立即失败；子进程只继承必需的 Windows 目录/系统变量，使用新建空配置和合成凭据，不读取部署配置，不启动 PCL，不访问远程服务，也不执行内存清理。

| 套件 | 检查内容 |
|---|---|
| `RecoveredUpdateServer` | 旧地址/缓存不触发请求、公告或替换；导出兼容入口只复制当前 EXE |
| `RecoveredConfiguration` | 本地配置优先级、请求头范围、CDN 签名 |
| `RecoveredToolbox` | 下载输入和缓存清理边界，原生互操作声明/布局 |
| `LocalThemes` | 真实 WPF 颜色/画刷、15 个本地主题、动态/静态配色、自定义参数与持久化 |
| `LocalModpacks` | 真实 ZIP/JSON 支持检查；各格式 Quilt、嵌套包和正向包夹具 |
| `LocalLink` | 邀请码、节点、CLI、组件校验/安全提取/安装，合成子进程超时取消清理 |
| `LocalLinkNavigation` | 真实剪贴板激活处理器，长房间码、用户确认、指纹去重及口令不入日志 |
| `LocalLinkInput` | 实际 WPF 文本输入、8192 长度、敏感返回值的日志脱敏及普通弹窗兼容 |
| `EmbeddedConfiguration` | 真实程序集资源加载、白名单、覆盖顺序、空值禁用及无资源回退 |
| `EmbeddedConfigurationBuild` | 构建资源净化、CDN 排除、输入边界及增量输出 |

使用 Windows 自带的 Framework 编译器及仓库内 Newtonsoft.Json.dll；主题组还使用 Windows WPF 程序集。无需分析目录、反编译工具或固定盘符。日志、临时源码、程序集、夹具只写入被忽略的各组 `bin/`。

真实官方 EasyTier 双 Core 本机传输是单独的显式集成检查，见 `LocalLink/README.md`；不混入日常离线套件。隔离检查、完整项目编译、线上登录以及跨机器游戏联机属于不同验证层次，报告中分别说明。

2026-10-25：八组共389项行为断言与6项互操作条件，从仓库外目录在PowerShell 7及Windows PowerShell 5.1均通过；单房主就绪及真实双Core TCP/UDP传输的独立集成检查也通过。

内嵌配置新增两组：126项运行时断言与262项构建净化断言；当前十组共777项行为断言，另有6项互操作条件。普通测试只使用合成配置。
