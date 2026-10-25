# 本地功能与恢复说明

本分支以公开 PCL 2.99.0.0 为基线，参考提供的 2.12.8.2 程序集恢复客户端代码，并按 fork 的用途重建本地功能。它不是作者私有仓库的完整副本；主题配色和联机邀请码为本 fork 的实现。

## 可用范围

- **主题**：0–14 全部本地开放，14 为自定义；保留名称、图案、渐变和背景配色。除默认主题外的配色是重建值。主题 12 动态变色，13 静态多彩；色调、饱和度、亮度、渐变滑块保存后立即生效。历史 42 或非法主题回退默认值。
- **Minecraft 与整合包**：已移除 2.99 的下载限制，恢复新实例、组件和受支持整合包的安装入口。明确使用 Quilt 的包（含内嵌版本 JSON、嵌套包）在创建实例和安装依赖前拒绝；已有本地 Quilt 实例的识别与启动保持不变。
- **资源与账号**：保留微软登录、CurseForge、Modrinth、下载镜像和用户选择的联网主页。凭据只从本地文件或环境变量读取。
- **百宝箱**：保留自定义下载、今日人品、确认后的缓存清理、内存整理和本地回声洞。不包含官方投稿、窗口恶作剧或开发者私人工具。
- **整合包导出**：保留本地导出配置及附带启动器选项；附带当前 fork 的 EXE，不获取官方最新版，也不复制本地服务凭据。

## 不支持的功能

赞助权益和土豆码不受支持，相关输入/生成入口隐藏，残留调用只显示说明。主题不读取赞助等级、土豆码或解锁存档；设备识别仍用于本地设置兼容。

配置下发、公告、启动器自更新与遥测已停用，设置入口隐藏。旧服务地址、缓存公告及自动更新开关不会重新启用它们；旧 `--update` 调用退出失败，不替换文件。升级请手动替换启动器。全局设置导入/导出隐藏；整合包导出页面的本地导入/导出仍可用。

## EasyTier 联机：双方使用本 fork

无需 PCL 配置服务器。首次联机自动下载固定的官方 **EasyTier v2.6.4**，按系统架构选择 x86_64 / i686 / arm64 包，核对内置 SHA-256 后暂存安装，保留旧安装用于恢复。安装运行所需 EXE、DLL/SYS，不安装系统服务、不修改防火墙。

- 房主默认从 `https://uptime.easytier.cn/api/nodes?page=1&per_page=1000` 选择公共节点。请求失败时可使用七天内的缓存；没有有效缓存时提示重试或填写自定义节点。
- 设置中的自定义节点支持逗号分隔的连接 URI；房间码会携带实际节点，加入者无需再查询清单或手动填写同一节点。
- 房主在 Minecraft 选择“对局域网开放”，将显示的端口填入联机页面，创建后复制 `PCLF1:` 开头的房间码给朋友。朋友在同版 fork 中选择加入并粘贴。
- 房间码为 Base64URL JSON，字段为 `v`、`network`、`secret`、`port`、`peers`。房间名和口令使用随机生成值；它包含加入权限，请只发给需要联机的人。日志隐藏房间码和口令。
- 使用免 TUN 模式及本机 TCP/UDP 端口转发，恢复节点、人数、Ping、NAT、中继信息及本机 Minecraft 房间广播。退出、取消或失败只停止本次启动的组件进程。
- 不承诺与原版 PCL 的邀请码互通；旧房间码提示使用同版 fork 重建。公共节点在线状态和实际 NAT 条件仍会影响连接。

官方组件：[v2.6.4](https://github.com/EasyTier/EasyTier/releases/tag/v2.6.4)、[附件摘要](https://github.com/EasyTier/EasyTier/releases/expanded_assets/v2.6.4)、[免 TUN 模式](https://easytier.cn/guide/network/no-root.html)。

## 本地凭据配置

将 `recovered-config.example.json` 复制到可执行文件旁的 `PCL/recovered-config.local.json`，按需填写；该文件被 Git 忽略，不能提交实际值。源项目中存在该文件时，本地构建会复制到输出目录；交付 ZIP 不包含它。

可用 `PCL_RECOVERED_CONFIG` 指定路径。环境变量优先于 JSON；显式空值禁用对应配置。

| JSON 字段 | 环境变量 | 用途 |
|---|---|---|
| `OAuthClientId` | `PCL_MS_CLIENT_ID` | 微软 OAuth 应用标识 |
| `CurseForgeAPIKey` | `PCL_CURSEFORGE_API_KEY` | 官方资源 API 请求头 |
| `CdnSigningSecret` | `PCL_CDN_SIGNING_SECRET` | 带 `{CDN}` 标记地址的本地签名材料 |
| `CdnSigningUserId` | `PCL_CDN_SIGNING_USER_ID` | 签名标识，默认 0 |

旧的 `ServerConfigUrl`、`UpdateBaseUrl`、`UpdateAllowAutomatic`、`UpdateSha256` 和 `PotatoPublicKey` 不再用于启用相应功能。保留 `OpenSource` 分支标识及 `PCLDebug` 设置隔离，不迁移或删除官方设置。

## 构建与检查

初始化子模块，使用支持 C# 13 的 SDK、WPF 工具链和 .NET Framework 4.8 引用程序集：

```powershell
git submodule update --init --recursive
dotnet restore PCLCS/PCLCS.csproj
msbuild "Plain Craft Launcher 2/Plain Craft Launcher 2.vbproj" -p:Configuration=Release
./tests/Run-RecoveredChecks.ps1
```

统一测试入口涵盖禁用后端、本地配置/请求头、百宝箱、主题、整合包支持检查和联机。它们在独立隐藏进程使用合成配置，不读取实际凭据。真实双 Core 的本机 TCP/UDP 传输另有显式集成入口，见 `tests/LocalLink`。

测试与编译不能替代所有线上服务验证。首轮平台为 Windows 11 x64；其他架构的运行、跨网络 NAT 打洞/中继和两台机器上的 Minecraft 加入应单独记录。辅助 JAR 和第三方组件仍沿用上游公开依赖或二进制，不声称其全部原始源码已恢复。
## 2026-10-25 验证结果

- Debug、Release 的三个 C# 依赖及全部 VB/WPF 完整构建通过。
- 从仓库外目录通过 PowerShell 7 与 Windows PowerShell 5.1 运行统一入口，两次均为退出码 0：八组共 389 项行为断言，另有 6 项原生互操作声明/布局检查。
- 使用官方 v2.6.4 x64 包，真实验证单房主在零玩家时可准备房间，以及双 Core CLI、TCP/UDP 数据往返；不修改系统服务、防火墙或 TUN。
- 单独复制 Release EXE 后执行不支持的 --update，退出码 1，证明无需旁置依赖 DLL 即可进入禁用分支；未进行真实更新替换。
- 本机公共节点 API 域名解析失败；跨互联网两台机器和真实 Minecraft 房间加入尚未验证。登录/API 凭据有效性、原生内存整理没有实测。