# 本地配置与鉴权边界隔离检查

运行 `./Run.ps1` 执行本组 28 项断言，或从仓库根目录运行 `./tests/Run-RecoveredChecks.ps1` 执行全部三组检查。统一入口为各组创建独立进程，只保留 Windows 必需环境变量，并使用空配置夹具隔离继承凭据；详细说明见 `../README.md`。要求 Windows / .NET Framework 4.8；只使用仓库已有 Newtonsoft.Json.dll。

检查环境变量优先级、本地 OAuth / API 别名、HTTPS 精确主机匹配与相似域名 / 路径 / HTTP / userinfo 拒绝、手动请求头保留、浏览器与百度 UA、CDN 标记 / 本地签名输入 / 无 secret 回退。

框架自带 vbc 仅支持 VB 2012。脚本在 `bin/generated/` 测试副本中将 NameOf 降为同名字符串、三处固定 UA / Referer 插值降为等价 String.Format；生产文件保持原样。随机数、时间和 digest 使用确定性 stub，因此验证签名输入与拼接，不验证密码算法。它不是完整项目构建。

运行阶段在独立进程内先覆盖配置路径并清除相关继承变量，只使用新生成的 fixture JSON；不读取真实本地配置或继承凭据值，也不发起 HTTP 请求。父进程环境不受影响。编译日志、源文件散列、结果、生成 DLL 与 fixture 全部写入被忽略的 `bin/`。
