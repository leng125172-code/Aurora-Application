# Aurora Application 项目完整文档

> **生成日期**：2026-06-30  
> **解决方案**：`Aurora Application.slnx`  
> **框架**：ABP (ASP.NET Boilerplate) + Vue 3 + OpenCV

---

## 目录

1. [项目概述](#1-项目概述)
2. [技术架构](#2-技术架构)
3. [项目结构与分层](#3-项目结构与分层)
4. [领域层 (Domain)](#4-领域层-domain)
5. [应用层 (Application)](#5-应用层-application)
6. [基础设施层 (EF Core)](#6-基础设施层-ef-core)
7. [Web 层 (HttpApi.Host)](#7-web-层-httpapihost)
8. [前端 (Frontend)](#8-前端-frontend)
9. [OpenCV 算子引擎](#9-opencv-算子引擎)
10. [工作流引擎](#10-工作流引擎)
11. [框架接口自动生成规范](#11-框架接口自动生成规范)
12. [开发规范与约定](#12-开发规范与约定)
13. [构建与部署](#13-构建与部署)

---

## 1. 项目概述

Aurora Application 是一个基于 ABP 框架的模块化 3D 结构光视觉检测平台，核心功能包括：

- **3D 点云处理**：点云读取、滤波、配准、拟合、分割、特征提取等
- **2D 图像处理**：图像读取、滤波、形态学操作、边缘检测、模板匹配等
- **可视化工作流引擎**：拖拽式算子编排，支持 ForLoop/IfElse 容器、静态校验、数据流仿真
- **设备管理**：相机、投影仪、电机、串口设备统一管理
- **标定系统**：相机标定、投影仪标定、电机标定、多设备联合标定
- **AI 模型管理**：模型上传、格式转换、版本管理
- **产品模型管理**：三维数模导入与管理

---

## 2. 技术架构

### 2.1 后端技术栈

| 技术 | 版本/说明 |
|------|----------|
| .NET | .NET 9 |
| ABP Framework | 多租户、审计、权限、设置管理 |
| Entity Framework Core | ORM，含迁移支持 |
| PostgreSQL | 主数据库 |
| Redis | 分布式缓存（算子注册表、SignalR 背板） |
| Hangfire | 后台任务调度 |
| SignalR | 实时双向通信 |
| CAP | 分布式事务消息 |
| OpenCvSharp | 2D 图像处理 |
| Open3D | 3D 点云处理（通过 P/Invoke） |

### 2.2 前端技术栈

| 技术 | 说明 |
|------|------|
| Vue 3 | 组合式 API + TypeScript |
| Vite | 构建工具 |
| PrimeVue v4 | UI 组件库（Aura 暗色预设） |
| Inspira UI | 装饰/动画组件 |
| Pinia | 状态管理 |
| Vue Router | 路由管理 |
| Axios | HTTP 客户端 |
| ECharts | 数据可视化 |
| Lucide Vue | 图标库 |
| Zod | 表单校验 |
| i18n | 5 国多语言（中/英/日/韩/繁） |

### 2.3 架构模式

采用 **DDD（领域驱动设计）** + **分层架构**：

```
┌─────────────────────────────────────┐
│           Web 层 (Frontend)          │  Vue 3 + PrimeVue
├─────────────────────────────────────┤
│        Web 层 (HttpApi.Host)         │  ASP.NET Core + SignalR
├─────────────────────────────────────┤
│         应用层 (Application)          │  AppService + DTO + 权限
├─────────────────────────────────────┤
│      应用层契约 (Application.Contracts)│  接口 + DTO + 权限常量
├─────────────────────────────────────┤
│         领域层 (Domain)              │  实体 + 仓储接口 + 领域服务
├─────────────────────────────────────┤
│      领域共享层 (Domain.Shared)       │  枚举 + 常量 + 共享类型
├─────────────────────────────────────┤
│      基础设施层 (EntityFrameworkCore) │  DbContext + 仓储实现 + 迁移
├─────────────────────────────────────┤
│        OpenCV 引擎层                 │  算子 + 工作流编译/执行
└─────────────────────────────────────┘
```

---

## 3. 项目结构与分层

### 3.1 解决方案项目清单

```
Aurora Application.slnx
├── Sources/
│   └── AuroraStruct3D/
│       ├── AuroraStruct3D.Domain/                  # 领域层
│       ├── AuroraStruct3D.Domain.Shared/           # 领域共享层
│       ├── AuroraStruct3D.Application/             # 应用层
│       ├── AuroraStruct3D.Application.Contracts/   # 应用层契约
│       ├── AuroraStruct3D.EntityFrameworkCore/    # EF Core 基础设施层
│       ├── AuroraStruct3D.HttpApi.Host/            # HTTP API 主机
│       ├── AuroraStruct3D.OpenCV/                  # OpenCV 引擎层
│       └── AuroraStruct3D.Frontend/                # Vue 3 前端
├── Tools/                                          # 开发工具脚本
├── Documents/                                      # 项目文档
└── Assets/                                         # 静态资源
```

### 3.2 各层职责

| 层 | 项目 | 职责 |
|----|------|------|
| **领域共享层** | `Domain.Shared` | 枚举、常量、共享类型定义，不依赖任何层 |
| **领域层** | `Domain` | 聚合根、实体、值对象、仓储接口、领域服务、Blob 容器 |
| **应用层契约** | `Application.Contracts` | AppService 接口、DTO、权限常量、Notifier 接口 |
| **应用层** | `Application` | AppService 实现、后台任务、AutoMapper 配置 |
| **基础设施层** | `EntityFrameworkCore` | DbContext、仓储实现、EF Core 迁移 |
| **Web 层** | `HttpApi.Host` | 启动配置、中间件、SignalR Hub、Endpoint 路由 |
| **引擎层** | `OpenCV` | 算子定义、工作流编译/校验/仿真/执行 |
| **前端** | `Frontend` | Vue 3 页面、组件、API 模块、状态管理 |

---

## 4. 领域层 (Domain)

### 4.1 实体与聚合根

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.Domain/`

| 模块 | 实体类 | 文件 | 说明 |
|------|--------|------|------|
| 项目管理 | `ProjectInfo` | `Projects/ProjectInfo.cs` | 项目信息聚合根 |
| 工作流 | `WorkflowDefinition` | `Workflow/WorkflowDefinition.cs` | 工作流定义聚合根 |
| 相机 | `CameraDevice` | `Cameras/CameraDevice.cs` | 相机设备聚合根 |
| 投影仪 | `ProjectorDevice` | `Projectors/ProjectorDevice.cs` | 投影仪设备聚合根 |
| 电机 | `MotorAxis` | `Motors/MotorAxis.cs` | 电机轴实体 |
| 标定 | `CalibProject` | `Calibration/CalibProject.cs` | 标定项目聚合根 |
| 标定 | `CalibPhotoRecord` | `Calibration/CalibPhotoRecord.cs` | 标定照片记录 |
| 标定 | `CalibCameraParam` | `Calibration/CalibCameraParam.cs` | 相机标定参数 |
| 标定 | `CalibMotorParam` | `Calibration/CalibMotorParam.cs` | 电机标定参数 |
| 产品模型 | `ProductModel` | `ProductModels/ProductModel.cs` | 产品三维数模聚合根 |
| AI 模型 | `AiModel` | `AI/AiModel.cs` | AI 模型聚合根 |
| 串口 | `SerialPortConfig` | `SerialPorts/SerialPortConfig.cs` | 串口配置 |
| 设备状态 | `DeviceFault` | `DeviceState/DeviceFault.cs` | 设备故障记录 |
| 设备状态 | `DeviceStateLog` | `DeviceState/DeviceStateLog.cs` | 设备状态日志 |
| 算子文件 | `OperatorFileRecord` | `OperatorFile/OperatorFileRecord.cs` | 算子文件记录 |

> 所有实体继承 `FullAuditedAggregateRoot<Guid>`，自动获得创建/修改/删除审计字段。

### 4.2 仓储接口

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.Domain/`

| 接口 | 文件 |
|------|------|
| `IProjectInfoRepository` | `Projects/IProjectInfoRepository.cs` |
| `ICameraDeviceRepository` | `Cameras/ICameraDeviceRepository.cs` |
| `IProjectorRepository` | `Projectors/IProjectorRepository.cs` |
| `IMotorAxisRepository` | `Motors/IMotorAxisRepository.cs` |
| `IProductModelRepository` | `ProductModels/IProductModelRepository.cs` |
| `IAiModelRepository` | `AI/IAiModelRepository.cs` |
| `ICalibrationRepository` | `Calibration/ICalibrationRepository.cs` |
| `ICalibPhotoRepository` | `Calibration/ICalibPhotoRepository.cs` |
| `ISerialPortConfigRepository` | `SerialPorts/ISerialPortConfigRepository.cs` |
| `IDeviceFaultRepository` | `DeviceState/IDeviceFaultRepository.cs` |
| `IDeviceStateLogRepository` | `DeviceState/IDeviceStateLogRepository.cs` |

### 4.3 领域服务与管理器

| 接口/类 | 文件 |
|---------|------|
| `AuroraStruct3DDomainService` | `AuroraStruct3DDomainService.cs` |
| `IDeviceStateManager` | `DeviceState/IDeviceStateManager.cs` |

### 4.4 领域共享层

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.Domain.Shared/`

| 内容 | 文件 |
|------|------|
| 模块注册 | `AuroraStruct3DDomainSharedModule.cs` |
| 常量定义 | `AuroraStruct3DDomainSharedConsts.cs` |
| 设置定义 | `Settings/AuroraStruct3DSettings.cs` |

---

## 5. 应用层 (Application)

### 5.1 应用服务接口清单

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/`

| 接口 | 文件 | 说明 |
|------|------|------|
| `IWorkflowAppService` | `Workflow/IWorkflowAppService.cs` | 工作流 CRUD、校验、仿真 |
| `IWorkflowExecutionAppService` | `Workflow/IWorkflowExecutionAppService.cs` | 工作流执行 |
| `IWorkflowNodePaletteAppService` | `Workflow/IWorkflowNodePaletteAppService.cs` | 节点面板（算子列表） |
| `IProjectInfoAppService` | `Projects/IProjectInfoAppService.cs` | 项目信息管理 |
| `ICameraAppService` | `Cameras/ICameraAppService.cs` | 相机设备管理 |
| `IMotorDeviceAppService` | `Motors/IMotorDeviceAppService.cs` | 电机设备管理 |
| `IProjectorDeviceAppService` | `Projectors/IProjectorDeviceAppService.cs` | 投影仪设备管理 |
| `IProductModelAppService` | `ProductModels/IProductModelAppService.cs` | 产品模型管理 |
| `IAiModelAppService` | `AI/IAiModelAppService.cs` | AI 模型管理 |
| `ICalibProjectAppService` | `Calibration/ICalibProjectAppService.cs` | 标定项目管理 |
| `ICalibCameraParamAppService` | `Calibration/ICalibCameraParamAppService.cs` | 相机标定参数 |
| `ICalibMotorParamAppService` | `Calibration/ICalibMotorParamAppService.cs` | 电机标定参数 |
| `ICalibPhotoAppService` | `Calibration/ICalibPhotoAppService.cs` | 标定照片管理 |
| `ICalibPointCloudAppService` | `Calibration/ICalibPointCloudAppService.cs` | 点云数据管理 |
| `ICalibScanAppService` | `Calibration/ICalibScanAppService.cs` | 扫描数据管理 |
| `IOperatorFileAppService` | `OperatorFile/IOperatorFileAppService.cs` | 算子文件管理 |
| `IDeviceStateAppService` | `DeviceState/IDeviceStateAppService.cs` | 设备状态管理 |
| `ISerialPortAppService` | `SerialPorts/ISerialPortAppService.cs` | 串口通信管理 |
| `IDeviceSessionAppService` | `Sessions/IDeviceSessionAppService.cs` | 设备会话管理 |
| `ILeisaiMotorAppService` | `Leisai/ILeisaiMotorAppService.cs` | Leisai 电机控制 |
| `IKtechMotorAppService` | `Ktech/IKtechMotorAppService.cs` | Ktech 电机控制 |
| `IVariableStageAppService` | `VariableStage/IVariableStageAppService.cs` | 可变阶段控制 |

### 5.2 应用服务实现

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.Application/`

| 实现类 | 对应接口 | 文件 |
|--------|----------|------|
| `WorkflowAppService` | `IWorkflowAppService` | `Workflow/WorkflowAppService.cs` |
| `WorkflowExecutionAppService` | `IWorkflowExecutionAppService` | `Workflow/WorkflowExecutionAppService.cs` |
| `WorkflowNodePaletteAppService` | `IWorkflowNodePaletteAppService` | `Workflow/WorkflowNodePaletteAppService.cs` |
| `ProjectInfoAppService` | `IProjectInfoAppService` | `Projects/ProjectInfoAppService.cs` |
| `CameraDeviceAppService` | `ICameraAppService` | `Cameras/CameraDeviceAppService.cs` |
| `MotorDeviceAppService` | `IMotorDeviceAppService` | `Motors/MotorDeviceAppService.cs` |
| `ProjectorDeviceAppService` | `IProjectorDeviceAppService` | `Projectors/ProjectorDeviceAppService.cs` |
| `ProductModelAppService` | `IProductModelAppService` | `ProductModels/ProductModelAppService.cs` |
| `AiModelAppService` | `IAiModelAppService` | `AI/AiModelAppService.cs` |
| `CalibProjectAppService` | `ICalibProjectAppService` | `Calibration/CalibProjectAppService.cs` |
| `CalibCameraParamAppService` | `ICalibCameraParamAppService` | `Calibration/CalibCameraParamAppService.cs` |
| `CalibMotorParamAppService` | `ICalibMotorParamAppService` | `Calibration/CalibMotorParamAppService.cs` |
| `CalibPhotoAppService` | `ICalibPhotoAppService` | `Calibration/CalibPhotoAppService.cs` |
| `CalibScanAppService` | `ICalibScanAppService` | `Calibration/CalibScanAppService.cs` |
| `OperatorFileAppService` | `IOperatorFileAppService` | `OperatorFile/OperatorFileAppService.cs` |
| `DeviceStateAppService` | `IDeviceStateAppService` | `DeviceState/DeviceStateAppService.cs` |
| `SerialPortAppService` | `ISerialPortAppService` | `SerialPorts/SerialPortAppService.cs` |
| `DeviceSessionAppService` | `IDeviceSessionAppService` | `Sessions/DeviceSessionAppService.cs` |
| `LeisaiMotorAppService` | `ILeisaiMotorAppService` | `Leisai/LeisaiMotorAppService.cs` |
| `KtechMotorAppService` | `IKtechMotorAppService` | `Ktech/KtechMotorAppService.cs` |
| `VariableStageAppService` | `IVariableStageAppService` | `VariableStage/VariableStageAppService.cs` |

### 5.3 权限常量

**文件**：`Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Permissions/AuroraStruct3DPermissions.cs`

```csharp
// 权限层次结构
AuroraStruct3D
├── ProjectManagement      // 项目管理
├── ProductModelManagement // 产品模型管理
├── AiModelManagement      // AI 模型管理
├── CalibrationManagement  // 标定管理
├── CameraManagement       // 相机管理
├── MotorManagement        // 电机管理
├── ProjectorManagement    // 投影仪管理
├── SerialPortManagement   // 串口管理
├── WorkflowManagement     // 工作流管理
└── DeviceStateManagement  // 设备状态管理
```

### 5.4 后台任务 (Background Jobs)

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.Application/BackgroundJobs/`

| Job 类 | 文件 | 说明 |
|--------|------|------|
| `AiModelConversionJob` | `BackgroundJobs/AiModelConversionJob.cs` | AI 模型格式转换 |

> 使用 Hangfire 作为调度器，通过 `IBackgroundJobClient.Enqueue<T>()` 入队。

### 5.5 AutoMapper 配置

**文件**：`Sources/AuroraStruct3D/AuroraStruct3D.Application/AutoMapper/AuroraStruct3DApplicationAutoMapperProfile.cs`

### 5.6 通知器接口 (Notifier)

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/`

| 接口 | 文件 | 说明 |
|------|------|------|
| `ICalibPointCloudNotifier` | `Calibration/ICalibPointCloudNotifier.cs` | 点云数据更新通知 |
| `ICalibScanNotifier` | `Calibration/ICalibScanNotifier.cs` | 扫描数据更新通知 |
| `IKtechMotorNotifier` | `Ktech/IKtechMotorNotifier.cs` | Ktech 电机状态通知 |
| `ILeisaiMotorNotifier` | `Leisai/ILeisaiMotorNotifier.cs` | Leisai 电机状态通知 |
| `IMotorScanProgressNotifier` | `Motors/IMotorScanProgressNotifier.cs` | 扫描进度通知 |
| `IProductModelConversionNotifier` | `ProductModels/IProductModelConversionNotifier.cs` | 产品模型转换通知 |
| `IAiModelConversionNotifier` | `AI/IAiModelConversionNotifier.cs` | AI 模型转换通知 |

---

## 6. 基础设施层 (EF Core)

### 6.1 DbContext

**文件**：`Sources/AuroraStruct3D/AuroraStruct3D.EntityFrameworkCore/EntityFrameworkCore/AuroraStruct3DDbContext.cs`

核心 DbContext 整合了多个 ABP 模块的 DbSet：

| 模块 | 主要 DbSet |
|------|-----------|
| 身份认证 | `Users`, `Roles`, `ClaimTypes`, `OrganizationUnits`, `Sessions` |
| 权限管理 | `PermissionGroups`, `Permissions`, `PermissionGrants` |
| 功能管理 | `FeatureGroups`, `Features`, `FeatureValues` |
| 设置管理 | `Settings`, `SettingDefinitionRecords` |
| 多租户 | `Tenants`, `TenantConnectionStrings` |
| 审计日志 | `AuditLogs`, `AuditLogExcelFiles` |
| 通知 | `Notifications`, `NotificationSubscriptions` |
| 数据字典 | `DataDictionaries` |
| 多语言 | `Languages`, `LanguageTexts` |
| 后台任务 | `BackgroundJobs` |
| 标定 | `CalibProjects`, `CalibMotorParams`, `CalibCameraParams`, `CalibPhotoRecords` 等 |
| 串口 | `SerialPortConfigs`, `SerialPortOperationLogs` |
| 相机 | `CameraDevices`, `CameraParameterSets`, `CameraOperationLogs` |
| 电机 | `MotorAxes`, `MotorMotionConfigs`, `MotorOperationLogs` |
| 投影仪 | `ProjectorDevices`, `ProjectorOperationLogs` |
| 设备状态 | `DeviceStateLogs`, `DeviceFaults` |
| 产品模型 | `ProductModels`, `ProductModelOperationLogs` |
| AI 模型 | `AiModels`, `AiModelFiles`, `AiModelOperationLogs` |
| 项目管理 | `ProjectInfos` |
| 算子文件 | `OperatorFileRecords` |
| 工作流 | `WorkflowDefinitions` |

### 6.2 模型构建扩展

**文件**：`Sources/AuroraStruct3D/AuroraStruct3D.EntityFrameworkCore/EntityFrameworkCore/AuroraStruct3DDbContextModelCreatingExtensions.cs`

### 6.3 仓储实现

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.EntityFrameworkCore/`

| 实现 | 文件 |
|------|------|
| `EfCoreAiModelRepository` | `AI/EfCoreAiModelRepository.cs` |

---

## 7. Web 层 (HttpApi.Host)

### 7.1 启动配置

**文件**：`Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/`

| 文件 | 说明 |
|------|------|
| `Program.cs` | 应用入口 |
| `Startup.cs` | 服务注册与中间件配置 |
| `appsettings.json` | 配置文件 |
| `AuroraStruct3DHttpApiHostModule.cs` | ABP 模块定义 |

### 7.2 中间件与端点

| 路径 | 说明 |
|------|------|
| `/api/app/*` | ABP 自动生成的 REST API |
| `/hangfire` | Hangfire 仪表盘 |
| `/swagger` | Swagger UI（自研） |
| `/cap` | CAP 分布式消息仪表盘 |
| `/profiler` | 性能分析器 |

### 7.3 SignalR Hub

用于设备状态实时推送、扫描进度通知等。

---

## 8. 前端 (Frontend)

### 8.1 目录结构

```
src/
├── api/                    # API 请求模块
│   ├── client.ts           # Axios 实例（拦截器、认证、租户头）
│   ├── auth.ts             # 认证 API
│   ├── calibration.ts      # 标定项目 API
│   ├── calib-photo.ts      # 标定照片 API
│   ├── calib-scan.ts       # 标定扫描 API
│   ├── calib-point-cloud.ts# 标定点云 API
│   ├── cameras.ts          # 相机 API
│   ├── motors.ts           # 电机 API
│   ├── projectors.ts       # 投影仪 API
│   ├── serial-ports.ts     # 串口 API
│   ├── device-state.ts     # 设备状态 API
│   ├── device-sessions.ts  # 设备会话 API
│   ├── product-models.ts   # 产品模型 API
│   ├── ai-models.ts        # AI 模型 API
│   ├── management.ts       # 管理 API
│   ├── ktech.ts            # Ktech 电机 API
│   ├── leisai.ts           # Leisai 电机 API
│   ├── swagger.ts          # Swagger API
│   └── abp-application.ts  # ABP 应用配置 API
├── components/             # 公共组件
│   ├── primevue/           # PrimeVue 封装组件（AppCard, AppDataTable, AppDialog）
│   ├── ui/                 # Inspira UI 装饰组件
│   ├── LangSwitcher.vue    # 语言切换器
│   └── ThemeToggle.vue     # 主题切换器
├── composables/            # 组合式函数
│   └── useAppToast.ts      # Toast 通知封装
├── i18n/                   # 国际化
│   └── locales/            # 五国语言包（zh-CN, en, ja, ko, zh-TW）
├── layouts/                # 布局组件
│   └── MainLayout.vue      # 主布局（侧边栏 + 顶栏）
├── router/                 # 路由
│   └── index.ts            # 路由表 + 鉴权守卫
├── stores/                 # Pinia 状态管理
│   ├── auth.ts             # 认证状态
│   ├── cameras.ts          # 相机状态
│   ├── motors.ts           # 电机状态
│   ├── projectors.ts       # 投影仪状态
│   ├── serialPorts.ts      # 串口状态
│   ├── deviceState.ts      # 设备状态
│   ├── deviceSession.ts    # 设备会话
│   ├── theme.ts            # 主题状态
│   ├── tenant.ts           # 租户状态
│   ├── ktech.ts            # Ktech 电机状态
│   └── leisai.ts           # Leisai 电机状态
├── views/                  # 页面组件
│   ├── login/              # 登录页
│   ├── dashboard/          # 仪表盘
│   ├── calibration/        # 标定向导（7 步流程）
│   ├── cameras/            # 相机管理/控制/日志
│   ├── motors/             # 电机管理/控制/日志
│   ├── projectors/         # 投影仪管理/控制/日志
│   ├── serial-ports/       # 串口管理/日志
│   ├── device-state/       # 故障历史/状态日志
│   ├── product-models/     # 产品模型管理/日志
│   ├── ai-models/          # AI 模型管理/日志
│   ├── system/             # 系统信息/用户/角色/租户
│   ├── swagger/            # 自研 Swagger UI
│   ├── embed/              # 内嵌页面（Hangfire/Cap/Profiler）
│   └── error/              # 404 页面
├── App.vue                 # 根组件
└── main.ts                 # 应用入口
```

### 8.2 路由表

| 路径 | 页面 | 组件 |
|------|------|------|
| `/login` | 登录 | `LoginPage` |
| `/dashboard` | 仪表盘 | `DashboardPage` |
| `/embed/swagger` | Swagger UI | `SwaggerPage` |
| `/embed/cap` | CAP 仪表盘 | `CapPage` |
| `/embed/hangfire` | Hangfire 仪表盘 | `HangfirePage` |
| `/embed/profiler` | 性能分析器 | `ProfilerPage` |
| `/system-info` | 系统信息 | `SystemInfoPage` |
| `/device-state/faults` | 故障历史 | `FaultHistoryPage` |
| `/device-state/logs` | 状态日志 | `StateLogPage` |
| `/projectors` | 投影仪管理 | `ProjectorManagePage` |
| `/projectors/logs` | 投影仪日志 | `ProjectorLogsPage` |
| `/projectors/:id/control` | 投影仪控制 | `ProjectorControlPage` |
| `/cameras` | 相机管理 | `CameraManagePage` |
| `/cameras/logs` | 相机日志 | `CameraLogsPage` |
| `/cameras/:id/control` | 相机控制 | `CameraControlPage` |
| `/serial-ports` | 串口管理 | `SerialPortManagePage` |
| `/serial-ports/logs` | 串口日志 | `SerialPortLogsPage` |
| `/motors` | 电机管理 | `MotorDeviceManagePage` |
| `/motors/logs` | 电机日志 | `MotorLogsPage` |
| `/motors/ktech-console/:axisId` | Ktech 控制台 | `KtechMotorConsolePage` |
| `/motors/leisai-console/:axisId` | Leisai 控制台 | `LeisaiMotorConsolePage` |
| `/product-models` | 产品模型管理 | `ProductModelManagePage` |
| `/product-models/logs` | 产品模型日志 | `ProductModelLogsPage` |
| `/ai-models` | AI 模型管理 | `AiModelManagePage` |
| `/ai-models/logs` | AI 模型日志 | `AiModelLogsPage` |
| `/calibration/projects` | 标定项目管理 | `CalibProjectManagePage` |
| `/calibration/projects/:id/wizard` | 标定向导 | `CalibWizardPage` |

---

## 9. OpenCV 算子引擎

### 9.1 模块入口

**文件**：`Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/OpenCVModule.cs`

### 9.2 算子接口

**文件**：`Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/IOperator.cs`

每个算子必须实现 `IOperator` 接口，包含：
- 静态属性 `InputVisionParameters`（输入端口）
- 静态属性 `OutputVisionParameters`（输出端口）
- 静态属性 `ConfigParameters`（可配置参数）
- 实例方法 `ExecuteAsync(WorkflowContext)`（执行逻辑）

### 9.3 算子分类清单

#### 2D 图像处理

| 分类 | 算子 | 文件 |
|------|------|------|
| **2D 预处理** | 高斯模糊 | `MatOps/gaussian_blur.cs` |
| | 中值模糊 | `MatOps/median_blur.cs` |
| | 双边滤波 | `MatOps/bilateral_filter.cs` |
| | 色彩转灰度 | `MatOps/color_to_grayscale.cs` |
| | 图像缩放 | `MatOps/resize_image.cs` |
| | 降采样 | `MatOps/downscale_image.cs` |
| | 形态学去噪 | `MatOps/morphological_denoise.cs` |
| | 通道分离 XY | `MatOps/split_xy.cs` |
| | 通道分离 XYZ | `MatOps/split_xyz.cs` |
| | 通道合并 | `MatOps/merge_channels.cs` |
| | 直方图均衡化 | `ContrastOps/histogram_equalization.cs` |
| | CLAHE 增强 | `ContrastOps/clahe.cs` |
| | 腐蚀 | `MorphOps/morph_erode.cs` |
| | 膨胀 | `MorphOps/morph_dilate.cs` |
| | 开运算 | `MorphOps/morph_open.cs` |
| | 闭运算 | `MorphOps/morph_close.cs` |
| | 图像读取 | `File/Images/read_image.cs` |
| **2D 检测** | Canny 边缘检测 | `EdgeDetectOps/canny_edge.cs` |
| | Sobel 边缘检测 | `EdgeDetectOps/sobel_edge.cs` |
| | 霍夫直线检测 | `GeometryOps/hough_line.cs` |
| | 霍夫圆检测 | `GeometryOps/hough_circle.cs` |
| | 阈值分割 | `ThresholdOps/threshold.cs` |
| | OTSU 阈值 | `ThresholdOps/otsu_threshold.cs` |
| | 模板匹配 | `TemplateOps/template_match.cs` |
| | 连通组件 | `ConnectedOps/connected_components.cs` |
| | 轮廓分析 | `ConnectedOps/contour_analysis.cs` |
| | Blob 特征 | `BlobOps/blob_features.cs` |
| | FFT 滤波 | `FrequencyOps/fft_filter.cs` |

#### 3D 点云处理

| 分类 | 算子 | 文件 |
|------|------|------|
| **3D 预处理** | 点云读取 | `File/PointCloud/read_point_cloud.cs` |
| | 体素降采样 | `PointCloudOps/voxel_downsample.cs` |
| | 均匀降采样 | `PointCloudOps/uniform_downsample.cs` |
| | 统计离群点移除 | `PointCloudOps/statistical_outlier_removal.cs` |
| | 半径离群点移除 | `PointCloudOps/radius_outlier_removal.cs` |
| | 条件移除 | `PointCloudOps/conditional_removal.cs` |
| | 3D 双边滤波 | `PointCloudOps/bilateral_filter_3d.cs` |
| | MLS 平滑 | `PointCloudOps/moving_least_squares.cs` |
| | 法线计算 | `PointCloudNormals/compute_normals.cs` |
| | 点云裁剪 | `PointCloudOps/point_cloud_crop.cs` |
| | Z 通道统计 | `PointCloudOps/z_channel_stats.cs` |
| | 区域生长分割 | `PointCloudSegmentation/region_growing_segmentation.cs` |
| | 欧几里得聚类 | `PointCloudClustering/euclidean_cluster_extraction.cs` |
| | 变换应用 | `PointCloudRegistration/apply_transform.cs` |
| **3D 检测** | RANSAC 平面拟合 | `PointCloudFit/ransac_plane_fit.cs` |
| | RANSAC+SVD 平面拟合 | `PointCloudFit/ransac_svd_plane_fit.cs` |
| | SVD 平面拟合 | `PointCloudFit/svd_plane_fit.cs` |
| | 球体拟合 | `PointCloudFit/sphere_fit.cs` |
| | 圆柱体拟合 | `PointCloudFit/cylinder_fit.cs` |
| | 3D 圆拟合 | `PointCloudFit/circle_fit_3d.cs` |
| | 3D 直线拟合 | `PointCloudFit/line_fit_3d.cs` |
| | 点云特征提取 | `PointCloudFeatures/point_cloud_features.cs` |
| | 曲率计算 | `PointCloudCurvature/compute_curvature.cs` |
| | 点到平面距离 | `PointCloudDistance/point_to_plane_distance.cs` |
| | 点云间距离 | `PointCloudDistance/cloud_to_cloud_distance.cs` |
| **3D 配准** | ICP 配准 | `PointCloudRegistration/icp_registration.cs` |
| | ICP 点到面 | `PointCloudRegistration/icp_point_to_plane.cs` |
| | NDT 配准 | `PointCloudRegistration/ndt_registration.cs` |
| | 特征粗配准 | `PointCloudRegistration/feature_coarse_registration.cs` |
| **3D 投影** | 平面投影 | `PointCloudProjection/project_to_plane.cs` |
| | 图像投影 | `PointCloudProjection/project_to_image.cs` |
| | 泊松重建 | `PointCloudProjection/poisson_reconstruction.cs` |
| | 贪婪投影 | `PointCloudProjection/greedy_projection.cs` |

#### ROI 分区（2D→3D 桥接）

| 算子 | 文件 | 说明 |
|------|------|------|
| ROI 分区 | `RoiOps/roi_partition.cs` | 2D ROI 掩膜生成，可桥接 3D 裁剪 |

### 9.4 算子注册机制

**核心文件**：

| 文件 | 说明 |
|------|------|
| `Registry/OperatorRegistry.cs` | 算子注册表，扫描程序集自动发现算子 |
| `Registry/IOperatorRegistry.cs` | 注册表接口 |
| `Registry/OperatorDescriptor.cs` | 算子描述符（ID、分类、显示名、类型） |
| `Registry/ParameterDescriptor.cs` | 端口参数描述符 |
| `Registry/ConfigParameterDescriptor.cs` | 配置参数描述符 |
| `Registry/OperatorScanAssemblies.cs` | 扫描程序集配置 |
| `Registry/OperatorRegistryInitializer.cs` | 启动时初始化注册表 |

**注册流程**：
1. 启动时 `OperatorRegistryInitializer` 调用 `OperatorRegistry.InitializeAsync()`
2. 扫描 `OperatorScanAssemblies` 中所有程序集
3. 通过反射发现实现了 `IOperator` 接口且带 `[Guid]` 特性的类
4. 提取 `[Category]`、`[DisplayName]`、`[Description]` 特性
5. 反射读取 `InputVisionParameters` / `OutputVisionParameters` / `ConfigParameters` 静态属性
6. 序列化算子元数据写入 Redis 缓存
7. 后续请求直接从 Redis 读取，无需重复扫描

### 9.5 视觉参数类型

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/VisionParameters/`

| 类型 | 文件 | 说明 |
|------|------|------|
| `IVisionParameter` | `IVisionParameter.cs` | 视觉参数接口 |
| `VisionParameter<T>` | `VisionParameter_T.cs` | 泛型视觉参数 |
| `IConfigParameter` | `IConfigParameter.cs` | 配置参数接口 |
| `ConfigParameter` | `ConfigParameter.cs` | 配置参数实现 |
| `MatImg` | `MatImg.cs` | 图像矩阵类型 |
| `ImgFilePath` | `ImgFilePath.cs` | 图像文件路径 |
| `PointCloudFilePath` | `PointCloudFilePath.cs` | 点云文件路径 |
| `PointCloudData` | `PointCloudData.cs` | 点云数据对象 |

### 9.6 公共工具

| 文件 | 说明 |
|------|------|
| `Common/Math3D.cs` | 3D 数学工具（向量、矩阵运算） |
| `Common/PointCloudUtils.cs` | 点云工具函数 |
| `Common/SpatialHashGrid.cs` | 空间哈希网格（加速近邻搜索） |

---

## 10. 工作流引擎

### 10.1 架构概览

```
前端编辑器 (graphData JSON)
        │
        ▼
┌─────────────────────────┐
│  WorkflowGraphValidator  │  静态校验（变量、类型、可达性）
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│  WorkflowGraphCompiler   │  编译为可执行语句列表
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│  WorkflowExecutor        │  顺序执行语句，维护上下文
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│  WorkflowContext         │  运行时变量存储
└─────────────────────────┘
```

### 10.2 编译模块

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/Workflow/Compilation/`

| 文件 | 说明 |
|------|------|
| `WorkflowGraphCompiler.cs` | 主编译器：解析 JSON → 校验 → 拓扑排序 → 编译 |
| `WorkflowGraphValidator.cs` | 静态校验器：变量写前读、类型兼容、不可达节点、未使用变量、循环变量保护、IfElse 分支完整性 |
| `WorkflowDataFlowSimulator.cs` | 数据流仿真器：模拟变量绑定与流动路径，不执行算子 |
| `WorkflowDataFlowReport.cs` | 数据流仿真报告模型 |
| `WorkflowValidationResult.cs` | 校验结果模型 |
| `WorkflowDiagnostic.cs` | 诊断信息模型（Error/Warning） |
| `WorkflowCompilationException.cs` | 编译异常 |
| `WorkflowSignature.cs` | 工作流签名（用于版本校验） |
| `WorkflowAnchor.cs` | 锚点位置校验 |
| `GraphTopology.cs` | 拓扑排序算法 |
| `CompilationTokens.cs` | 编译令牌常量 |
| `ConditionCompiler.cs` | 条件表达式编译器 |
| `ValueCoercion.cs` | 值类型强制转换 |
| `Model/GraphModels.cs` | 图数据模型（NodeModel, EdgeModel, GraphDataModel） |

**校验规则**：
1. **写前读检测**：变量必须先被定义才能被读取
2. **类型兼容性**：数值类型间赋值告警（不阻断）
3. **命名规则**：变量名必须以字母或下划线开头
4. **不可达节点**：未被边连通的节点告警
5. **未使用变量**：已定义但从未消费的变量告警
6. **重复变量名**：同一变量被多个节点定义告警
7. **循环变量保护**：ForLoop 循环变量在循环体内不可重定义，循环结束后从符号表移除
8. **IfElse 分支完整性**：两个分支的新增变量必须一致

### 10.3 执行模块

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/Workflow/`

| 文件 | 说明 |
|------|------|
| `WorkflowExecutor.cs` | 执行引擎：逐条执行语句 |
| `WorkflowDefinition.cs` | 工作流定义（名称 + 语句列表） |
| `WorkflowContext.cs` | 运行时上下文（变量名 → 值） |
| `IWorkflowContext.cs` | 上下文接口 |
| `WorkflowExecutionException.cs` | 执行异常 |

### 10.4 语句类型

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/Workflow/Statements/`

| 语句 | 文件 | 说明 |
|------|------|------|
| `IWorkflowStatement` | `IWorkflowStatement.cs` | 语句接口 |
| `OperatorCallStatement` | `OperatorCallStatement.cs` | 算子调用 |
| `ForLoopStatement` | `ForLoopStatement.cs` | 循环语句 |
| `IfElseStatement` | `IfElseStatement.cs` | 条件分支语句 |
| `AssignStatement` | `AssignStatement.cs` | 赋值语句 |
| `PortBinding` | `PortBinding.cs` | 端口绑定 |

### 10.5 值类型系统

**文件位置**：`Sources/AuroraStruct3D/AuroraStruct3D.OpenCV/Workflow/Values/`

| 文件 | 说明 |
|------|------|
| `WorkflowValueTypes.cs` | 值类型定义 |
| `WorkflowValueSerializer.cs` | 值序列化器 |
| `MatBinary.cs` | 图像矩阵二进制 |
| `PointCloudBinary.cs` | 点云数据二进制 |

### 10.6 API 接口

#### 工作流管理 (`IWorkflowAppService`)

| 方法 | 路由 | 说明 |
|------|------|------|
| `GetListByProjectIdAsync` | `GET /api/app/workflow/by-project/{projectId}` | 获取项目下所有工作流 |
| `CreateAsync` | `POST /api/app/workflow` | 新建工作流 |
| `GetAsync` | `GET /api/app/workflow/{id}?projectId=...` | 获取工作流详情 |
| `UpdateAsync` | `PUT /api/app/workflow/{id}` | 修改保存工作流 |
| `DeleteAsync` | `DELETE /api/app/workflow/{id}?projectId=...` | 删除工作流 |
| `ValidateAsync` | `GET /api/app/workflow/{id}/validate?projectId=...` | 静态校验工作流 |
| `SimulateAsync` | `GET /api/app/workflow/{id}/simulate?projectId=...` | 数据流仿真 |

---

## 11. 框架接口自动生成规范

### 11.1 ABP 自动 API 控制器

**核心原则：绝对禁止手动创建 Controller。**

所有 HTTP 接口通过应用服务接口自动生成，约定如下：

1. **继承 `IApplicationService`**：所有应用服务接口必须继承 `IApplicationService`，ABP 框架会自动扫描并生成对应的 REST 端点

2. **路由规则**：`/api/app/{resource}/{action}`
   - `{resource}`：从接口名去掉 `I` 前缀和 `AppService` 后缀，转为 kebab-case
   - `{action}`：从方法名去掉 `Async` 后缀，转为 kebab-case
   - 示例：`IProductModelAppService.GetListAsync` → `GET /api/app/product-model`

3. **HTTP 方法映射**：
   - `Get` / `GetList` → `HTTP GET`
   - `Create` → `HTTP POST`
   - `Update` → `HTTP PUT`
   - `Delete` → `HTTP DELETE`

4. **参数绑定**：
   - 简单类型参数从路由或查询字符串绑定
   - 复杂类型参数从请求体绑定（JSON）

5. **权限配置**：在方法上使用 `[Authorize(XxxPermissions.Yyy)]` 配置权限

6. **文件上传/下载**：使用 `IRemoteStreamContent`（ABP 标准接口）

### 11.2 BLOB 存储约定

- 使用 `[BlobContainerName("容器名")]` 特性标记空容器类（位于 Domain 层）
- 注入 `IBlobContainer<T>`，调用 `SaveAsync` / `GetAsync` / `DeleteAsync`
- FileSystem basePath 在 `appsettings.json` 中配置（`Volo.Abp.BlobStoring` 节）

### 11.3 Hangfire 后台任务约定

- Job 类实现无参数 `Execute` 方法，使用 `[AutomaticRetry(Attempts = N)]` 控制重试
- 在 AppService 中注入 `IBackgroundJobClient`，调用 `client.Enqueue<TJob>(() => job.Execute(args))` 入队
- Hangfire 仪表盘路由：`/hangfire`

### 11.4 前端 API 调用约定

- 一律使用 `src/api/httpClient`（axios 实例）调用后端，**不使用** ABP HttpApi.Client
- API 模块按业务分文件，导出类型定义 + async 函数
- 文件上传使用 `FormData` + `onUploadProgress` 回调 + `AbortController` 支持取消

### 11.5 前端 UI 组件约定

- **禁止自定义原生控件样式**，基础 UI 一律使用 **PrimeVue v4**（Aura 暗色预设）
- 装饰/动画使用 **Inspira UI**
- 项目级封装放在 `@/components/primevue/`（如 `AppCard`、`AppDataTable`、`AppDialog`）
- Toast 通知：使用 `useAppToast()`（`@/composables/useAppToast`），底层为 PrimeVue `useToast()`
- 表单校验：使用 `@primevue/forms` + `zod`
- 图标库：`@lucide/vue`
- 数据可视化：`echarts`
- 路由懒加载：`() => import('@/views/xxx/XxxPage.vue')`
- i18n 必须 5 国多语言

---

## 12. 开发规范与约定

### 12.1 编码规范

- 开发语言统一使用 C#，遵循 Microsoft C# 编码规范
- 公共方法必须添加 XML 中文注释，说明方法用途、参数含义和返回值
- 方法名、变量名用英文，保证有明确含义
- 异步方法统一以 `Async` 结尾
- 类、方法、属性采用 PascalCase，私有字段用 camelCase，私有只读字段以下划线开头（`_logger`）
- 公共 API 中禁止使用 `var` 关键字
- 禁止硬编码字符串

### 12.2 命名约定

- 解决方案命名：`Aurora Application.slnx`
- 项目文件命名：`{Module}.{Layer}.csproj`（如 `User.Application.csproj`）
- 接口命名统一以 `I` 开头（如 `IUserService`）
- 异步方法命名格式：`DoSomethingAsync()`（如 `GetUserListAsync()`）

### 12.3 架构规范

- 采用分层架构，各层职责清晰
- 遵循 DDD 领域驱动设计思想
- 统一使用依赖注入，降低模块间耦合度
- 保持整洁的目录结构

### 12.4 文件组织

```
Sources/
├── {Module}/
│   ├── {Module}.Domain/               # 领域层
│   │   ├── {Aggregate}/               # 按聚合根分目录
│   │   │   ├── {Aggregate}.cs         # 聚合根实体
│   │   │   ├── I{Aggregate}Repository.cs  # 仓储接口
│   │   │   └── {Aggregate}Manager.cs  # 领域服务（可选）
│   │   ├── Settings/                  # 设置类
│   │   └── {Module}DomainModule.cs    # 模块注册
│   ├── {Module}.Domain.Shared/       # 领域共享层
│   │   ├── {Module}DomainSharedConsts.cs
│   │   └── {Module}DomainSharedModule.cs
│   ├── {Module}.Application.Contracts/# 应用层契约
│   │   ├── {Aggregate}/
│   │   │   ├── I{Aggregate}AppService.cs  # 应用服务接口
│   │   │   └── Dtos/                 # 数据传输对象
│   │   └── Permissions/              # 权限常量
│   ├── {Module}.Application/         # 应用层
│   │   ├── {Aggregate}/
│   │   │   └── {Aggregate}AppService.cs  # 应用服务实现
│   │   ├── BackgroundJobs/           # 后台任务
│   │   └── AutoMapper/               # 映射配置
│   ├── {Module}.EntityFrameworkCore/  # EF Core 基础设施层
│   │   ├── EntityFrameworkCore/
│   │   │   ├── {Module}DbContext.cs
│   │   │   └── {Module}DbContextModelCreatingExtensions.cs
│   │   ├── {Aggregate}/
│   │   │   └── EfCore{Aggregate}Repository.cs
│   │   └── Migrations/               # 迁移文件
│   ├── {Module}.HttpApi.Host/        # HTTP API 主机
│   │   ├── Program.cs
│   │   ├── Startup.cs
│   │   ├── appsettings.json
│   │   └── {Module}HttpApiHostModule.cs
│   ├── {Module}.OpenCV/              # OpenCV 引擎
│   │   ├── {Category}Ops/            # 按分类组织算子
│   │   ├── Registry/                 # 算子注册
│   │   ├── Workflow/                 # 工作流引擎
│   │   └── VisionParameters/         # 视觉参数类型
│   └── {Module}.Frontend/            # 前端
│       ├── src/
│       │   ├── api/                  # API 调用模块
│       │   ├── components/           # 公共组件
│       │   ├── composables/          # 组合式函数
│       │   ├── i18n/                 # 国际化
│       │   ├── layouts/              # 布局
│       │   ├── router/               # 路由
│       │   ├── stores/               # 状态管理
│       │   └── views/                # 页面
│       ├── package.json
│       └── vite.config.ts
```

---

## 13. 构建与部署

### 13.1 构建命令

```bash
# 后端（在解决方案根目录）
dotnet build "Aurora Application.slnx"

# 启动后端 HTTP 主机
dotnet run --project Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/AuroraStruct3D.HttpApi.Host.csproj

# 前端（在 Sources/AuroraStruct3D/AuroraStruct3D.Frontend/）
npm run dev          # 开发模式
npm run build        # 生产构建
npm run type-check   # TypeScript 类型检查

# EF Core 迁移（在解决方案根目录）
dotnet ef migrations add <MigrationName> \
  --project Sources/AuroraStruct3D/AuroraStruct3D.EntityFrameworkCore \
  --startup-project Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host
```

### 13.2 配置要点

- 数据库连接字符串在 `appsettings.json` 的 `ConnectionStrings.Default` 中配置
- Redis 连接字符串在 `appsettings.json` 的 `Redis.Configuration` 中配置
- BLOB 存储路径在 `Volo.Abp.BlobStoring` 节中配置
- 算子注册表在 Redis 中缓存，key 格式：`opencv:operators`、`opencv:op:{operatorId}:params`

---

> **文档版本**：v1.0  
> **最后更新**：2026-06-30  
> **生成方式**：基于项目源码自动扫描生成