# AuroraCommunication

库位于 `Libraries/AuroraCommunication`，是独立 Rust workspace 组件。当前包含：

- `aurora-comm-core`：协议无关类型、错误码和 `DeviceClient`；
- `aurora-comm-transport`：Tokio TCP、UDP 与串口传输；
- `aurora-comm-runtime`：每设备串行会话、控制/普通优先队列、重连和 Watch；
- `aurora-modbus`：Modbus TCP/UDP、RTU/ASCII 串口及 RTU/ASCII over TCP，支持 HSL 风格地址、零/一基址、字节字序和 FC20/21/22/23 PDU。
- `aurora-s7`：ISO-on-TCP/S7comm，支持 S1200、S300、S400、S1500、S200Smart、S200 的 TSAP 默认值及 DB/I/Q/M/T/C 地址。
- `aurora-mc3e`：Mitsubishi MC 3E 二进制/ASCII、TCP/UDP，支持 D/M/X/Y/W/B/R/ZR 及计时器、计数器等常用软元件。
- `aurora-opcua`：安全 Rust API、Browse/Read/Write/订阅契约与 simulator；`aurora-opcua-sys` 隔离固定的 open62541 1.5.4 + mbedTLS 3.6.7 静态 native 边界。

地址 `s=2;x=4;500` 表示临时使用站号 2、功能码 4、偏移 500；缺省功能码是 3。一次连接上的 Modbus 事务严格串行，避免响应错配。控制命令进入高优先级队列，但通过 `max_control_burst` 保证普通采集不会永久饥饿。

自动化测试包含 50 个独立 Modbus TCP 端点，以及 Modbus/S7/MC3E/OPC UA 四类混合会话的优先级负载。混合测试在每台设备存在普通采集积压时测量控制命令 P99，并要求低于 100 ms。该测试证明框架调度模型和资源边界，不替代真实交换机、PLC 和噪声环境下的 72 小时老化测试。

OPC UA simulator 已覆盖 Browse、Read、Write 和订阅通知；native feature 当前覆盖证书信任、用户认证和标量读写。native Browse/MonitoredItem 尚未通过 HIL，因此能力查询会明确返回 `browse=false`、`watch=false`，不得将其标记为生产就绪。提交到 `test-data/golden` 的报文是 CI 的固定行为基准，CI 不访问外部 C# 源码。不得把 HSL 派生代码发布到授权组织之外。

Linux native 构建命令为 `cargo check -p aurora-opcua --features native --locked`，已在 Ubuntu 26.04、GCC 15、CMake 4.2、Rust 1.98 上验证。Windows/macOS native 构建仍由对应 CI/HIL runner 给出结论。
