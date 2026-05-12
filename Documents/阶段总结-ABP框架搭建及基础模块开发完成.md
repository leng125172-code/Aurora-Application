# 阶段总结：ABP 框架搭建完成 & 基础功能及模块开发完成

**日期**：2026 年 5 月 8 日  
**项目**：Aurora Application — AuroraStruct3D  
**研发团队**：三花装备事业部 C Sharp 开发团队

---

## 一、总体目标

基于 ABP Framework（源码引入方式）为 AuroraStruct3D 业务系统搭建完整的后端服务框架，集成企业级基础管理模块，完成数据库初始化，具备可运行的 API 服务能力。

---

## 二、技术选型

| 类别 | 选型 |
|------|------|
| 运行时 | .NET 10.0 |
| 后端框架 | ABP Framework（源码引入，路径 `Sources/AuroraAbpPro/`） |
| 数据库 | PostgreSQL 15+ |
| ORM | Entity Framework Core 10.0 + Npgsql |
| 缓存 | Redis（开发环境可禁用） |
| 后台任务 | Hangfire（PostgreSQL 存储） |
| 消息总线 | DotNetCore.CAP（PostgreSQL + RabbitMQ，开发环境内存模式） |
| 实时通信 | SignalR |
| 身份认证 | JWT Bearer |
| 日志 | Serilog（Console + File + Elasticsearch 可选） |
| API 文档 | Swagger / OpenAPI |
| 性能监控 | MiniProfiler |
| 对象存储 | BlobStoring.FileSystem（本地文件系统） |
| 前端 | Vue 3 + Vite + TypeScript + shadcn-vue（SPA） |

---

## 三、解决方案结构

### 3.1 后端分层架构

```
AuroraStruct3D/
├── AuroraStruct3D.Domain.Shared          # 领域共享层（枚举、常量、本地化资源）
├── AuroraStruct3D.Domain                  # 领域层（实体、领域服务、仓储接口）
├── AuroraStruct3D.Application.Contracts   # 应用契约层（DTO、接口定义、权限）
├── AuroraStruct3D.Application             # 应用层（业务逻辑实现）
├── AuroraStruct3D.HttpApi                 # HTTP API 层（Controller）
├── AuroraStruct3D.HttpApi.Host            # 宿主项目（启动入口、模块配置）
├── AuroraStruct3D.EntityFrameworkCore     # 数据访问层（DbContext、仓储实现、迁移）
├── AuroraStruct3D.DbMigrator              # 数据库迁移工具（独立可执行）
├── AuroraStruct3D.Frontend                # Vue 3 SPA 前端（集成到宿主 wwwroot）
└── AuroraStruct3D.WebGateway              # API 网关
```

### 3.2 集成的 ABP 模块

| 模块 | 功能说明 |
|------|----------|
| `BasicManagement` | 用户、角色、组织机构、权限管理（核心基础管理） |
| `NotificationManagement` | 站内消息通知管理 |
| `DataDictionaryManagement` | 数据字典管理 |
| `LanguageManagement` | 多语言管理 |
| `CodeManagement` | 代码生成管理 |
| `TemplateManagement` | 消息模板管理 |
| `DynamicMenuManagement` | 动态菜单管理 |
| `ImportExportManagement` | 导入导出管理 |
| `FileManagement` | 文件管理 |
| `CacheManagement` | 缓存管理 |
| `MasterDataManagement` | 主数据管理 |

### 3.3 开发预览工具

| 工具 | 路径 | 说明 |
|------|------|------|
| CapDashboardPreview | `Tools/CapDashboardPreview/` | CAP 消息总线仪表盘本地预览 |
| HangfireDashboardPreview | `Tools/HangfireDashboardPreview/` | Hangfire 后台任务仪表盘本地预览 |

---

## 四、关键里程碑

### 4.1 框架搭建

- [x] 基于 ABP Framework 完成分层架构搭建（Domain / Application / HttpApi / EF Core 各层）
- [x] 全局 `common.props` 统一配置（.NET 10.0、NRT、LangVersion preview、版本号等）
- [x] 所有 ABP 框架源码以 ProjectReference 方式引入，统一适配 net10.0
- [x] PostgreSQL + EF Core 数据访问层配置完成
- [x] JWT 身份认证、多租户、审计日志、分布式锁等核心功能配置完成
- [x] Hangfire 后台任务集成（PostgreSQL 存储）
- [x] CAP 消息总线集成（支持 PostgreSQL + RabbitMQ）
- [x] SignalR 实时通信集成
- [x] Swagger / MiniProfiler / Serilog 等开发辅助工具集成完成

### 4.2 前端集成

- [x] Vue 3 + Vite + TypeScript + shadcn-vue SPA 前端搭建完成
- [x] 前端构建产物集成到 HttpApi.Host 的 `wwwroot/`
- [x] 后端根路径 `/` 自动服务 `index.html`（SPA 路由兜底）
- [x] Release 配置自动执行 `npm ci && npm run build`

### 4.3 数据库初始化

- [x] 生成初始 EF Core 迁移文件（`20260508053009_InitialCreate`）
  - 包含所有 ABP 框架表及业务模块表
- [x] 修复 ABP 源码 NRT 兼容性问题（Setting.ProviderKey/ProviderName 可空性）
  - 迁移：`20260508053407_Fix_Setting_ProviderKey_Nullable`
- [x] 修复 Menu 实体 Url/Component 可空性问题
  - 迁移：`20260508053951_Fix_Menu_UrlComponent_Nullable`
- [x] DbMigrator 完整运行成功（exit code 0）
  - 数据库表结构创建完成
  - 种子数据填充完成（管理员账号、权限、菜单、系统设置）

---

## 五、已知问题及解决方案记录

### 5.1 NRT 与 ABP 源码兼容性问题

**现象**：全局启用 `<Nullable>enable</Nullable>` 后，ABP 源码中用 `[CanBeNull]` 注解但类型为 `string` 的属性，被 EF Core 识别为 NOT NULL，导致种子数据插入 null 值时报 `23502` 约束错误。

**根因**：ABP 源码设计时未采用 C# NRT（`string?`），使用 JetBrains `[CanBeNull]` 注解代替。EF Core 通过程序集 `NullableContextAttribute` 元数据判断属性可空性，`[CanBeNull]` 不被识别。

**修复方案**：将受影响实体的属性类型从 `string` 改为 `string?`：
- `Volo.Abp.SettingManagement.Domain` → `Setting.ProviderName`、`Setting.ProviderKey`
- `Lion.AbpPro.DynamicMenuManagement.Domain` → `Menu.Url`、`Menu.Component`（及相关 DTO/Input 类）

**注意**：未来若其他 ABP 实体中带 `[CanBeNull]` 注解的 `string` 属性出现类似错误，按同样模式修复。

### 5.2 EF Core 迁移目录为空

**现象**：`DbMigrator` 运行时报 `42P01: 关系 "AbpSettings" 不存在`。

**根因**：项目首次运行前从未执行过 `dotnet ef migrations add`，`Migrations/` 目录为空，`MigrateAsync()` 不执行任何 DDL。

**修复**：在 `AuroraStruct3D.EntityFrameworkCore` 项目目录执行：
```powershell
dotnet ef migrations add InitialCreate --startup-project "../AuroraStruct3D.DbMigrator"
```

---

## 六、开发环境运行说明

### 前置条件

| 依赖 | 版本要求 | 说明 |
|------|----------|------|
| .NET SDK | 10.0+ | 必须 |
| PostgreSQL | 15+ | 必须，监听 `localhost:5432` |
| Node.js | 20+ | 必须（前端构建） |
| Redis | 任意版本 | 可选（`appsettings.Development.json` 中 `Redis.IsEnabled: false` 时跳过） |

### 数据库连接字符串

开发环境配置文件：`AuroraStruct3D.HttpApi.Host/appsettings.Development.json`

```json
{
  "ConnectionStrings": {
    "Default": "User ID=postgres;Password=Password01!0;Host=localhost;Port=5432;Database=LionAbpPro10;"
  }
}
```

### 首次初始化数据库

```powershell
cd Sources/AuroraStruct3D/AuroraStruct3D.DbMigrator
dotnet run
# 输出 "Successfully completed all database migrations." 即为成功
```

### 启动 API 服务

```powershell
cd Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host
dotnet run
# 访问 http://localhost:44315/swagger
```

### 启动前端开发服务器（独立调试）

```powershell
cd Sources/AuroraStruct3D/AuroraStruct3D.Frontend
npm install
npm run dev
# 访问 http://localhost:5173
```

### 预览 Hangfire 仪表盘

```powershell
cd Tools/HangfireDashboardPreview
dotnet run
# 访问 http://localhost:5123（自动跳转到 /hangfire）
```

### 预览 CAP 仪表盘

```powershell
cd Tools/CapDashboardPreview
dotnet run
# 访问 http://localhost:5XXX（自动跳转到 /cap）
```

---

## 七、版本信息

- 版本号格式：`0.1.{年}.{月日}`（由 `common.props` 自动生成）
- 本阶段基准版本：`0.1.2026.58`（2026 年 5 月 8 日生成）

---

## 八、下一阶段计划

- [ ] AuroraStruct3D 业务领域模块开发（三维结构相关功能）
- [ ] 完善单元测试（`AuroraStruct3D.Domain.Tests` / `AuroraStruct3D.Application.Tests`）
- [ ] Docker 容器化部署配置完善
- [ ] CI/CD 流水线接入
- [ ] HttpApi.Host 启动问题排查和修复
