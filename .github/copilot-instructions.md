# GitHub Copilot Instructions: Aurora Framework

# GitHub Copilot Instructions: Aurora Framework

## Interaction Rule

All output in English: chat, code comments, logic explanations\. Code identifiers \(methods, variables\) follow English naming conventions\.

## Project Overview

ABP\-based modular framework for rapid app development, module management, solution generation and embedded resource extraction\.

## Coding Standards

- C\#, follow Microsoft C\# conventions and ABP best practices

- Public methods require XML docs \(purpose, parameters, return value\)

- PascalCase for classes/methods/properties; camelCase for private fields; leading underscore for private readonly fields \(e\.g\. `_logger`\)

- Async methods end with `Async`; interfaces start with `I` \(e\.g\. `IUserService`\)

## Architecture \& Structure

- Layered: Application / Domain / Shared / Web, DDD compliant, decouple via dependency injection

- Directories: `Sources/` \(core source\), `Tools/` \(scripts\), `Assets/` \(static files\), `Documents/` \(docs\)

- Solution: `Aurora Application.slnx`

- Project naming: `{Module}.{Layer}.csproj` \(e\.g\. `User.Application.csproj`\)

## Prohibited

- `var` in public APIs; meaningless naming; hardcoded strings; unused variables/methods/references

- Manual Controller creation \(auto\-generate APIs via application services only\)

- Frontend deps: `reka-ui`, `radix-vue`, `shadcn-vue`, `vue-sonner`

- Custom native control styling

## Build Commands

```bash
# Build backend
dotnet build "Aurora Application.slnx"

# Start HTTP host
dotnet run --project Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host/AuroraStruct3D.HttpApi.Host.csproj

# Frontend (dir: Sources/AuroraStruct3D/AuroraStruct3D.Frontend/)
npm run dev          # dev mode
npm run build        # production build
npm run type-check   # TS type check

# EF Core migration
dotnet ef migrations add <Name> \
  --project Sources/AuroraStruct3D/AuroraStruct3D.EntityFrameworkCore \
  --startup-project Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host
```

## ABP Auto API Rules

- App service interfaces inherit `IApplicationService` → auto REST endpoints

- Route: `/api/app/{resource}/{action}`

- Permissions via `[Authorize(XxxPermissions.Yyy)]`

- File I/O uses `IRemoteStreamContent`

- Reference: `Sources/AuroraStruct3D/AuroraStruct3D.Application/Motors/MotorDeviceAppService.cs`

## Blob Storage

- Empty container class in Domain layer with `[BlobContainerName("name")]`

- Inject `IBlobContainer<T>`, call `SaveAsync` / `GetAsync` / `DeleteAsync`

- FileSystem path in `appsettings.json` → `Volo.Abp.BlobStoring`

- Reference: `Sources/AuroraAbpPro/**/FileManagement/FileAppService.cs`

## Hangfire Jobs

- `Volo.Abp.BackgroundJobs.Hangfire` enabled

- Job has parameterless `Execute`; `[AutomaticRetry(Attempts = N)]` for retries

- Inject `IBackgroundJobClient` in AppService, enqueue via `Enqueue`

- Dashboard: `/hangfire`

- Reference: `Sources/AuroraStruct3D/AuroraStruct3D.Application/Cameras/Jobs/`

## Frontend API

- Use `src/api/httpClient` \(axios\) only; no ABP HttpApi\.Client

- Split API files by business module, export types \+ async functions

- File upload: `FormData` \+ `onUploadProgress` \+ `AbortController`

- Reference: `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/api/management.ts`

## Frontend UI \(PrimeVue v4\)

- Base UI: PrimeVue v4 \(Aura dark preset\); decorations: Inspira UI \(`@/components/ui/`\)

- Project wrappers in `@/components/primevue/` \(AppCard, AppDataTable etc\.\); prefer wrappers

- Toast: `useAppToast()` \(`@/composables/useAppToast`\), PrimeVue\-backed

- Form validation: `@primevue/forms` \+ `zod`

- Icons: `@lucide/vue`; Charts: `echarts`

- Pinia stores in `src/stores/`, setup composition style

- Router: `src/router/index.ts`, lazy\-loaded pages

- AppCard includes Inspira `BorderBeam` by default for Aurora style

- i18n: 5 languages required
