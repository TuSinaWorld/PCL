# 本地功能与恢复说明

后续开发统一在 `dev/local-client` 分支提交并同步到 fork；原恢复分支及 PR 保留。开发与发布规则见 `AGENTS.md`、`RELEASING.md`。

本分支以公开 PCL 2.99.0.0 为基线，参考提供的 2.12.8.2 程序集恢复客户端代码，并按 fork 的用途重建本地功能。当前正式版本为 **fork-3.0.1**（程序集 3.0.1.0）。它不是作者私有仓库的完整副本；主题配色和联机邀请码为本 fork 的实现。

## 可用范围

- **主题**：0–14 全部本地开放，14 为自定义；保留名称、图案、渐变和背景配色。除默认主题外的配色是重建值。主题 12 动态变色，13 静态多彩；色调、饱和度、亮度、渐变滑块保存后立即生效。历史 42 或非法主题回退默认值。
- **Minecraft 与整合包**：已移除 2.99 的下载限制，恢复新实例、组件和受支持整合包的安装入口。明确使用 Quilt 的包（含内嵌版本 JSON、嵌套包）在创建实例和安装依赖前拒绝；已有本地 Quilt 实例的识别与启动保持不变。
- **资源与账号**：保留微软登录、CurseForge、Modrinth、下载镜像和用户选择的联网主页。支持构建时内嵌获授权的 Client ID／CurseForge Key，并允许环境变量或本地文件覆盖。
- **百宝箱**：保留自定义下载、今日人品、确认后的缓存清理、内存整理和本地回声洞。不包含官方投稿、窗口恶作剧或开发者私人工具。
- **整合包导出**：保留本地导出配置及附带启动器选项；附带当前 fork 的 EXE，不获取官方最新版，也不复制本地服务凭据。

## 不支持的功能

赞助权益和土豆码不受支持，相关输入/生成入口隐藏，残留调用只显示说明。主题不读取赞助等级、土豆码或解锁存档；设备识别仍用于本地设置兼容。

配置下发、公告、启动器自更新与遥测已停用，设置入口隐藏。旧服务地址、缓存公告及自动更新开关不会重新启用它们；旧 `--update` 调用退出失败，不替换文件。升级请手动替换启动器。全局设置导入/导出隐藏；整合包导出页面的本地导入/导出仍可用。

## VLan 联机融合（fork-3.0.2）

联机入口已替换为用户自己的 VLan v9 后端。现有 v9 服务端代码及协议保持不变；旧 EasyTier 模块仅留作历史回归夹具，产品路径不再获取公共节点或启动 EasyTier。当前提供 Windows x64 的融合组件，随 fork-3.0.2 发布；保留 fork-3.0.1 正式 Release。

- 房主首次在设置中填写服务器地址、端口（默认 11510）和鉴权密码，可测试鉴权；凭据按 Windows 当前用户 DPAPI 加密保存。
- 创建房间优先读取当前 Minecraft 日志中的局域网端口，识别失败可手填。默认自动生成随机房间密码，也可在设置中选择不设房间密码。
- `PCLV1:` 邀请码带完整服务器凭据、房间信息及游戏主机会话。持码者可以取得并复用服务器密码；加密只遮蔽明文，请仅分享给可信好友。不承诺客户端过期或撤销能让服务端密码失效。
- 加入时展示服务器端点并允许仅本次使用或加密记住；不要求额外导入服务器配置。旧 `PCLF1:` 提示房主重新创建。
- 房间不存在时提示获取新码，不自动创建替代房间。无密码房间直接加入，但仍要求服务器鉴权；密码错误可在失败卡片重新输入。房间 ID 复用通过房主随机挑战校验阻止误连。
- 游戏代理只监听本机 IPv4/IPv6 回环地址，将 Minecraft TCP 转发到房主虚拟 IP；本机广播使游戏可发现房间，另提供复制地址。当前不承诺其它游戏的本地 UDP 代理。
- PCL 与游戏保持普通权限，辅助进程通过 `runas` 申请 UAC。实际联机使用 Wintun，会配置临时网卡、路由和防火墙规则；退出时清理，不安装常驻服务。清理未完成不启动第二个组件。
- 关闭启动器会确认并等待组件清理；取消关闭继续联机。启动器意外退出或命名管道断开时，组件走正常清理，不强杀驱动线程。
- 显示成员、实际传输方式、RTT、探测超时率。保留最多 8 人、IPv4、中心中继；不提供 P2P 或 EasyTier NAT 状态。高级设置可选择下次建房使用 TCP Relay。

融合 EXE 内嵌经校验的 CLI + 官方 Wintun 包，首次联机自动释放并逐文件校验，避免依赖另行发布的机器接口下载资产；无额外手动下载步骤。包的精确 CLI 源码在 [VLan 集成分支](https://github.com/TuSinaWorld/vlan-connect/tree/feat/pcl-integration)，接口见该分支的 `docs/pcl-integration.md`。打包脚本 `build/Update-VlanBundle.ps1` 要求 `--version` 带 `protocol=9` 和 `machine=1`，并记录源码提交。

运行 `tests/VlanLink/Run.ps1` 检查合成邀请码、DPAPI 和组件修复；加 `-IncludeLocalTransport` 执行真实回环 TCP、房主会话和 CLI 管道/退出测试。它们不连接实际服务端、不创建虚拟网卡。真实 UAC、Wintun 和双设备 Minecraft 验收另行记录，不将隔离测试等同于线上可玩性证明。

## 构建时内嵌与本地覆盖

fork-3.0.1 与 fork-3.0.2 正式发布构建内嵌 Client ID 和获授权的 CurseForge Key，无需另放 JSON 即可读取这两项默认值。CDN 材料不内嵌。读取顺序为环境变量、本地 JSON、内嵌默认值、调用方默认值；显式空值禁用对应项。

普通源码构建默认不携带凭据。要生成内嵌构建，准备私有 JSON（只需 `OAuthClientId` 与 `CurseForgeAPIKey`），并显式传入路径：

```powershell
msbuild "Plain Craft Launcher 2/Plain Craft Launcher 2.vbproj" -p:Configuration=Release -p:PclEmbedClientConfigPath=C:\private\client.local.json
```

准备脚本只输出这两个白名单字段，资源位于被忽略的 `obj`；不把真实值写入源码、日志或发布说明。内嵌值可以从 EXE 中提取，不提供保密保证。

如需覆盖内嵌值，将 `recovered-config.example.json` 复制到可执行文件旁的 `PCL/recovered-config.local.json`，按需填写；该文件被 Git 忽略，不能提交实际值。源项目中存在该文件时，本地构建会复制到输出目录；发布资产不包含它。只需修改哪一项就保留哪一项，留空会禁用对应项。

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

统一测试入口涵盖禁用后端、本地配置/请求头、百宝箱、主题、整合包支持检查和联机。它们在独立隐藏进程使用合成配置，不读取实际凭据。VLan 的本机 TCP/管道集成另有显式入口，见 `tests/VlanLink`；`tests/LocalLink` 的 EasyTier 双 Core 入口仅用于历史基线。

测试与编译不能替代所有线上服务验证。首轮平台为 Windows 11 x64；其他架构的运行、跨网络 NAT 打洞/中继和两台机器上的 Minecraft 加入应单独记录。辅助 JAR 和第三方组件仍沿用上游公开依赖或二进制，不声称其全部原始源码已恢复。
## 2026-10-25 验证结果

- Debug、Release 的三个 C# 依赖及全部 VB/WPF 完整构建通过。
- 从仓库外目录通过 PowerShell 7 与 Windows PowerShell 5.1 运行统一入口，两次均为退出码 0：八组共 389 项行为断言，另有 6 项原生互操作声明/布局检查。
- 使用官方 v2.6.4 x64 包，真实验证单房主在零玩家时可准备房间，以及双 Core CLI、TCP/UDP 数据往返；不修改系统服务、防火墙或 TUN。
- 单独复制 Release EXE 后执行不支持的 --update，退出码 1，证明无需旁置依赖 DLL 即可进入禁用分支；未进行真实更新替换。
- 本机公共节点 API 域名解析失败；跨互联网两台机器和真实 Minecraft 房间加入尚未验证。登录/API 凭据有效性、原生内存整理没有实测。

## fork-3.0.1 验证补充

正式Release内嵌的资源仅包含Client ID与获授权CurseForge Key，CDN材料排除；无外部配置的独立EXE可读取默认值并用于实际请求头。十组777项行为断言及6项互操作条件通过；构建输入撤销后资源不会残留。最小在线检查中CurseForge搜索和微软设备登录准备均返回200，未进行用户登录。


## 2026-10-29 VLan 实测补充

已完成真实 UAC 允许、临时 v9 服务端鉴权、有/无密码房间、错误房间密码和失效房间处理。使用无网卡协议端验证远程 RawUDP、KCP、TCP Relay 与真实 Wintun 数据往返；正常退出及活动网卡控制通道丢失均清理完成。没有修改服务端。仅一台设备，完整 GUI 与双设备 Minecraft 实际入服仍未验收。可复现入口与证据边界见 [VLan 验收说明](docs/VLAN-INTEGRATION.md)。
