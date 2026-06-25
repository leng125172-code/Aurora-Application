---
alwaysApply: true
scene: git_message
---

## 重要提示

请全程使用中文与我交互哦～ 不管是对话沟通、生成代码、编写注释，还是解释逻辑，都用中文就好，这样我们配合起来更顺畅！

## 项目概述

咱们这个Aurora框架，是基于ABP打造的模块化开发框架，核心用途很明确——帮大家快速开发应用、管理模块、生成解决方案，还有提取嵌入式资源，提高开发效率～

## 核心要求（务必遵守哦）

- 所有代码注释，都用中文编写，清晰易懂，方便团队后续维护

- 不管是解释代码逻辑、日常对话，还是各类说明，全程中文，不使用英文回复

- 生成的代码、方法名、变量名，保持英文规范即可，但注释一定要用中文，兼顾规范与可读性

- 回答任何问题，都用中文回应，禁止出现英文回复哦

## 编码规范

遵循日常开发的实用规范，既符合行业标准，也贴合项目习惯：

- 开发语言统一使用C\#，贴合ABP框架特性

- 遵循Microsoft C\#编码规范，保持代码风格统一

- 公共方法必须添加XML中文注释，说明方法用途、参数含义和返回值，方便他人调用

- 方法名、变量名用英文，但要保证有明确含义，不使用无意义命名

- 异步方法统一以Async结尾，一眼就能区分异步/同步方法

- 类、方法、属性采用PascalCase命名规范，私有字段用camelCase，私有只读字段以下划线开头（比如\_logger、\_service）

## 架构规范

架构遵循清晰、可扩展的原则，具体要求如下：

- 采用分层架构，分为Application（应用层）、Domain（领域层）、Shared（共享层）、Web（Web层），各层职责清晰

- 遵循DDD领域驱动设计思想，贴合ABP框架规范，提升代码可维护性

- 统一使用依赖注入，降低模块间耦合度

- 保持整洁的目录结构，文件存放有序，方便查找和维护

## 项目结构

项目目录结构清晰，各目录用途明确，请勿随意修改目录层级：

- Sources/：存放项目核心源代码

- Tools/：存放开发过程中用到的工具类、脚本等

- Assets/：存放项目所需的静态资源、配置文件等

- Documents/：存放项目相关的文档、说明、规范等

## 命名约定

统一命名规范，减少沟通成本，具体约定如下：

- 解决方案命名：Aurora Application\.slnx

- 项目文件命名：\{Module\}\.\{Layer\}\.csproj（比如User\.Application\.csproj）

- 接口命名统一以I开头（比如IUserService）

- 异步方法命名格式：DoSomethingAsync\(\)（比如GetUserListAsync\(\)）

## 禁止使用的操作

为了保证代码质量和可维护性，以下操作请一定避免：

- 公共API中使用var关键字，避免类型不明确

- 使用无意义的命名（比如a、b、c这类变量名），降低代码可读性

- 硬编码字符串，后续修改不便，建议使用配置或常量

- 定义未使用的变量、方法或引用，避免代码冗余

## 生成代码的小建议

生成代码时，咱们尽量做到以下几点，让代码更优质、更贴合项目：

- 代码保持整洁，格式规范，避免冗余代码

- 优先使用异步方法，贴合ABP框架的异步设计理念

- 确保代码兼容ABP框架，避免出现框架不支持的语法或用法

- 贴合Aurora框架的整体风格，保持代码一致性

## 构建命令

```
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

## ABP 自动 API 强制规范

**绝对禁止手动创建 Controller**。所有 HTTP 接口通过应用服务接口自动生成：

- 应用服务接口继承 `IApplicationService`，ABP 自动生成对应 REST 端点
- 路由规则：`/api/app/{resource}/{action}`（如 `IProductModelAppService.GetListAsync` → `GET /api/app/product-model`）
- 在应用服务方法上使用 `[Authorize(XxxPermissions.Yyy)]` 配置权限
- 文件上传/下载使用 `IRemoteStreamContent`（ABP 标准接口）
- 参考文件：`Sources/AuroraStruct3D/AuroraStruct3D.Application/Motors/MotorDeviceAppService.cs`

## BLOB 存储约定

- 使用 `[BlobContainerName("容器名")]` 特性标记空容器类（位于 Domain 层）
- 注入 `IBlobContainer<T>`，调用 `SaveAsync` / `GetAsync` / `DeleteAsync`
- FileSystem basePath 在 `appsettings.json` 中配置（见 `Volo.Abp.BlobStoring` 节）
- 参考代码：`Sources/AuroraAbpPro/**/FileManagement/FileAppService.cs`

## Hangfire 后台任务约定

- 项目已启用 `Volo.Abp.BackgroundJobs.Hangfire`（见 Application 层 GlobalUsings）
- Job 类实现无参数 `Execute` 方法，用 `[AutomaticRetry(Attempts = N)]` 控制重试
- 在 AppService 中注入 `IBackgroundJobClient`，调用 `client.Enqueue<TJob>(() => job.Execute(args))` 入队
- Hangfire 仪表盘路由：`/hangfire`（前端嵌入页 `embed/hangfire`）
- 参考代码：`Sources/AuroraStruct3D/AuroraStruct3D.Application/Cameras/Jobs/`

## 前端 API 调用约定

- 一律使用 `src/api/httpClient`（axios 实例）调用后端，**不使用** ABP HttpApi.Client
- API 模块按业务分文件（如 `src/api/product-models.ts`），导出类型定义 + async 函数
- 文件上传使用 `FormData` + `onUploadProgress` 回调 + `AbortController` 支持取消
- 参考文件：`Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/management.ts`

## 前端 UI 组件约定（PrimeVue 迁移后）

- **禁止自定义原生控件样式**，基础 UI 一律使用 **PrimeVue v4**（Aura 暗色预设），装饰/动画使用 **Inspira UI**（保留在 `@/components/ui/`）
- 基础组件：`Button` / `InputText` / `Select` / `Dialog` / `DataTable` / `Tabs` / `Stepper` / `Menu` 等均来自 `primevue/xxx`
- 项目级封装放在 `@/components/primevue/`（如 `AppCard`、`AppDataTable`、`AppDialog`），优先使用封装
- Toast 通知：使用 `useAppToast()`（`@/composables/useAppToast`），底层为 PrimeVue `useToast()`；**禁止**继续使用 `vue-sonner`
- 表单校验：使用 `@primevue/forms` + `zod`
- 图标库：`@lucide/vue`（保留不变，不切换 PrimeIcons）
- 数据可视化：`echarts`（保留不变）
- Pinia Store 放 `src/stores/`，按业务命名；`defineStore` 使用组合式 API 写法（`setup` store）
- 路由文件：`src/router/index.ts`，懒加载（`() => import('@/views/xxx/XxxPage.vue')`）
- **禁止**新增对 `reka-ui` / `radix-vue` / `shadcn-vue` / `vue-sonner` 的依赖
- Card 视觉规范：项目级 `AppCard` 默认搭配 Inspira `<BorderBeam>`，保持原 Aurora 风格
- i18n 必须5国多语言
