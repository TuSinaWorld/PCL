# 本地 EasyTier 联机检查

日常离线行为检查（不下载、不调用公共节点、不启动 EasyTier）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\LocalLink\Run.ps1
```

直接编译生产 `Modules/ModLocalLink.vb`，Option Strict On，不转换源码。52 项断言覆盖：PCLF1 邀请码与实际节点传递、128 bit 随机密钥、格式/大小/字段/URI 校验、公共节点过滤、官方 CLI JSON 解析、固定三架构组件清单、ZIP 路径/重复/符号链接/取消、全部本机依赖提取、SHA256 与安装清单校验、原子替换保留旧安装，以及真实测试子进程输出/失败/超时/取消后退出。夹具与日志位于被忽略的 `bin/`。

使用已经下载的官方 v2.6.4 ZIP 进行独立真实集成检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\LocalLink\Run-Integration.ps1 -OfficialZip C:\path\easytier-windows-x86_64-v2.6.4.zip
```

也可以传入由同一生产安装器生成、清单与文件摘要校验通过的组件目录：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\LocalLink\Run-Integration.ps1 -ComponentDirectory C:\path\v2.6.4-x86_64
```

此入口不会下载组件或请求公共节点清单，只启动两个官方 Core 和一个测试 echo 子进程，通过 loopback TCP 节点互联，使用生产 CLI 执行/解析检查后，先验证单房主可在零玩家时准备房间，再验证真实 TCP、UDP 端口转发内容一致。使用 `--no-tun --use-smoltcp --bind-device false`，不安装服务、不开 TUN、不修改防火墙，并在 finally 停止本次拥有的子进程；STUN 列表设空。`--bind-device false` 避免官方默认网卡绑定将本机 loopback 连接错误地绑定到非 loopback 源地址。

可选 `-Architecture i686|arm64`、`-OutputRoot`；默认 x86_64，默认输出为 `tests/LocalLink/bin/integration-{guid}/`。输出包括 `integration-result.json`、两端 CLI JSON、隐藏房间密钥后的 Core 日志。集成入口按需手动运行，不属于日常统一离线测试。

实际验收边界：已在当前机器核验官方 x86_64 包 SHA256 并通过双 Core、CLI、TCP/UDP echo。32 位、ARM64、跨互联网两台机器、不同 NAT/中继环境、真实 Minecraft UI 房间发现仍需独立设备联调。
