# PLY 预览功能详细使用说明

## 1. 文档目的

本文说明 AuroraStruct3D 前端中 **PLY 文件的实际解析与三维渲染实现**，不讨论预览按钮、弹窗或页面布局。

仓库中存在两条用途不同的 PLY 预览链路：

1. **完整 PLY 文件预览**：从 Blob 接口下载完整文件，使用 Three.js 的 `PLYLoader` 解析，再根据文件是否包含面数据渲染为点云或三角网格。这是普通 PLY 文件预览的推荐实现。
2. **在线扫描增量预览**：通过 SignalR 接收多个 ASCII PLY 分片，将每个分片解析成 XYZRGB 浮点数组并追加到实时点云中。

如果需求是“给定一个 `.ply` 文件并在页面中直接显示”，优先使用第一条链路，即 `WorkflowBlobPreview.vue`。

## 2. 相关代码文件

| 文件 | 作用 |
| --- | --- |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/components/workflow/WorkflowBlobPreview.vue` | 完整 PLY 文件下载、解析、场景初始化、点云/网格判断和渲染 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/utils/workflow-result.ts` | 根据 Blob 文件扩展名把 `.ply` 分类为 `point-cloud` |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/workflow/WorkflowIdePage.vue` | `WorkflowBlobPreview` 的现有调用示例 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/components/PointCloudViewer.vue` | 将 XYZ 或 XYZRGB `Float32Array` 渲染成实时点云 |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/views/calibration/CalibStep6OnlineScan.vue` | 接收 SignalR PLY 分片、解析 ASCII PLY、合并点云并调用 `PointCloudViewer` |
| `Sources/AuroraStruct3D/AuroraStruct3D.Frontend/src/utils/ply-parser.ts` | 独立 PLY 解析工具；当前没有接入上述主预览链路，详见第 9 节 |

当前前端依赖的 Three.js 版本在 `package.json` 中为：

```json
{
  "three": "^0.184.0"
}
```

## 3. 完整 PLY 文件预览

### 3.1 组件输入

`WorkflowBlobPreview.vue` 接收两个属性：

```ts
const props = defineProps<{
    blobKey: string
    presentation: WorkflowResultPresentation
}>()
```

| 属性 | 类型 | PLY 预览时的值 | 说明 |
| --- | --- | --- | --- |
| `blobKey` | `string` | 以 `.ply` 结尾的 Blob 键 | 同时用于下载文件和识别文件扩展名 |
| `presentation` | `WorkflowResultPresentation` | `'point-cloud'` | 告诉组件创建 Three.js 预览画布 |

`blobKey` 必须保留 `.ply` 扩展名。例如：

```text
workflow-results/20260826/aligned-cloud.ply
```

如果 Blob 键没有 `.ply` 扩展名，组件虽然仍可能进入三维预览，但格式识别会失去依据，不建议这样使用。

### 3.2 在任意 Vue 页面中直接使用

该组件本身不是弹窗，可以直接放入页面、卡片、侧栏或结果区域。

```vue
<script setup lang="ts">
import { ref } from 'vue'
import WorkflowBlobPreview from '@/components/workflow/WorkflowBlobPreview.vue'

const plyBlobKey = ref('workflow-results/20260826/aligned-cloud.ply')
</script>

<template>
    <section class="w-full">
        <WorkflowBlobPreview
            :blob-key="plyBlobKey"
            presentation="point-cloud"
        />
    </section>
</template>
```

组件内部画布当前使用以下高度：

```html
class="h-[clamp(16rem,42vh,24rem)] w-full"
```

因此父容器至少需要有可用宽度。画布创建时如果 `clientWidth` 或 `clientHeight` 为零，摄像机比例和渲染尺寸会不正确。

### 3.3 自动识别展示类型

工作流结果页面不是硬编码 `'point-cloud'`，而是通过 `classifyWorkflowResult` 判断：

```ts
import {
    classifyWorkflowResult,
    type WorkflowResultPresentation,
} from '@/utils/workflow-result'

const blobKey = 'workflow-results/20260826/aligned-cloud.ply'
const presentation: WorkflowResultPresentation = classifyWorkflowResult('blob', blobKey)
// presentation === 'point-cloud'
```

模板中使用：

```vue
<WorkflowBlobPreview
    :blob-key="blobKey"
    :presentation="presentation"
/>
```

`workflow-result.ts` 当前把以下扩展名归类为点云：

```ts
['ply', 'pcd', 'xyz', 'pts', 'asc', 'obj']
```

### 3.4 文件下载接口

PLY 预览会调用：

```http
GET /api/app/operator-file/download?blobName={blobKey}
```

Axios 请求配置为：

```ts
const response = await httpClient.get('/api/app/operator-file/download', {
    params: { blobName: props.blobKey },
    responseType: 'blob',
})
```

接口必须满足以下条件：

- 返回完整 PLY 文件内容，而不是 JSON 或 Base64 文本包装。
- HTTP 状态码为成功状态。
- 当前登录用户有读取该 Blob 的权限。
- 响应可以被浏览器作为 `Blob` 读取；`Content-Type` 可使用 `application/octet-stream`。

组件随后执行：

```ts
const blob = await fetchBlob()
const buffer = await blob.arrayBuffer()
await initThree(buffer)
```

### 3.5 PLY 解析与点云/网格判断

核心解析代码为：

```ts
const geometry = new PLYLoader().parse(buffer)

if (geometry.index) {
    geometry.computeVertexNormals()
    return new THREE.Mesh(
        geometry,
        new THREE.MeshStandardMaterial({
            color: 0x94a3b8,
            vertexColors: geometry.hasAttribute('color'),
        }),
    )
}

return new THREE.Points(
    geometry,
    new THREE.PointsMaterial({
        size: 0.02,
        color: themeForegroundColor(),
        vertexColors: geometry.hasAttribute('color'),
        sizeAttenuation: true,
    }),
)
```

判断规则：

- PLY 中存在 `face` 数据并生成索引时，`geometry.index` 有值，使用 `THREE.Mesh` 渲染三角网格。
- PLY 只有 `vertex`、没有面数据时，`geometry.index` 为空，使用 `THREE.Points` 渲染点云。
- 几何体包含 `color` 属性时启用 `vertexColors`，显示逐点或逐顶点颜色。
- 没有颜色属性的点云使用当前主题的前景色。

这一步很重要。纯点云不能无条件使用 `THREE.Mesh`，否则由于没有三角面，画布可能是空白的。

### 3.6 场景与相机初始化

完整预览的初始化流程如下：

```text
下载 Blob
  -> 转 ArrayBuffer
  -> PLYLoader.parse()
  -> 创建 Mesh 或 Points
  -> 计算包围盒
  -> 模型移到原点
  -> 根据最大尺寸放置相机
  -> 启动 requestAnimationFrame 渲染循环
```

当前主要参数：

| 项目 | 当前值 | 作用 |
| --- | --- | --- |
| 相机 | `PerspectiveCamera(60, aspect, 0.01, 100000)` | 透视投影，兼容较大坐标范围 |
| 环境光 | 白色，强度 `0.8` | 保证网格基础亮度 |
| 平行光 | 白色，强度 `1` | 让三角网格产生明暗层次 |
| 网格 | `GridHelper(10, 10)` | 提供空间参考 |
| 点大小 | `0.02` | 点云中每个点的屏幕尺寸基准 |
| 控制器 | `OrbitControls`，启用阻尼 | 支持旋转、缩放和平移 |

模型居中与相机定位：

```ts
const box = new THREE.Box3().setFromObject(sceneObject)
const center = box.getCenter(new THREE.Vector3())
const size = box.getSize(new THREE.Vector3())

sceneObject.position.sub(center)

const maxDimension = Math.max(size.x, size.y, size.z, 1)
camera.position.set(maxDimension, maxDimension, maxDimension * 1.5)
controls.target.set(0, 0, 0)
controls.update()
```

因此源文件可以不以原点为中心，预览时会自动平移到场景中心。此平移只影响浏览器中的显示对象，不修改 PLY 文件内容。

### 3.7 鼠标操作

`OrbitControls` 默认交互如下：

| 操作 | 效果 |
| --- | --- |
| 鼠标左键拖动 | 旋转 |
| 鼠标滚轮 | 缩放 |
| 鼠标右键拖动 | 平移 |

`enableDamping = true`，因此每帧必须调用 `controls.update()`：

```ts
const animate = () => {
    animationFrame = requestAnimationFrame(animate)
    controls?.update()
    renderer?.render(scene, camera)
}
```

### 3.8 属性变化和资源释放

组件会监听 `blobKey` 和 `presentation`：

```ts
watch(
    () => [props.blobKey, props.presentation],
    () => void loadPreview(),
)
```

当 Blob 键改变时，会销毁旧场景资源并加载新 PLY。组件卸载时会执行：

- 取消 `requestAnimationFrame`。
- 销毁 `OrbitControls`。
- 销毁点云或网格的 `BufferGeometry`。
- 销毁材质。
- 销毁 `WebGLRenderer`。
- 释放通过 `URL.createObjectURL` 创建的地址。

复制或重写预览组件时必须保留资源释放逻辑，否则反复切换文件后会持续占用 GPU 和内存。

## 4. 直接预览浏览器本地 PLY 文件

现有 `WorkflowBlobPreview` 从后端 Blob 接口取文件。如果文件来自 `<input type="file">`，无需再调用下载接口，直接读取 `ArrayBuffer`：

```vue
<script setup lang="ts">
import { ref } from 'vue'

const fileInput = ref<HTMLInputElement>()

async function handleFileChange(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    if (!file) return

    if (!file.name.toLowerCase().endsWith('.ply')) {
        throw new Error('请选择 .ply 文件')
    }

    const buffer = await file.arrayBuffer()
    await initThree(buffer)
}
</script>

<template>
    <input
        ref="fileInput"
        type="file"
        accept=".ply"
        @change="handleFileChange"
    >
    <canvas ref="canvasRef" class="h-[500px] w-full" />
</template>
```

这里的 `initThree()`、`parseThreeObject()`、`pointsFromGeometry()` 和销毁逻辑可直接复用 `WorkflowBlobPreview.vue` 中的实现。不要把本地文件转成文本后再交给 `PLYLoader`，因为二进制 PLY 必须保持 `ArrayBuffer`。

## 5. 直接预览任意 HTTP 地址返回的 PLY

如果 PLY 不是存储在 AuroraStruct3D Blob 服务中，可以先获取二进制内容：

```ts
async function loadPlyUrl(url: string) {
    const response = await fetch(url, {
        credentials: 'include',
    })
    if (!response.ok) {
        throw new Error(`PLY 下载失败：HTTP ${response.status}`)
    }

    const buffer = await response.arrayBuffer()
    await initThree(buffer)
}
```

跨域地址必须由服务端正确配置 CORS。受鉴权保护的地址还需要携带项目要求的 Cookie 或 Authorization 请求头。

## 6. 在线扫描增量 PLY 预览

### 6.1 适用场景

在线扫描不是一次下载完整文件，而是持续接收多个独立的 ASCII PLY 分片。当前实现位于 `CalibStep6OnlineScan.vue`，渲染组件是 `PointCloudViewer.vue`。

SignalR 事件签名：

```ts
hubConnection.on(
    'ReceiveIncrementalPointCloudAsync',
    (
        projectId: string,
        plyBytes: Uint8Array,
        pointCount: number,
        totalPointCount: number,
    ) => {
        // 解析并追加点云
    },
)
```

当前线上分片约定为 **ASCII PLY，XYZRGB**。每次事件携带的 `plyBytes` 都是一个具有完整 PLY 头的独立分片。

### 6.2 ASCII PLY 转换格式

`parseAsciiPly()` 把每个顶点转换为连续的六个 `Float32`：

```text
[x, y, z, r, g, b, x, y, z, r, g, b, ...]
```

其中：

- `x`、`y`、`z` 保持原始坐标值。
- PLY 中 `0～255` 的 RGB 被归一化为 Three.js 使用的 `0～1`。
- 输出步长固定为 `6`。

示例：

```text
原始顶点：10.5 20.0 30.2 255 128 0
输出数组：10.5, 20.0, 30.2, 1.0, 0.50196, 0.0
```

接收代码：

```ts
hubConnection.on(
    'ReceiveIncrementalPointCloudAsync',
    (projectId, plyBytes, _pointCount, totalPointCount) => {
        if (projectId !== props.project.id || !plyBytes?.length) return

        const chunk = parseAsciiPly(Uint8Array.from(plyBytes))
        if (!chunk?.length) return

        appendPointCloudChunk(chunk, totalPointCount)
    },
)
```

### 6.3 调用实时点云组件

```vue
<script setup lang="ts">
import { ref } from 'vue'
import PointCloudViewer from '@/components/PointCloudViewer.vue'

const pointCloudData = ref<Float32Array | null>(null)
</script>

<template>
    <div class="h-[500px] w-full">
        <PointCloudViewer
            v-if="pointCloudData"
            :point-data="pointCloudData"
            :has-color="true"
        />
    </div>
</template>
```

`PointCloudViewer` 的属性：

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `pointData` | `Float32Array \| null` | XYZ 或 XYZRGB 连续数组 |
| `hasColor` | `boolean` | `true` 表示每点 6 个数，`false` 表示每点 3 个数 |

数据布局必须与 `hasColor` 一致：

```ts
// hasColor = false
new Float32Array([x1, y1, z1, x2, y2, z2])

// hasColor = true
new Float32Array([
    x1, y1, z1, r1, g1, b1,
    x2, y2, z2, r2, g2, b2,
])
```

颜色必须是 `0～1`，不能直接传 `0～255`。

### 6.4 增量合并注意事项

每次追加都重新分配并复制一个更大的 `Float32Array`，点数很大时会增加 GC 和内存压力。实时预览应考虑：

- 限制预览最大点数。
- 在进入浏览器前做体素下采样。
- 只保留最近若干分片。
- 达到上限后按固定间隔抽样，而不是无限追加。

完整、无损的数据仍应以扫描结束后生成的完整 PLY 文件为准。

## 7. 支持的 PLY 内容

完整文件预览使用 Three.js `PLYLoader`，可以处理常见 ASCII 和二进制 PLY。建议 PLY 至少包含：

```ply
ply
format ascii 1.0
element vertex 3
property float x
property float y
property float z
property uchar red
property uchar green
property uchar blue
end_header
0 0 0 255 0 0
1 0 0 0 255 0
0 1 0 0 0 255
```

三角网格还需要 `face`：

```ply
element face 1
property list uchar int vertex_indices
```

并在顶点数据后提供面索引：

```text
3 0 1 2
```

推荐属性命名：

| 内容 | 属性名 |
| --- | --- |
| 坐标 | `x`、`y`、`z` |
| 颜色 | `red`、`green`、`blue` |
| 法线 | `nx`、`ny`、`nz` |
| 面索引 | `vertex_indices` |

如果上游使用非标准属性名，`PLYLoader` 可能无法生成预期的坐标、颜色或法线属性。

## 8. 常见问题排查

### 8.1 画布存在但完全空白

按以下顺序检查：

1. 浏览器网络面板中下载接口是否返回成功。
2. 响应体是否真的是 PLY，而不是错误 JSON 或登录页 HTML。
3. `blobKey` 是否以 `.ply` 结尾。
4. PLY 头是否包含有效的 `element vertex` 和 `end_header`。
5. 纯点云是否使用了 `THREE.Points`，而不是无条件使用 `THREE.Mesh`。
6. 父容器和 Canvas 的宽高是否大于零。
7. 浏览器控制台是否报告 WebGL、内存或 PLY 解析错误。

### 8.2 能看到点，但点太小或太大

调整：

```ts
new THREE.PointsMaterial({
    size: 0.02,
    sizeAttenuation: true,
})
```

`size` 与点云坐标尺度有关。如果点云坐标使用毫米且范围很大，可适当增大；如果坐标已经归一化到 `0～1`，应减小。

### 8.3 有 RGB 属性但颜色不显示

检查以下三点：

- PLY 属性名是否为 `red`、`green`、`blue`。
- `geometry.hasAttribute('color')` 是否为 `true`。
- 材质是否设置 `vertexColors: true`。

使用 `PointCloudViewer` 时还要确认 RGB 已从 `0～255` 归一化到 `0～1`。

### 8.4 模型被裁切或缩放异常

- 检查 PLY 中是否包含极端离群点；包围盒会把离群点也计算在内，导致主体看起来很小。
- 检查坐标中是否存在 `NaN` 或无穷值。
- 超大坐标范围可能需要调整相机的 `near`、`far` 和初始距离。

### 8.5 切换多个文件后浏览器变慢

确认切换前调用了：

```ts
cancelAnimationFrame(animationFrame)
controls.dispose()
geometry.dispose()
material.dispose()
renderer.dispose()
```

仅从 Scene 中移除对象不会立即释放 GPU 资源。

### 8.6 容器大小改变后图像拉伸

`WorkflowBlobPreview` 当前只在初始化时读取 Canvas 尺寸，没有使用 `ResizeObserver`。如果布局会动态改变，需要在尺寸变化时执行：

```ts
camera.aspect = width / height
camera.updateProjectionMatrix()
renderer.setSize(width, height, false)
```

并在组件卸载时断开 `ResizeObserver`。

## 9. `ply-parser.ts` 的定位和限制

`src/utils/ply-parser.ts` 提供 `parsePlyHeader()` 和 `parsePlyData()`，但当前没有被完整文件预览或在线扫描预览引用。

它的输出同样是 XYZ 或 XYZRGB `Float32Array`，适合在明确控制 PLY 属性顺序时与 `PointCloudViewer` 配合。但当前实现有以下限制：

- 假设前三个顶点属性就是 XYZ。
- 假设颜色紧跟在 XYZ 后面。
- 二进制解析依赖固定属性布局，没有逐属性计算字节宽度。
- 不处理面数据。
- 不适合包含法线、透明度、自定义属性或复杂属性顺序的通用 PLY。

因此：

- 普通完整 PLY 文件优先使用 Three.js `PLYLoader`。
- 在线扫描的既定 ASCII XYZRGB 分片使用 `parseAsciiPly()`。
- 不要在未验证属性布局的情况下直接把任意 PLY 交给 `ply-parser.ts`。

## 10. 性能建议

### 10.1 限制设备像素比

高 DPI 屏幕上直接使用 `window.devicePixelRatio` 可能显著提高 GPU 开销，可限制上限：

```ts
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
```

### 10.2 大点云下采样

百万级以上点云建议在后端或 Web Worker 中下采样。主线程同时做 PLY 解析、数组复制和 WebGL 上传时会阻塞界面。

### 10.3 避免重复创建渲染器

同一页面同时展示多个 PLY 会创建多个 WebGL 上下文。大量预览缩略图应使用按需加载，离开可视区域后停止动画或销毁渲染器。

### 10.4 静止时按需渲染

当前实现持续运行 `requestAnimationFrame`。如果页面需要降低 GPU 占用，可以改为在以下事件发生时渲染：

- 控制器 `change`。
- 文件加载完成。
- 主题变化。
- 容器尺寸变化。

启用阻尼时仍需要在惯性动画期间连续更新。

## 11. 推荐选型

| 需求 | 推荐实现 |
| --- | --- |
| 从工作流 Blob 键预览完整 `.ply` | `WorkflowBlobPreview.vue` |
| 从本地文件选择器预览 `.ply` | 复用 `PLYLoader + initThree`，输入改为 `file.arrayBuffer()` |
| 从普通 HTTP 地址预览 `.ply` | 复用 `PLYLoader + initThree`，输入改为 `response.arrayBuffer()` |
| SignalR 持续推送 ASCII PLY 分片 | `parseAsciiPly + PointCloudViewer.vue` |
| 已经拥有 XYZ/XYZRGB 浮点数组 | 直接使用 `PointCloudViewer.vue` |
| 包含任意属性顺序的通用 PLY | 使用 `PLYLoader`，不要直接使用当前 `ply-parser.ts` |

## 12. 最小验证流程

完成接入后按以下步骤验收：

1. 准备一个仅含 XYZ 的纯点云 PLY，确认以点模式显示。
2. 准备一个带 RGB 的纯点云 PLY，确认逐点颜色正确。
3. 准备一个含 `face` 的网格 PLY，确认以实体网格显示且光照正常。
4. 使用鼠标验证旋转、缩放和平移。
5. 连续切换多个 PLY，确认旧模型消失且内存不会持续快速增长。
6. 切换深色和浅色主题，确认无颜色点云和参考网格仍清晰可见。
7. 模拟下载失败和无效 PLY，确认页面显示错误而不是永久停留在加载状态。

