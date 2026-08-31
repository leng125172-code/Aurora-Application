# AuroraCommunication

库位于 `Libraries/AuroraCommunication`，是独立 Rust workspace 组件。当前包含：

- `aurora-comm-core`：协议无关类型、错误码和 `DeviceClient`；
- `aurora-comm-transport`：Tokio TCP 长连接传输；
- `aurora-comm-runtime`：每设备串行会话、控制/普通优先队列、重连和 Watch；
- `aurora-modbus`：Modbus TCP 读写与 HSL 风格地址解析。

地址 `s=2;x=4;500` 表示临时使用站号 2、功能码 4、偏移 500；缺省功能码是 3。一次连接上的 Modbus 事务严格串行，避免响应错配。控制命令进入高优先级队列，但通过 `max_control_burst` 保证普通采集不会永久饥饿。

自动化测试会启动 50 个独立 TCP 仿真设备，同时执行连接、读取和写入。该测试证明框架并发模型和资源边界，不替代真实交换机、PLC 和噪声环境下的 72 小时老化测试。

尚未迁移的协议按以下顺序实现，并用现有 C# AuroraCommunication 作为行为测试基准：Modbus RTU、S7 常用区、Melsec MC 3E、OPC UA（open62541 FFI）。不得把 HSL 派生代码发布到授权组织之外。
