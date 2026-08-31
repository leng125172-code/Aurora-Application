# AuroraCommunication

库位于 `Libraries/AuroraCommunication`，是独立 Rust workspace 组件。当前包含：

- `aurora-comm-core`：协议无关类型、错误码和 `DeviceClient`；
- `aurora-comm-transport`：Tokio TCP、UDP 与串口传输；
- `aurora-comm-runtime`：每设备串行会话、控制/普通优先队列、重连和 Watch；
- `aurora-modbus`：Modbus TCP/UDP、RTU/ASCII 串口及 RTU/ASCII over TCP，支持 HSL 风格地址、零/一基址、字节字序和 FC20/21/22/23 PDU。
- `aurora-s7`：ISO-on-TCP/S7comm，支持 S1200、S300、S400、S1500、S200Smart、S200 的 TSAP 默认值及 DB/I/Q/M/T/C 地址。

地址 `s=2;x=4;500` 表示临时使用站号 2、功能码 4、偏移 500；缺省功能码是 3。一次连接上的 Modbus 事务严格串行，避免响应错配。控制命令进入高优先级队列，但通过 `max_control_burst` 保证普通采集不会永久饥饿。

自动化测试会启动 50 个独立 TCP 仿真设备，同时执行连接、读取和写入。该测试证明框架并发模型和资源边界，不替代真实交换机、PLC 和噪声环境下的 72 小时老化测试。

后续协议按 Melsec MC 3E、OPC UA（open62541 FFI）的顺序实现。提交到 `test-data/golden` 的报文是 CI 的固定行为基准，CI 不访问外部 C# 源码。不得把 HSL 派生代码发布到授权组织之外。
