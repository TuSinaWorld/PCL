# 恢复模块隔离检查

Windows / .NET Framework 4.8 环境中的本地与 CI 单一入口：

~~~powershell
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\tests\Run-RecoveredChecks.ps1
~~~

入口根据脚本自身位置定位文件，使用绝对脚本路径时可从任意工作目录运行。它依次在三个独立 Windows PowerShell 进程中执行更新与联网配置、本地配置与请求头、百宝箱检查；保留各组已有的隔离运行方式。每组退出码非零时立即失败，完整成功时退出 0。

统一入口本身也可由 PowerShell 7 (`pwsh`) 执行。命令中的 `-ExecutionPolicy Bypass` 仅针对本次子进程，不更改用户或机器的执行策略；入口不修改父进程环境或用户环境变量。子进程隐藏运行，不创建桌面控制台窗口。

子进程环境只复制必需的 Windows 系统与目录变量，不继承 PCL 配置、API Key 或其他服务凭据。入口为子进程设置新建的空配置夹具，本地配置组的运行进程会继续替换为自己的合成夹具。入口不读取真实本地配置或凭据；测试不发出 HTTP 请求、不执行 PCL、不替换运行中的程序、不调用原生内存清理。

当前检查数量：更新与联网配置 25 项、本地配置与请求头 28 项、百宝箱边界 23 项（合计 76 项断言），以及原生互操作声明的 6 项布局/签名检查。目录联接不可创建时，百宝箱边界检查会明确提示并跳过 2 项，其余 21 项仍运行。

2026-10-23 已从仓库外的 `C:\Windows\Temp` 分别通过 Windows PowerShell 5.1 和 PowerShell 7 调用统一入口：三组退出码均为 0，76 项断言和 6 项互操作声明条件全部通过，目录联接用例正常执行。

需要 Windows 自带的 .NET Framework v4 VB 编译器与仓库内已有的 Newtonsoft.Json.dll，不需要完整 WPF 工具链、分析目录、反编译产物或特定盘符。所有生成的程序集、源码副本、结果与夹具都位于 Git 已忽略的 `tests/bin/` 或各组 `bin/`。本入口属于模块隔离验证，不能替代完整 WPF 构建、界面操作或线上服务验证。
