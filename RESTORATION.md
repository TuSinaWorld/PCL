# 客户端实现恢复说明

这份代码以最新公开版本 **2.99.0.0**（`6b9a50df911c9d68b69b24c979efba477fb14b67`）为基线，参考用户提供的 **2.12.8.2** 托管程序集及同版本公开源码，手工移植原先未公开的客户端流程。它是适配后的恢复实现，不是作者原始私有仓库的副本。

输入程序集 SHA-256：`FF36C5D67CF5CD97322E7639CB54533410303C0DCEBA17F7B21FEB1D5475F8FC`。同版本公开源码提交：`9be217fe12ae435a11eb25dae4e58bdc511879aa`。

## 已恢复的部分

- **网络请求**：CDN URL 的时间戳、随机值和摘要签名；请求头设置及指定主机的 CurseForge API Key 注入。签名材料和凭据从本地配置读取。
- **更新**：检查自建更新源、下载进度、完整 EXE / ZIP / 增量包处理、SHA-256 核对、暂存、等待旧进程退出、备份替换和可选重启。
- **联网配置**：JSON 配置加载、同一来源的离线缓存、公告条件、按钮及动作；联机页面可消费已配置的 `Link` 信息。
- **整合包导出**：下载配置指定的 launcher，或者在没有更新源时嵌入当前 fork 构建。缓存与官方 `Latest.exe` 分开。
- **主题与身份**：主题状态检查、已有绘制分支、设备识别、本地特征缓存和保留 ECDSA 验证的土豆码解析。
- **百宝箱**：自定义下载、今日人品、用户确认后的 PCL 缓存清理、系统内存整理、回声洞和随机提示；页面根据当前控件重新实现。

代码分别位于 `Modules/ModRecoveredConfig.vb`、`ModRecoveredUpdate.vb`、`ModRecoveredServer.vb`、`ModRecoveredTheme.vb`、`ModRecoveredIdentity.vb` 和 `Pages/PageOther/PageOtherTest.xaml[.vb]`。

## 运行配置

将根目录的 `recovered-config.example.json` 复制到 **可执行文件旁的 `PCL/recovered-config.local.json`**，填写所需字段。这个文件已被 Git 忽略；在源码项目目录下存在同名文件时，构建会将其复制到输出目录。

也可以通过 `PCL_RECOVERED_CONFIG` 指定其他本地 JSON 路径。环境变量的优先级高于 JSON；显式空值可用于禁用某项配置。

| JSON 字段 | 对应环境变量 | 用途 |
|---|---|---|
| `OAuthClientId` | `PCL_MS_CLIENT_ID` | 微软 OAuth 应用标识 |
| `CurseForgeAPIKey` | `PCL_CURSEFORGE_API_KEY` | CurseForge 请求头 |
| `CdnSigningSecret` | `PCL_CDN_SIGNING_SECRET` | 带 `{CDN}` 标记地址的签名材料 |
| `CdnSigningUserId` | `PCL_CDN_SIGNING_USER_ID` | CDN 签名标识，默认 `0` |
| `ServerConfigUrl` | `PCL_SERVER_CONFIG_URL` | 自建 JSON 配置服务 |
| `UpdateBaseUrl` | `PCL_UPDATE_BASE_URL` | 更新 URL 的基址，或直接指向 EXE / ZIP |
| `UpdateAllowAutomatic` | `PCL_UPDATE_ALLOW_AUTOMATIC` | 是否响应后台更新请求，默认关闭 |
| `UpdateSha256` | `PCL_UPDATE_SHA256` | 可选的更新包 SHA-256 覆盖值 |
| `PotatoPublicKey` | `PCL_POTATO_PUBLIC_KEY` | 可选的验证公钥覆盖值；不填写则使用样本及公开旧版 MeloongCore 的公钥 |

原始官方凭据和签名秘密不在 Git 源码内。CI 只写入构建提交号，服务配置在运行时读取。没有配置服务地址时，不会自动访问原始官方配置服务或用官方更新覆盖 fork。

## 自建配置示例

```json
{
  "Version": 1,
  "Update": {
    "Release": {
      "VersionCode": 414,
      "DownloadUrls": ["https://updates.example.com/launcher.zip"],
      "Format": "zip",
      "Executable": "Plain Craft Launcher 2.exe",
      "Sha256": "填写下载包的64位十六进制SHA256",
      "ExecutableSha256": "填写解包后EXE的64位十六进制SHA256"
    }
  },
  "Notices": []
}
```

`Link` 内容仍需符合现有联机页面的服务结构。未知公告条件默认不满足；剪贴板、打开网页和退出等交互动作需要用户选择。远程静默修改设置与自动更新需要额外本地启用。

## 保留的版本差异与恢复边界

- 保留 **2.99.0.0 的 Minecraft 下载限制**，没有用旧版反编译代码覆盖后续公开业务变更。
- 保留 `OpenSource` 分支标识及 `PCLDebug` 设置隔离，避免写入官方 PCL 的设置分支。
- 所给 EXE 的 `ThemeRefresh` 本身强制默认主题，`ThemeUnlock` 返回 False，`CurrentRank` 返回 None，`GeneratePotatoCode` 为空，输入入口只作提示。移植没有把它们改成无条件授权或伪造生成器。
- 设备识别从全零占位改为真实识别。已有开源构建使用全零识别码写出的旧加密数据可能无法直接读取；没有自动修改或迁移已有注册表数据。
- 回声洞恢复了可辨认的非敏感文本，随机选择做了适配；没有复制持续控制窗口等恶作剧、投稿到官方表单或开发者私人工具。
- Java 辅助组件和第三方库继续使用上游附带资源 / 公开依赖，没有声称恢复它们的全部原始源码。服务端程序、动态配置及样本未包含的私有实现仍不可从此 EXE 还原。

## 构建与验证

初始化固定子模块后，使用支持 C# 13 的 .NET SDK、Windows MSBuild / WPF 构建工具与 .NET Framework 4.8 引用程序集，按原工作流恢复 NuGet 并构建：

```powershell
git submodule update --init --recursive
dotnet restore PCLCS/PCLCS.csproj
msbuild "Plain Craft Launcher 2/Plain Craft Launcher 2.vbproj" -p:Configuration=Release
```

2026-10-23 已完成三个原始 C# 依赖项目以及全部 VB / WPF XAML 的 Release、Debug 完整构建。便携构建仅通过外部 targets 补齐本机引用与 WPF 编译环境，没有修改子模块源码。

仓库自带回归入口：

```powershell
./tests/Run-RecoveredChecks.ps1
```

从仓库外目录分别通过 Windows PowerShell 5.1 和 PowerShell 7 验证，均为退出码 0：更新 / 联网配置 25 项、配置 / 请求头 28 项、百宝箱边界 23 项，合计 **76 项行为断言**；另有 **6 项原生互操作布局 / 声明检查**。所有夹具和日志放在被忽略的 `tests/**/bin/`，各组使用独立子进程及隔离配置，不读取实际部署凭据。

验证需要区分隔离行为测试、完整项目编译和真实界面 / 服务联调。本次没有运行原始启动器或生成的启动器，也没有实际调用内存整理、联网登录、API 鉴权、原生补丁和更新替换。构建及隔离检查通过不意味着所有外部服务可用。
