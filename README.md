# Aurora Application

> `2.0` 分支正在移除 ABP 运行时依赖。新架构采用 Rust Core Service + 独立
> AuroraCommunication + .NET HMI Host；现有 ABP/C# 项目仅作为 1.x 迁移来源保留。

## Aurora 2.0

- Rust 承担跨平台业务执行、工站 Actor 和设备通讯；
- Windows HMI 使用 C# WinUI，Linux 首选 Avalonia；
- HMI Host 同时托管 REST、SignalR 和 Web 调试页，通过本机 gRPC 访问 Core；
- 当前纵向切片支持 Modbus TCP，并包含 50 个独立设备的并发仿真测试。

请从 [Aurora 2.0 文档](Documents/Aurora2/index.md) 开始。以下内容描述仍在迁移的 1.x 系统。

## 1.x 项目简介

Aurora Application是一个基于ABP的模块化开发框架，用于快速应用开发、模块管理、解决方案生成和嵌入式资源提取。同时，它也是一个功能强大的工业计算机视觉平台，专为工业自动化场景设计，提供从图像获取到分析输出的完整视觉解决方案。

## 核心模块

### 1. Aurora CV Engine

**功能特性：**
- **图像获取**：支持多种工业相机接口，兼容主流相机设备
- **预处理**：图像增强、去噪、校正等预处理功能
- **算法调度**：智能算法管理和调度系统
- **测量**：精确的尺寸测量和定位
- **缺陷检测**：表面缺陷检测和分类
- **结果输出**：标准化的结果输出和集成接口

**技术特点：**
- 性能可与海康威视MVS相媲美
- 高度可定制的算法框架
- 支持实时处理和批量处理模式

### 2. Aurora Struct3D Core

**功能特性：**
- **3D结构光控制**：精确的结构光投影和控制
- **点云生成**：高质量的3D点云数据生成
- **多光源/相机匹配**：支持多光源和多相机系统的协同工作

**技术特点：**
- 针对RK3588平台优化
- 高效的3D重建算法
- 支持复杂场景的3D建模

## 技术栈

- **核心框架**：ABP框架、OpenCV
- **硬件平台**：支持x86、x64和arm64架构
- **开发语言**：C#
- **依赖管理**：NuGet

## 系统要求

- .NET SDK 6.0或更高版本
- 支持的平台：Windows、Linux
- 硬件要求：
  - 基本配置：4核CPU，8GB内存
  - 推荐配置：8核CPU，16GB内存，独立GPU

## 安装与使用

### 安装

1. 克隆项目仓库
2. 安装依赖包
3. 构建解决方案

### 快速开始

1. 配置相机参数
2. 选择合适的算法模块
3. 运行应用程序
4. 查看分析结果

## 项目结构

### 目录结构
- `Sources/`：源代码目录
- `Tools/`：工具目录
- `Assets/`：资源文件目录
- `Documents/`：文档目录

### 核心模块
- `Aurora.CV.Engine`：核心视觉引擎模块
- `Aurora.Struct3D.Core`：3D结构光处理模块
- `Aurora.Application`：主应用程序

## 贡献指南

我们欢迎社区贡献，包括：
- 功能增强
-  bug修复
- 文档完善
- 算法优化

## 许可证

本项目采用MIT许可证。

## 联系方式

- 开发团队：三花装备事业部 C Sharp 开发团队
- 项目地址：[https://github.com/leng125172-code/Aurora-Application](https://github.com/leng125172-code/Aurora-Application)

## 版本历史

- v0.1.20260415：初始版本发布

---

© 2026 三花装备事业部 C Sharp 开发团队
