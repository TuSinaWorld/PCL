# 本 fork 的开发约定

## 分支与同步

- 后续已授权的项目开发统一使用 `dev/local-client`，远程为 `origin`（`TuSinaWorld/PCL`）。该分支从已恢复的客户端代码继续开发。
- 开始前检查分支和工作区；如不在开发分支，先确认并切换。保留用户和其它任务已有的未提交改动，不重置、不覆盖。
- 每次完成已授权的改动并通过相应检查后，提交并推送到 `origin/dev/local-client`。这是用户对后续开发同步的明确要求；无需重复询问是否推送。
- 不向 `main` 或 `restore/decompiled-client` 追加开发提交，不自动合并 PR，不强推、不重写远程历史。推送失败时保留本地提交并说明阻塞。
- 当前 `.github/workflows/build.yml` 的未提交修改是之前保留的工作流草稿，未经单独处理不要混入其它提交。此前 GitHub CLI 登录缺少 `workflow` scope。

## 实现与验证

- 功能目标与边界见 `RESTORATION.md`。当前联机使用 VLan v9，EasyTier 仅保留历史回归代码；配置下发、公告、自更新、遥测、赞助权益与土豆码已禁用，不因旧配置重新启用。
- 根据变更范围运行必要检查。统一离线入口为 `tests/Run-RecoveredChecks.ps1`；当前真实回环 TCP/机器接口测试为 `tests/VlanLink/Run.ps1 -IncludeLocalTransport`，不创建网卡。`-UacProbe` 会请求人工管理员授权，只查状态；真实网卡与双设备 Minecraft 必须另行记录。历史 EasyTier 双 Core 测试不可作为 VLan 验收依据。
- 完整构建需要 Windows、支持 C# 13 的 SDK、WPF 工具链及 .NET Framework 4.8 引用程序集。纯文档改动无需重复完整运行测试。
- 区分编译、隔离行为、完整 GUI、真实服务和跨网络联机验证，不把某一项通过当作全部功能验证。

## 凭据与发布

- 实际 API Key、CDN 签名材料、账号令牌和房间口令不得作为字面量或配置明文提交到 Git。`PCL/recovered-config.local.json` 只保存在本地；公开样例使用空值。
- 用户已明确授权：正式发布可在构建时内嵌 Client ID 与获准用于此 fork 公开分发的 CurseForge Key。只通过显式私有输入和白名单资源生成，源码不含真实值；EXE 内嵌不提供保密保证。CDN 材料、账号令牌和房间口令不内嵌。
- Git 同步不等于自动发布新版本。用户要求发布时，再创建指向明确提交的版本标签和 GitHub Release；遵循用户指定的版本号和正式/预发布状态。本次指定 `fork-3.0.1` 正式版。
- 主程序的托管库和原生资源内嵌于 EXE。发布单 EXE 前核对嵌入依赖、当前构建及其 SHA-256；同时提供运行前提、按需下载和外部配置说明。
- 新标签或资产不得默默覆盖已有 Release。发布记录包含源码提交、EXE 哈希、检查结果和未验证范围；避免把验证不充分的开发版本标成稳定版。
