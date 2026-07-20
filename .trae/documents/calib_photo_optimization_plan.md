# 标定拍照优化实施计划

## 需求分析

### 需求1：移除后端缩略图和压缩功能
- **目的**：前端预览图显示原图，避免压缩影响后续 AI 调用
- **范围**：删除所有与缩略图相关的后端逻辑

### 需求2：内参拍照不操作投影仪
- **目的**：内参拍照不需要投影仪开灯关灯等操作
- **范围**：修改 `TakeIntrinsicPhotoAsync` 方法，移除所有投影仪操作

## 修改文件清单

### 后端修改

#### 1. `CalibImageUtils.cs` - 删除缩略图生成方法
- 删除 `GenerateThumbnailBase64` 方法

#### 2. `CalibPhotoRecord.cs` - 数据库实体
- 删除 `ThumbnailBase64` 属性
- 删除 `SetThumbnail` 方法
- 修改构造函数，移除 `thumbnailBase64` 参数

#### 3. `CalibPhotoDtos.cs` - DTO 定义
- 删除 `CalibPhotoDto.ThumbnailBase64` 属性
- 考虑移除 `TakeIntrinsicPhotoInput.ProjectorDeviceId` 参数（或保留但不使用）

#### 4. `CalibPhotoAppService.cs` - 业务逻辑
- 修改 `TakeIntrinsicPhotoAsync`：移除所有投影仪操作（开灯、关灯、设置白屏）
- 修改 `TakeExtrinsicDotPhotoAsync`：移除缩略图生成
- 修改 `TakeExtrinsicCheckerboardPhotoAsync`：移除缩略图生成
- 修改 `TakeStereoExtrinsicPairPhotoAsync`：移除缩略图生成
- 修改 `ToPhotoDto` 方法：不再映射 `ThumbnailBase64`
- 修改照片记录创建逻辑：不再传入缩略图参数

### 前端修改

#### 5. `CalibStep5CameraCalib.vue` - 标定页面
- 修改照片列表渲染逻辑：不再使用 `thumbnailBase64`
- 添加从 BLOB 获取原图的逻辑用于预览

#### 6. `calib-photo.ts` - API 接口定义
- 更新 `CalibPhotoDto` 类型定义，移除 `thumbnailBase64`

## 实施步骤

### 步骤1：删除后端缩略图生成方法
- 删除 `CalibImageUtils.GenerateThumbnailBase64`

### 步骤2：修改数据库实体和 DTO
- 删除 `CalibPhotoRecord.ThumbnailBase64`
- 删除 `CalibPhotoDto.ThumbnailBase64`

### 步骤3：修改业务逻辑中的缩略图调用
- 在 `CalibPhotoAppService` 中搜索所有 `GenerateThumbnailBase64` 调用并删除
- 修改照片记录创建时不再传入缩略图

### 步骤4：修改内参拍照方法
- 删除 `TakeIntrinsicPhotoAsync` 中的投影仪操作代码块
- 保留或移除 `ProjectorDeviceId` 参数验证逻辑

### 步骤5：修改前端预览逻辑
- 前端不再使用 `thumbnailBase64` 字段
- 添加通过 BLOB URL 获取原图预览的逻辑

### 步骤6：编译验证
- 运行 `dotnet build` 验证后端编译
- 运行 `npm run type-check` 验证前端类型检查

## 潜在风险和注意事项

### 风险1：数据库字段删除
- 删除 `ThumbnailBase64` 字段需要创建数据库迁移
- 需要确保现有数据兼容（字段可为 null，删除后数据会丢失，但不影响功能）

### 风险2：前端预览性能
- 原图可能较大，直接加载可能影响性能
- **解决方案**：前端可自行实现按需加载或懒加载

### 风险3：API 兼容性
- DTO 字段变更会影响前后端交互
- 需要确保前后端同步更新

## 验证方法

1. **后端编译验证**：`dotnet build "Aurora Application.slnx"`
2. **前端类型检查**：`npm run type-check`（在前端目录）
3. **运行时验证**：
   - 内参拍照：确认不再调用投影仪接口
   - 照片预览：确认前端显示原图而非缩略图
   - 照片列表：确认正常显示且无错误

## EF Core 迁移命令

由于删除了 `ThumbnailBase64` 数据库字段，需要创建新的迁移：

```bash
dotnet ef migrations add RemoveThumbnailBase64 \
  --project Sources/AuroraStruct3D/AuroraStruct3D.EntityFrameworkCore \
  --startup-project Sources/AuroraStruct3D/AuroraStruct3D.HttpApi.Host
```

## 依赖关系

- 后端修改需要先完成，再进行前端修改
- 数据库迁移需要在发布前执行
