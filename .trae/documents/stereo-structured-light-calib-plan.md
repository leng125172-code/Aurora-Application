# 标定页面清理与标定板识别优化计划

## 一、代码分析结论

### 1.1 相机自动对齐功能

**涉及文件清单**：

| 文件                          | 内容                                                                                 | 清理范围      |
| --------------------------- | ---------------------------------------------------------------------------------- | --------- |
| `CalibPhotoAppService.cs`   | `AutoAlignCamerasAsync` (4854-4930行) + `AlignOneCameraAsync` (4946-5280行)          | 删除两个方法    |
| `ICalibPhotoAppService.cs`  | `AutoAlignCamerasAsync` 接口声明                                                       | 删除接口方法    |
| `CalibPhotoDtos.cs`         | `CameraAlignCameraResult` (271行) + `AutoAlignCamerasResultDto` (307行)              | 删除两个DTO类  |
| `CalibStep5CameraCalib.vue` | 自动对齐按钮、逻辑、状态变量                                                                     | 删除按钮和相关逻辑 |
| `calib-photo.ts`            | `CameraAlignCameraResult` 接口 + `AutoAlignCamerasResultDto` + `autoAlignCameras` 函数 | 删除接口和函数   |
| `i18n/locales/*.ts`         | 5个语言文件中的 `step5AutoAlignBtn` 和 `step5AutoAligning`                                 | 删除翻译项     |

**代码量**：约 430 行（后端）+ 少量前端代码

### 1.2 圆点标定板识别成功率问题

**检测流程分析**：

```
DetectBoardFeaturePoints (2831行)
    └── FindBoardPointsSubpixGray (3158行)
            └── FindCircleGridPoints (3190行)
                    ├── SimpleBlobDetector.Detect() 检测圆点
                    └── TryBuildOrderedCircleGrid() 排序
```

**问题根因**：

1. **缺少图像预处理**：圆点板检测直接在原图上进行，没有像棋盘格那样进行CLAHE、高斯模糊等预处理
2. **检测策略单一**：只有一个Blob检测策略，没有备选方案
3. **未使用OpenCV FindCirclesGrid**：对于非缺孔圆点板使用了`FindCirclesGrid`，但中心缺孔圆点板（MarkedSymmetricCircleGrid）完全依赖自定义Blob检测+排序
4. **Blob检测器参数可能不适用**：默认参数 `MinArea=25`, `MaxArea=10000` 可能不适合高分辨率图像中的圆点
5. **缺少多策略检测**：棋盘格有4种检测策略（原图、CLAHE、高斯模糊、SB算法），圆点板只有1种

***

## 二、修改方案

### 2.1 删除相机自动对齐功能

**步骤**：

1. **后端 - 删除接口方法**

   * 文件：`ICalibPhotoAppService.cs`

   * 删除：`Task<AutoAlignCamerasResultDto> AutoAlignCamerasAsync(Guid id);`

2. **后端 - 删除DTO类**

   * 文件：`CalibPhotoDtos.cs`

   * 删除：`CameraAlignCameraResult` 类（271-306行）

   * 删除：`AutoAlignCamerasResultDto` 类（307-316行）

3. **后端 - 删除实现方法**

   * 文件：`CalibPhotoAppService.cs`

   * 删除：`AutoAlignCamerasAsync` 方法（4854-4930行）

   * 删除：`AlignOneCameraAsync` 方法（4946-5280行）

4. **前端 - 删除API函数和接口**

   * 文件：`calib-photo.ts`

   * 删除：`CameraAlignCameraResult` 接口（470-487行）

   * 删除：`AutoAlignCamerasResultDto` 接口（489-492行）

   * 删除：`autoAlignCameras` 函数（500-510行）

5. **前端 - 删除页面逻辑和UI**

   * 文件：`CalibStep5CameraCalib.vue`

   * 删除：`showAutoAlign` 计算属性（851行）

   * 删除：`alignResult` 响应式变量（856行）

   * 删除：`doAutoAlignCameras` 函数（858-870行）

   * 删除：自动对齐按钮组件（1292-1306行）

   * 删除：相关导入语句（42行、45行）

6. **前端 - 删除国际化翻译**

   * 文件：`i18n/locales/zh-CN.ts`、`en-US.ts`、`ja-JP.ts`、`ko-KR.ts`、`de-DE.ts`

   * 删除：`step5AutoAlignBtn` 和 `step5AutoAligning`

### 2.2 优化圆点标定板检测

**步骤**：

1. **增强圆点板检测策略**

   * 文件：`CalibPhotoAppService.cs`

   * 修改：`FindCircleGridPoints` 方法（3190行）

   * 添加图像预处理策略（原图、CLAHE、高斯模糊）

   * 添加 `FindCirclesGrid` 作为备选方案

2. **优化Blob检测器参数**

   * 文件：`CalibPhotoAppService.cs`

   * 修改：`CreateCircleBlobDetector` 方法（3321行）

   * 根据图像分辨率动态调整面积范围

3. **添加多策略检测**

   * 文件：`CalibPhotoAppService.cs`

   * 修改：`DetectBoardFeaturePoints` 方法（2831行）

   * 为圆点板添加多个检测策略

***

## 三、风险处理

### 3.1 删除自动对齐功能风险

| 风险       | 等级 | 应对措施              |
| -------- | -- | ----------------- |
| 其他模块可能引用 | 低  | 搜索全局引用，确认只有标定页面使用 |
| 编译错误     | 中  | 删除后立即编译验证         |
| 前端引用残留   | 中  | 搜索前端代码中的引用        |

### 3.2 修改圆点板检测逻辑风险

| 风险     | 等级 | 应对措施                    |
| ------ | -- | ----------------------- |
| 检测性能下降 | 中  | 添加多种预处理会增加计算时间，但只在拍照时执行 |
| 误检测增加  | 低  | 多策略检测可以提高成功率，排序算法会过滤噪点  |
| 编译错误   | 中  | 修改后立即编译验证               |

***

## 四、依赖关系

* 无跨模块依赖，自动对齐功能仅在标定页面使用

* 检测逻辑修改仅影响 `CalibPhotoAppService.cs` 内部

***

## 五、验证方法

1. **编译验证**：`dotnet build` 确认后端无错误
2. **前端验证**：`npm run type-check` 确认前端类型正确
3. **功能验证**：测试圆点标定板拍照，确认识别成功率提升
4. **日志验证**：检查日志中检测策略的执行情况

