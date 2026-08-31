# 架构总览

```text
Windows WinUI ─┐
Linux Avalonia ├─ REST + SignalR ─ .NET HMI Host ─ gRPC/Protobuf ─ Rust Core Service
Web 调试页 ────┘                                           │
                                                   Station Actor
                                                          │
                                      AuroraCommunication / C ABI 驱动
                                                          │
                                                PLC、运动控制器等设备
```

职责边界：

| 模块 | 负责 | 不负责 |
| --- | --- | --- |
| Rust Core | 工站状态、流程编排、设备命令、安全降级、持久化 | 页面、用户交互布局 |
| AuroraCommunication | 协议、传输、重连、轮询和队列背压 | 工站业务、数据库、HMI |
| .NET HMI Host | gRPC 网关、REST、SignalR、Web 静态资源、访问边界 | 设备协议和实时业务真值 |
| WinUI/Avalonia/Web | 展示与操作输入 | 独立保存控制状态 |

每台设备由一个托管会话串行拥有连接。每个工站由一个 Actor 串行修改状态，避免 UI 请求、定时任务和设备回调同时改写业务对象。大图像/点云后续走共享内存描述符，不进入 gRPC 消息体。

C++ 仅保留给已有稳定算法或厂商 SDK，通过窄 C ABI 或隔离驱动进程接入。新业务逻辑默认使用 Rust。
