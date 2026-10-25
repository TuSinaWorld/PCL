# 整合包支持检查

运行 `./Run.ps1`，或使用统一入口。直接以 Option Strict On 编译生产 `ModModpackPolicy.vb`，使用真实临时 ZIP 与 JSON 进行离线行为测试。

覆盖 CurseForge、Modrinth、MultiMC、MCBBS、HMCL、内嵌版本/patch JSON、大小写与包裹目录、带启动器的嵌套包、多内层包选择，以及 Forge/Fabric/NeoForge 正向夹具。仅凭描述文本出现 Quilt 不会拒绝；明确 Quilt 依赖会在实例安装动作前拒绝。扫描和实际安装使用同一内层文件选择规则，只选 modpack.zip / modpack.mrpack。

所有夹具位于被忽略的 bin/，不访问网络、不运行启动器、不创建用户实例。已有实例识别不在此策略模块中修改。