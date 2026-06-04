# AI 模型管理第三方接口说明

> 适用模块：AuroraStruct3D AI 模型管理
> 基础路径：`/api/app/ai-model`
> 协议：HTTPS + JSON / multipart/form-data
> 鉴权：默认需要登录态或 Bearer Token

---

## 1. 模块说明

当前 AI 模型管理模块提供以下能力：

1. 获取当前运行平台能力信息
2. 查询模型列表、详情、操作日志
3. 查询模型标识下拉项
4. 基于 MD5 做重复文件校验
5. 单文件上传（兼容旧方式）
6. 分片上传、查询上传会话状态、继续上传、取消上传
7. 编辑模型元数据
8. 删除、加载、卸载模型
9. 清理孤立记录

---

## 2. SignalR 说明

### 2.1 当前状态

AI 模型管理模块 **当前没有专用 SignalR Hub，也没有 AI 上传/加载相关实时推送事件**。

也就是说，第三方接入 AI 模型管理时：

1. 不需要建立 AI 专用 SignalR 连接
2. 上传进度需要由客户端自行聚合分片上传进度
3. 加载、卸载、删除、编辑结果均通过 REST 接口同步返回

### 2.2 后续扩展建议

如果后续需要服务端主动推送，可预留如下事件：

1. 上传会话进度推送
2. 模型加载状态变更推送
3. 推理任务状态推送

当前版本这三类能力均未实现，请第三方不要按 SignalR 方式接入 AI 模块。

---

## 3. 数据模型概览

### 3.1 AiModelDto

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | `string(Guid)` | 模型主键 |
| `name` | `string` | 模型显示名称，默认取文件名 |
| `description` | `string?` | 模型描述 |
| `originalFileName` | `string` | 原始文件名 |
| `modelIdentifiers` | `AiModelIdentifierDto[]` | 模型标识列表 |
| `version` | `string?` | 版本号，当前默认按时间生成 |
| `fileFormat` | `string` | 文件格式，如 `ONNX` / `RKNN` |
| `fileSizeBytes` | `number` | 文件大小（字节） |
| `md5` | `string` | 原始文件 MD5 |
| `locationKey` | `string?` | 位置标识 |
| `generationCondition` | `string?` | 生成条件 |
| `loadStatus` | `number` | 加载状态，见下方枚举 |
| `creatorUserName` | `string?` | 上传人 |
| `creationTime` | `string` | 创建时间（ISO 8601） |
| `lastModificationTime` | `string?` | 最后修改时间 |

### 3.2 AiModelLoadStatus

| 值 | 名称 | 说明 |
| --- | --- | --- |
| `0` | `Unloaded` | 未加载 |
| `1` | `Loading` | 加载中 |
| `2` | `Loaded` | 已加载 |
| `3` | `Failed` | 加载失败 |

### 3.3 AiModelIdentifierDto

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | `string(Guid)` | 标识主键 |
| `name` | `string` | 标识名称 |

---

## 4. 查询类接口

### 4.1 获取运行平台能力

- 方法：`GET`
- 路径：`/api/app/ai-model/runtime-platform-info`

#### 响应示例

```json
{
  "isSupported": true,
  "operatingSystem": "Linux",
  "architecture": "Arm64",
  "platformName": "Rockchip RK3588",
  "unsupportedReason": null,
  "compatibilityDescription": "Linux + Rockchip",
  "supportedExtensions": ["rknn", "rkllm", "onnx"],
  "supportsOnnxConversion": true
}
```

### 4.2 获取模型标识下拉列表

- 方法：`GET`
- 路径：`/api/app/ai-model/identifier-lookup`

#### Query 参数

| 参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `filter` | `string?` | 否 | 模糊过滤标识名称 |

### 4.3 MD5 查重

- 方法：`POST`
- 路径：`/api/app/ai-model/check-file-md5`

#### 请求体

```json
{
  "md5": "2f1c8d7b8a7d6a5c4b3e2f1a0b9c8d7e"
}
```

#### 响应字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `md5` | `string` | 标准化后的 MD5 |
| `exists` | `boolean` | 是否已存在 |
| `existingModelId` | `string?` | 已存在模型 ID |
| `existingModelName` | `string?` | 已存在模型名称 |
| `existingOriginalFileName` | `string?` | 已存在模型原始文件名 |

### 4.4 分页查询模型列表

- 方法：`GET`
- 路径：`/api/app/ai-model`

#### Query 参数

| 参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `filter` | `string?` | 否 | 搜索名称、原始文件名、模型标识 |
| `startTime` | `string?` | 否 | 开始时间 |
| `endTime` | `string?` | 否 | 结束时间 |
| `creatorId` | `string?` | 否 | 上传人 ID |
| `sorting` | `string?` | 否 | 排序，例如 `CreationTime DESC` |
| `skipCount` | `number?` | 否 | 跳过条数 |
| `maxResultCount` | `number?` | 否 | 每页条数 |

### 4.5 获取模型详情

- 方法：`GET`
- 路径：`/api/app/ai-model/{id}`

### 4.6 查询模型操作日志

- 方法：`GET`
- 路径：`/api/app/ai-model/logs`

#### Query 参数

| 参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `aiModelId` | `string?` | 否 | 指定模型 ID |
| `filter` | `string?` | 否 | 模糊搜索模型名/文件名/详情 |
| `operationType` | `number?` | 否 | 操作类型 |
| `isFailedOnly` | `boolean?` | 否 | 是否仅失败记录 |
| `startTime` | `string?` | 否 | 开始时间 |
| `endTime` | `string?` | 否 | 结束时间 |
| `skipCount` | `number?` | 否 | 跳过条数 |
| `maxResultCount` | `number?` | 否 | 每页条数 |

---

## 5. 上传接口

## 5.1 旧版整文件上传接口

> 适合中小文件。对于数 GB 模型文件，建议使用 5.2 的分片上传方案。

- 方法：`POST`
- 路径：`/api/app/ai-model/upload`
- Content-Type：`multipart/form-data`

#### 表单字段

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `file` | `file` | 是 | 模型文件 |
| `description` | `string?` | 否 | 模型描述 |
| `identifierNames` | `string[]` | 否 | 模型标识列表，可重复 append |
| `locationKey` | `string?` | 否 | 位置标识 |
| `generationCondition` | `string?` | 否 | 生成条件 |

## 5.2 分片上传推荐流程

推荐顺序：

1. 客户端先计算文件 MD5
2. 调用 MD5 查重接口
3. 初始化上传会话
4. 顺序上传每个分片
5. 若本地已丢失 `sessionId`，先按文件特征查找现有会话
6. 失败后调用“查询上传会话状态”继续上传
7. 全部分片完成后调用“完成上传”
8. 如需放弃上传，调用“取消上传”

### 5.2.1 初始化上传会话

- 方法：`POST`
- 路径：`/api/app/ai-model/initialize-upload`

#### 请求体

```json
{
  "originalFileName": "detector.onnx",
  "fileSizeBytes": 5368709120,
  "chunkSizeBytes": 16777216,
  "totalChunks": 320,
  "md5": "2f1c8d7b8a7d6a5c4b3e2f1a0b9c8d7e"
}
```

#### 响应字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `sessionId` | `string(Guid)` | 上传会话 ID |
| `originalFileName` | `string` | 原始文件名 |
| `fileSizeBytes` | `number` | 文件总大小 |
| `chunkSizeBytes` | `number` | 分片大小 |
| `totalChunks` | `number` | 分片总数 |
| `uploadedChunks` | `number` | 已上传分片数 |
| `uploadedBytes` | `number` | 已上传字节数 |

### 5.2.2 按文件特征查找上传会话

- 方法：`GET`
- 路径：`/api/app/ai-model/find-upload-session`

#### Query 参数

| 参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `originalFileName` | `string` | 是 | 原始文件名 |
| `md5` | `string` | 是 | 文件 MD5 |
| `fileSizeBytes` | `number` | 是 | 文件总大小 |
| `chunkSizeBytes` | `number?` | 否 | 分片大小，建议传入以精确匹配 |
| `totalChunks` | `number?` | 否 | 分片总数，建议传入以精确匹配 |

#### 响应说明

1. 找到会话时返回 `AiModelUploadSessionStatusDto`
2. 未找到会话时返回 `null`
3. 若存在多条匹配记录，返回最后更新时间最新的一条

### 5.2.3 查询上传会话状态

- 方法：`GET`
- 路径：`/api/app/ai-model/upload-session-status`

#### Query 参数

| 参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `sessionId` | `string(Guid)` | 是 | 上传会话 ID |

#### 响应字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `sessionId` | `string(Guid)` | 上传会话 ID |
| `originalFileName` | `string` | 原始文件名 |
| `md5` | `string` | 文件 MD5 |
| `fileSizeBytes` | `number` | 文件总大小 |
| `chunkSizeBytes` | `number` | 分片大小 |
| `totalChunks` | `number` | 分片总数 |
| `uploadedChunks` | `number` | 已上传分片数 |
| `uploadedBytes` | `number` | 已上传字节数 |
| `nextChunkIndex` | `number` | 下一个应上传分片序号 |
| `isCompleted` | `boolean` | 是否已完成全部分片 |
| `lastUpdatedTime` | `string` | 最后更新时间（UTC） |

### 5.2.4 上传单个分片

- 方法：`POST`
- 路径：`/api/app/ai-model/upload-chunk`
- Content-Type：`multipart/form-data`

#### Query 参数

| 参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `sessionId` | `string(Guid)` | 是 | 上传会话 ID |
| `chunkIndex` | `number` | 是 | 分片序号，从 `0` 开始 |

#### 表单字段

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `chunk` | `file/blob` | 是 | 当前分片内容 |

### 5.2.5 完成上传

- 方法：`POST`
- 路径：`/api/app/ai-model/complete-upload`

#### 请求体

```json
{
  "sessionId": "3a1f0b0c-1111-2222-3333-444455556666",
  "description": "用于缺陷检测",
  "identifierNames": ["ocr", "defect"],
  "locationKey": "line-a/station-03",
  "generationCondition": "2026Q2 训练集 v3"
}
```

#### 说明

完成上传时服务端会再次校验：

1. 会话是否已上传完全部分片
2. 临时文件大小是否与初始化声明一致
3. 重算整文件 MD5 是否与初始化值一致
4. 是否出现重复模型

通过后才会写入 Blob 和数据库。

### 5.2.6 取消上传

- 方法：`POST`
- 路径：`/api/app/ai-model/abort-upload`

#### Query 参数

| 参数 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `sessionId` | `string(Guid)` | 是 | 上传会话 ID |

### 5.2.7 失败后继续上传建议

第三方客户端建议本地保存如下信息：

1. `sessionId`
2. `originalFileName`
3. `fileSizeBytes`
4. `md5`
5. `chunkSizeBytes`
6. `totalChunks`

恢复上传时：

1. 如果本地仍保存 `sessionId`，直接调用“查询上传会话状态”
2. 如果本地已丢失 `sessionId`，先调用“按文件特征查找上传会话”
3. 读取 `nextChunkIndex`
4. 从对应分片继续调用“上传单个分片”
5. 全部分片完成后调用“完成上传”

---

## 6. 编辑接口

### 6.1 更新模型元数据

- 方法：`PUT`
- 路径：`/api/app/ai-model/{id}`

#### 可编辑字段

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `name` | `string` | 是 | 模型显示名称 |
| `description` | `string?` | 否 | 模型描述 |
| `identifierNames` | `string[]?` | 否 | 模型标识列表 |
| `version` | `string?` | 否 | 模型版本 |
| `locationKey` | `string?` | 否 | 位置标识 |
| `generationCondition` | `string?` | 否 | 生成条件 |

#### 请求体示例

```json
{
  "name": "detector-prod",
  "description": "用于产线 A 缺陷检测",
  "identifierNames": ["ocr", "defect", "line-a"],
  "version": "20260603153000",
  "locationKey": "line-a/station-03",
  "generationCondition": "训练集 v3 + 数据增强方案 B"
}
```

### 6.2 上传阶段可填写字段

无论使用整文件上传还是分片上传，最终可写入的业务元数据都是以下 4 个字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `description` | `string?` | 模型描述 |
| `identifierNames` | `string[]?` | 模型标识列表 |
| `locationKey` | `string?` | 位置标识 |
| `generationCondition` | `string?` | 生成条件 |

---

## 7. 动作类接口

### 7.1 删除模型

- 方法：`DELETE`
- 路径：`/api/app/ai-model/{id}`

### 7.2 加载模型

- 方法：`POST`
- 路径：`/api/app/ai-model/{id}/load`

### 7.3 卸载模型

- 方法：`POST`
- 路径：`/api/app/ai-model/{id}/unload`

### 7.4 清理孤立记录

- 方法：`POST`
- 路径：`/api/app/ai-model/clean-up-orphaned-records`

#### 响应

返回 `number`，表示本次清理掉的孤立记录数量。

---

## 8. 第三方接入建议

1. 大文件上传统一走分片上传，不要对数 GB 文件使用整包 multipart。
2. 客户端应在本地缓存上传会话信息，以支持失败后的继续上传。
3. 文件去重应先调用 MD5 查重，再初始化上传会话，避免浪费上传时间。
4. 当前 AI 模块没有 SignalR 推送，前端进度条请基于分片上传进度自行计算。
5. 编辑功能只允许修改元数据，不允许直接替换模型文件；替换文件应走重新上传。
