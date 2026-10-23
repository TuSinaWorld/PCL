# Recovered toolbox checks

在 Windows 本地磁盘上的仓库中运行，无需完整 WPF 构建工具：

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\RecoveredToolbox\Run-Tests.ps1
~~~

也可用 PowerShell 7；脚本根据自身位置定位仓库，不要求当前工作目录。需要 Windows 自带的 .NET Framework v4 VB 编译器，优先使用 Framework64，随后尝试 Framework。

全部三组恢复检查的本地与 CI 单一入口为 `../Run-RecoveredChecks.ps1`，详细说明见 `../README.md`。统一入口为各组创建独立进程，只保留 Windows 必需环境变量，并使用空配置夹具隔离继承凭据。

- Validate-Boundaries.ps1 从真正的 PageOtherTest.xaml.vb 提取下载输入验证和清理辅助函数，编译并运行 23 项隔离夹具断言。目录联接创建不可用时会明确提示，并跳过对应 2 项，其余 21 项仍运行。
- Validate-Interop.ps1 从同一生产文件提取原生内存声明和方法，检查声明可编译，并执行 6 项 TOKEN_PRIVILEGES 大小/偏移、方法签名及命令宽度检查；不会调用原生内存操作或请求管理员权限。

生成的 harness 源码、测试程序和 fixture 全放在本目录的 bin/，已被 Git 忽略。测试不会运行 PCL、访问网络或清理真实 PCL 缓存；不需要分析目录、反编译产物或官方凭证。这些是辅助函数检查，不能替代完整 WPF 应用构建或界面运行验证。
