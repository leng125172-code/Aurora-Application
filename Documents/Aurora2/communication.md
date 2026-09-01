# AuroraCommunication

库位于 `Libraries/AuroraCommunication`，是独立 Rust workspace 组件。当前包含：

- `aurora-comm-core`：协议无关类型、错误码和 `DeviceClient`；
- `aurora-comm-transport`：Tokio TCP、UDP 与串口传输；
- `aurora-comm-runtime`：每设备串行会话、控制/普通优先队列、重连和 Watch；
- `aurora-modbus`：Modbus TCP/UDP、RTU/ASCII 串口及 RTU/ASCII over TCP，支持 HSL 风格地址、零/一基址、字节字序和 FC20/21/22/23 PDU。
- `aurora-s7`：ISO-on-TCP/S7comm，支持 S1200、S300、S400、S1500、S200Smart、S200 的 TSAP 默认值及 DB/I/Q/M/T/C 地址。
- `aurora-mc3e`：Mitsubishi MC 3E 二进制/ASCII、TCP/UDP，支持 D/M/X/Y/W/B/R/ZR 及计时器、计数器等常用软元件。
- `aurora-opcua`：安全 Rust API、Browse/Read/Write/订阅契约与 simulator；`aurora-opcua-sys` 隔离固定的 open62541 1.5.4 + mbedTLS 3.6.7 静态 native 边界。

设备与视觉跨进程契约位于 `Contracts/Protos/aurora/v2/device_vision.proto`。当前纵向切片包含
稳定的 `DriverId + HardwareId` 身份、相机帧描述符、设备发现、采帧、视觉算子目录与执行；
Device/Vision Host 提供确定性相机、投影仪和电机模拟器，以及 GRAY8 灰度统计算子。
所有模拟设备在跨进程描述符中显式设置 `simulated=true`，生产 HMI 必须展示并过滤该标记。
Core 使用惰性本机 gRPC 通道代理该服务，Host 暂时不可用不会阻止 Core 进入安全停机状态。
电机、投影仪动作控制在工站互锁策略完成前不对 HMI 开放。

地址 `s=2;x=4;500` 表示临时使用站号 2、功能码 4、偏移 500；缺省功能码是 3。一次连接上的 Modbus 事务严格串行，避免响应错配。控制命令进入高优先级队列，但通过 `max_control_burst` 保证普通采集不会永久饥饿。

自动化测试包含 50 个独立 Modbus TCP 端点，以及 Modbus/S7/MC3E/OPC UA 四类混合会话的优先级负载。混合测试在每台设备存在普通采集积压时测量控制命令 P99，并要求低于 100 ms。该测试证明框架调度模型和资源边界，不替代真实交换机、PLC 和噪声环境下的 72 小时老化测试。

托管会话会在任一协议返回 Transport、Timeout 或 Unavailable 错误后统一关闭失效连接，
下一条命令按指数退避重新连接。CI 包含“首次读取模拟断线、下一条命令重连成功”的验收测试；
真实 PLC、串口转换器和 OPC UA 服务器的拔线/重启恢复仍属于 HIL 矩阵。

OPC UA simulator 已覆盖 Browse、Read、Write 和订阅通知；native feature 已实现完整 trust list、明确的 SecurityMode/Policy、用户认证、标量读写、带 continuation point 的 Browse，以及后台 `UA_Client_run_iterate` 驱动的 MonitoredItem 通知。native 代码虽已通过 Linux 静态编译，尚未完成真实服务器矩阵、证书组合与长稳 HIL，因此仍不得标记为生产就绪。提交到 `test-data/golden` 的报文是 CI 的固定行为基准，CI 不访问外部 C# 源码。不得把 HSL 派生代码发布到授权组织之外。

Linux native 构建命令为 `cargo check -p aurora-opcua --features native --locked`，已在 Ubuntu 26.04、GCC 15、CMake 4.2、Rust 1.98 上验证。Windows/macOS native 构建仍由对应 CI/HIL runner 给出结论。
