# VLan 联机融合

本分支以 VLan 替换 EasyTier，当前发布版本为 `fork-3.0.2`。原 `fork-3.0.1` 正式 Release 保留。服务端继续使用已有 v9 协议，不修改部署或服务端代码；实际部署如果是 v8，需先升级，不能混连。

## 使用

1. 房主进入“设置 → 联机”，设置自己的服务器地址、TCP/UDP 共同端口（默认 11510）与鉴权密码。密码遮蔽输入，按 Windows 当前用户 DPAPI 加密保存。“测试服务器连接”只校验连接和鉴权，不创建网卡、不请求 UAC。
2. Minecraft 单人世界选择“对局域网开放”，在启动器创建房间。启动器尝试从当前实例日志识别端口，也可手填；需要该端口能从房主虚拟 IP 访问。
3. 正常联机通过 `runas` 弹出 Windows 管理员授权，仅提升联机辅助进程。PCL 与 Minecraft 保持普通权限。不要求用户另开 CLI 或 VLan GUI。
4. 房间就绪后点击“复制邀请码”。新玩家粘贴 `PCLV1:`，确认服务器端点，可选仅本次使用或加密记住。游戏使用本机代理地址，可在多人列表发现房间，也可复制地址。
5. 退出房间会清理控制端口、游戏代理、Wintun、路由及临时防火墙规则。关闭启动器时确认并等待清理；不会结束 Minecraft。清理未完成时不启动第二个联网组件，不强杀驱动线程。

默认随机生成房间密码并装入邀请码。设置中可选择不设房间密码，界面会说明其他已鉴权服务器用户也可能加入。无密码不等于免服务器鉴权，也不等于私密房间。

## 邀请码和故障

单条邀请码包括服务器端点、服务器鉴权密码、房间 ID/可选密码、游戏主机身份与 IP/端口、控制端口和随机会话凭据。AES-256-CBC 与 HMAC-SHA256 用于遮蔽明文并检查损坏，独立密钥随码携带，持码者可提取及复用服务器密码；请仅发给可信好友。不提供服务端撤销或过期凭证保证。

- 房间不存在/失效：明确请求新邀请码，不猜测房间或自动新建；点击失败卡片重新输入。
- 房间无密码：直接提交无密码加入请求，不弹房间密码框；服务器鉴权仍照常执行。
- 缺少/错误的房间密码：点击失败卡片输入房间密码，也可返回重新输入邀请码；不将服务器密码当作房间密码。
- 房间满员、鉴权失败、网络超时、网卡失败：分开说明。网络超时不等同于房间不存在。
- 房间 ID 复用：加入后还须通过房主本次会话的随机挑战，验证完成前不发布游戏房间。
- 断线：显示重连状态，恢复失败结束本次联机，不自动创建另一房间。房主明确离开会提示成员结束；v9 的断线租约可能保留一段时间，因此成员提示区区分已探测连接和房间成员。
- UAC 取消：返回可重试失败状态，不循环弹窗。

默认传输策略沿用 VLan；TCP Relay 兼容预设对下次创建房间生效。当前为 IPv4、最多 8 人、中心中继，服务器承担游戏流量。游戏代理支持 Minecraft TCP，不声称已补齐其它游戏的本地 UDP 代理。

## 组件和源码

发布 EXE 内嵌固定组件包，首次联机自动释放并核对完整包与逐文件哈希，包含 `vlan-cli.exe`、官方 x64 `wintun.dll` 及许可证声明。损坏缓存暂存为备份目录再修复，不覆盖仍加载的旧文件。组件只依赖 Windows 系统库及 Wintun，无 Qt GUI 或 VC 运行库旁置要求。

VLan CLI 来源：[feat/pcl-integration](https://github.com/TuSinaWorld/vlan-connect/tree/feat/pcl-integration)，精确提交 `96d4bb5ce3ddb48e09fc47c7d0e718f1d5c52286`。网络协议仍是 v9，机器接口版本 1。构建脚本为该仓库 `client-cli/windows/build.bat`；PCL 打包入口为 `build/Update-VlanBundle.ps1`，检查 `--version` 的 `machine=1` 并固定包哈希。

用内嵌组件部署替代首次联网下载，使发布包不依赖另行发布的 CLI 机器接口资产。旧的官方 v0.3.4 包没有该接口，不能直接替换。EXE 仍会在运行目录之外生成组件及用户配置，不是内存中直接执行所有组件。

## 验证与回机验收

- `tests/Run-RecoveredChecks.ps1`：11 组离线检查，含生产 VLan 编解码、DPAPI、组件校验与修复；合成配置，不读取真实服务器凭据。
- `tests/VlanLink/Run.ps1 -IncludeLocalTransport`：52 项检查，额外验证真实回环 TCP 代理、房主挑战、错误会话拒绝、CLI 身份校验、正常关闭、控制管道异常断开清理、信令断线通知和房主控制端口恢复。不创建网卡，不触发 UAC。
- VLan CMake：22 组协议、鉴权、CLI/GUI 生命周期、传输、链路质量及机器 JSON 检查通过。网卡相关隔离测试使用替身，并非安装真实 Wintun 的证明。
- Debug 与 Release 完整构建；单 EXE 依赖、WPF 资源及内嵌授权 API 配置另行核验。

## 2026-10-29 管理员与临时远程服务器验收

实际 UAC 允许后的辅助进程启动、管道双方 PID 校验与服务器 v9 鉴权已通过。有密码、无密码、TCP Relay 预设房间均可创建并进入真实网卡就绪状态；接口 metric=5、MTU=1400、有限广播路由 metric=1，预期命名的入站防火墙规则存在。每次离房及最终退出后，测试新增的适配器、地址、接口、路由、规则和进程均清理完成。

使用无网卡的第二协议客户端验证：

- 无房间密码可加入；错误房间密码被服务端拒绝。
- 默认 RawUDP 的正式链路探针经远程服务器往返。
- 默认 KCP 的 IPv4 UDP 回声经远程服务器和真实 Wintun 注入/出站往返，载荷、地址和端口一致。
- TCP Relay 的探针与真实 Wintun 回声往返同样通过。
- 第二协议端不配置任何本机虚拟 IP，因此以上中继结果不是同机本地路由绕过服务器的假阳性。所有探针要求服务端确认注销。
- 活动 Wintun 期间直接断开启动器控制管道，辅助进程自行离房、退出并清理所有本次网络对象，没有强杀驱动工作线程。
- 加入已删除的本次受保护房间，明确返回 `ROOM_NOT_FOUND`，没有自动创建替代房间。

**本次验证覆盖启动器使用的生产后台模块、内嵌 CLI 和真实远程传输，不等于两台设备的 Minecraft 实际入服验收。** 当前只有一台 Windows 设备，完整 GUI 点击流程、双设备 Minecraft 入服与长时间游戏稳定性仍须另测。服务端未修改，测试密码不写入源码、Git 或报告；驱动包保留不视为泄漏，不卸载共享 Wintun 驱动。

### 复测

`tests/VlanLink/Run.ps1 -UacProbe` 只触发 UAC 并查询状态，不创建网卡。真实测试使用 `-Remote -ServerHost <服务器> -ServerPort 11510`；密码仅从临时进程环境变量 `PCL_VLAN_TEST_PASSWORD` 读取，不使用命令行参数。

先在 x64 MSVC 开发者环境构建匹配集成分支的 CLI，然后构建无网卡协议端：

```powershell
./tests/VlanLink/Build-RelayProbe.ps1 -VlanSourceRoot '<VLan源码目录>' -NativeObjectDirectory '<CLI本次构建obj目录>'
```

使用上一步输出的 EXE 路径运行完整远程验收；服务器使用占位符，不在测试源码中固定临时端点或密码：

```powershell
$secret = Read-Host '临时服务器密码' -AsSecureString
$env:PCL_VLAN_TEST_PASSWORD = [Net.NetworkCredential]::new('', $secret).Password
try {
    ./tests/VlanLink/Run.ps1 -Remote -ServerHost 'your-v9-server.example' -ServerPort 11510 -RelayProbe '<HeadlessRelayProbe.exe路径>'
} finally {
    Remove-Item Env:PCL_VLAN_TEST_PASSWORD -ErrorAction SilentlyContinue
}
```

每次运行使用独立测试目录，只操作本次随机命名/随机密码房间。若检测到已有 VLan 网卡、规则或该虚拟网段则拒绝开始，不删除用户既有网络配置。真实测试需要人工允许一次 UAC；记录保存在独立目录的 `s/live`，不保存邀请码或密码。只运行 `-Remote` 不带探针时不把本机游戏代理回声标成远程数据验证。