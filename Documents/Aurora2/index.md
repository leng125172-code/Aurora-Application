---
layout: home

hero:
  name: Aurora 2.0
  text: 工业控制上位机框架
  tagline: Rust 实时业务核心、独立通讯库、.NET HMI Host 与多端界面
  actions:
    - theme: brand
      text: 架构总览
      link: /architecture
    - theme: alt
      text: 本地运行
      link: /getting-started

features:
  - title: 业务核心独立
    details: Rust 服务负责设备会话、工站 Actor、流程状态和安全停机，不依赖 UI 生命周期。
  - title: 本地与远程同源
    details: WinUI/Avalonia 与 WebUI 均通过 .NET HMI Host 访问同一套 gRPC 契约。
  - title: 通讯库可复用
    details: AuroraCommunication 是独立 Rust workspace，不引用 HMI、数据库或业务项目。
---

当前代码是 2.0 的第一条可执行纵向切片，不再基于 ABP。已实现 Modbus TCP、50 设备并发仿真、工站安全状态、远程租约、redb outbox、可选 PostgreSQL 投影、gRPC、REST/SignalR 以及内置 Web 调试页。
