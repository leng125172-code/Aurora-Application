<script setup lang="ts">
// 动态 GenICam 驱动的相机控制页面
// 左侧：按 NodeMap.categories 动态渲染参数分组
// 右侧：预览 / 快照 / 旋转角度（软件端）/ 实时指标 / 快捷操作
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCameraStore } from '@/stores/cameras'
import { CameraStatus, type CameraLiveMetricsDto, type GenICamNodeDto } from '@/api/cameras'
import GenICamCategoryCard from '@/components/camera/GenICamCategoryCard.vue'
import { toast } from 'vue-sonner'

const route = useRoute()
const router = useRouter()
const store = useCameraStore()

// ─── 当前设备 ─────────────────────────────────────────────────────────────
const deviceId = computed<string>(() => route.params.id as string)
const device = computed(() => store.cameras.find((c) => c.id === deviceId.value) ?? store.selectedCamera)
const realtimeState = computed(() => store.cameraStates.get(deviceId.value) ?? null)
const isOpen = computed(() => {
    const status = realtimeState.value?.status ?? device.value?.status
    return status === CameraStatus.Ready || status === CameraStatus.Capturing
})
const previewUrl = computed(() => store.previewFrames.get(deviceId.value) ?? null)
const metrics = computed<CameraLiveMetricsDto | null>(() => store.liveMetrics.get(deviceId.value) ?? null)

// ─── NodeMap ─────────────────────────────────────────────────────────────
const nodeMap = computed(() => store.nodeMaps.get(deviceId.value) ?? null)
const nodeMapLoading = ref(false)
/** 节点最新值缓存（nodeName -> value） */
const nodeValues = ref<Record<string, string | null>>({})
/** 各分组刷新中标记 */
const categoryLoading = ref<Record<string, boolean>>({})

/** Selector -> 受影响节点的反向索引，写入 selector 后批量刷新被影响节点 */
const selectorDependencyIndex = computed<Record<string, string[]>>(() => {
    const map: Record<string, string[]> = {}
    if (!nodeMap.value) return map
    for (const dep of nodeMap.value.dependencies) {
        if (!map[dep.selectorNode]) map[dep.selectorNode] = []
        if (!map[dep.selectorNode].includes(dep.affectedNode)) {
            map[dep.selectorNode].push(dep.affectedNode)
        }
    }
    return map
})

// ─── Visibility 筛选 ─────────────────────────────────────────────────────
const visibility = ref<'Beginner' | 'Expert' | 'Guru'>('Beginner')

// ─── NodeMap 加载与节点值初始化 ──────────────────────────────────────────
async function loadNodeMap(forceRefresh = false) {
    if (!isOpen.value) return
    nodeMapLoading.value = true
    try {
        const map = forceRefresh ? await store.refreshNodeMap(deviceId.value) : await store.fetchNodeMap(deviceId.value)
        // 使用 NodeMap 携带的 currentValue 初始化值缓存
        const initial: Record<string, string | null> = {}
        for (const n of map.allNodes) {
            initial[n.nodeName] = n.currentValue
        }
        nodeValues.value = initial
    } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e)
        toast.error(`加载 NodeMap 失败：${msg}`)
    } finally {
        nodeMapLoading.value = false
    }
}

/** 批量读取指定节点最新值 */
async function readNodes(nodeNames: string[]) {
    if (nodeNames.length === 0 || !nodeMap.value) return
    const lookup = new Map(nodeMap.value.allNodes.map((n) => [n.nodeName, n]))
    const inputs = nodeNames
        .map((name) => lookup.get(name))
        .filter((n): n is GenICamNodeDto => !!n && n.nodeType !== 'Command' && n.nodeType !== 'Category')
        .map((n) => ({
            nodeName: n.nodeName,
            dataType: n.nodeType === 'Float' ? 'float' : n.nodeType === 'String' ? 'string' : 'int',
        }))
    if (inputs.length === 0) return
    try {
        const result = await store.readNodes(deviceId.value, inputs)
        for (const r of result.results) {
            nodeValues.value[r.nodeName] = r.success ? r.value : null
        }
    } catch {
        // 忽略：保留上次值
    }
}

/** 刷新单个分组中所有可读节点 */
async function refreshCategory(categoryName: string) {
    if (!nodeMap.value) return
    const cat = nodeMap.value.categories.find((c) => c.name === categoryName)
    if (!cat) return
    categoryLoading.value[categoryName] = true
    try {
        await readNodes(cat.nodes.map((n) => n.nodeName))
    } finally {
        categoryLoading.value[categoryName] = false
    }
}

/** 节点写入后回调：刷新该节点自身 + 所有受其依赖的节点 */
async function onNodeUpdated(node: GenICamNodeDto, _newValue: string) {
    const toRefresh = new Set<string>([node.nodeName])
    const affected = selectorDependencyIndex.value[node.nodeName] ?? []
    for (const a of affected) toRefresh.add(a)
    await readNodes(Array.from(toRefresh))
}

// ─── 旋转角度（软件端，不在 GenICam NodeMap 内）────────────────────────
const savedRotationAngle = ref(0)
const rotationAngle = ref(0)
const rotationSaving = ref(false)
const rotationDirty = computed(() => savedRotationAngle.value !== rotationAngle.value)
const rotationOptions = [
    { value: 0, label: '0°' },
    { value: 90, label: '90°' },
    { value: 180, label: '180°' },
    { value: 270, label: '270°' },
]

async function loadImageParams() {
    if (!isOpen.value) return
    try {
        const angle = await store.fetchImageRotationAngle(deviceId.value)
        savedRotationAngle.value = angle
        rotationAngle.value = angle
    } catch {
        /* 忽略 */
    }
}

async function saveRotationAngle() {
    rotationSaving.value = true
    try {
        await store.applyImageRotationAngle(deviceId.value, rotationAngle.value)
        savedRotationAngle.value = rotationAngle.value
        toast.success('旋转角度已保存')
    } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e)
        toast.error(`保存失败：${msg}`)
    } finally {
        rotationSaving.value = false
    }
}

// ─── 预览控制 ────────────────────────────────────────────────────────────
const previewing = ref(false)
const previewTab = ref<'video' | 'snapshot'>('video')
const snapshotUri = ref<string | null>(null)
const busy = ref(false)

async function run(fn: () => Promise<void>) {
    busy.value = true
    try {
        await fn()
    } finally {
        busy.value = false
    }
}

async function togglePreview() {
    if (previewing.value) {
        try {
            await run(async () => {
                await store.stopCameraPreview(deviceId.value)
                previewing.value = false
                toast.success('预览已停止')
            })
        } catch (e: unknown) {
            const msg = e instanceof Error ? e.message : String(e)
            toast.error(`停止预览失败：${msg}`)
        }
    } else {
        try {
            await run(async () => {
                await store.startCameraPreview(deviceId.value, { enableRtp: false })
                previewing.value = true
                toast.success('预览已启动（SignalR）')
            })
        } catch (e: unknown) {
            previewing.value = false
            const msg = e instanceof Error ? e.message : String(e)
            toast.error(`启动预览失败：${msg}`)
        }
    }
}

async function onSnapshot() {
    await run(async () => {
        const snap = await store.snapshot(deviceId.value)
        snapshotUri.value = snap.dataUri
        previewTab.value = 'snapshot'
        toast.success(`快照已拍摄：${new Date(snap.capturedAt).toLocaleTimeString()}`)
    })
}

async function onSoftTrigger() {
    await run(async () => {
        await store.softTrigger(deviceId.value)
        toast.success('软触发已发送')
    })
}

async function onExposureAutoOncePulse() {
    await run(async () => {
        await store.exposureAutoOncePulse(deviceId.value)
        toast.success('单次自动曝光已触发')
    })
}

function displayMetric(value: number | null | undefined, digits = 1): string {
    if (typeof value !== 'number' || !Number.isFinite(value)) return '—'
    return value.toFixed(digits)
}

// ─── 全部刷新 ────────────────────────────────────────────────────────────
async function refreshAll() {
    if (!nodeMap.value) {
        await loadNodeMap(false)
    } else {
        // 串行读取每个分组，避免并发占用 DbContext
        for (const cat of nodeMap.value.categories) {
            await refreshCategory(cat.name)
        }
    }
    await loadImageParams()
}

// ─── 侦听 / 生命周期 ─────────────────────────────────────────────────────
watch(isOpen, (val) => {
    if (val) {
        void loadNodeMap(false)
        void loadImageParams()
    }
})

onMounted(async () => {
    await store.startHub()
    if (!device.value) {
        await store.refreshCamera(deviceId.value).catch(() => {
            void router.push({ name: 'CameraManage' })
        })
    }
    if (isOpen.value) {
        void loadNodeMap(false)
        void loadImageParams()
    }
})

onUnmounted(async () => {
    if (previewing.value) {
        await store.stopCameraPreview(deviceId.value).catch(() => {})
    }
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <!-- ── 顶部标题栏 ── -->
        <div class="flex flex-wrap items-center gap-3">
            <button
                class="rounded border px-2 py-1 text-xs hover:bg-muted/50"
                @click="void router.push({ name: 'CameraManage' })"
            >
                ← 返回
            </button>
            <h1 class="text-lg font-semibold">{{ device?.name ?? '相机控制' }}</h1>
            <span
                :class="[
                    'rounded px-2 py-0.5 text-xs font-medium',
                    isOpen ? 'bg-green-100 text-green-700' : 'bg-muted text-muted-foreground',
                ]"
            >
                {{ isOpen ? '已打开' : '已关闭' }}
            </span>
            <span class="text-xs text-muted-foreground">SN: {{ device?.serialNumber ?? '—' }}</span>
            <span v-if="realtimeState?.isXmlLoaded === false" class="text-xs text-amber-500">
                GenICam NodeMap 加载中…
            </span>

            <div class="ml-auto flex items-center gap-2">
                <!-- Visibility 筛选 -->
                <label class="text-xs text-muted-foreground">可见性</label>
                <select v-model="visibility" class="rounded border px-2 py-1 text-xs focus:outline-none">
                    <option value="Beginner">Beginner</option>
                    <option value="Expert">Expert</option>
                    <option value="Guru">Guru</option>
                </select>
                <!-- 重新枚举（强制 NodeMap 刷新） -->
                <button
                    :disabled="!isOpen || nodeMapLoading"
                    class="rounded border px-2 py-1 text-xs hover:bg-muted/50 disabled:opacity-40"
                    @click="void loadNodeMap(true)"
                >
                    {{ nodeMapLoading ? '枚举中…' : '重新枚举' }}
                </button>
                <!-- 全部刷新 -->
                <button
                    :disabled="!isOpen"
                    class="rounded border px-2 py-1 text-xs hover:bg-muted/50 disabled:opacity-40"
                    @click="void refreshAll()"
                >
                    全部刷新
                </button>
            </div>
        </div>

        <!-- ── 主体：左侧动态分组 + 右侧预览面板 ── -->
        <div class="grid grid-cols-[1fr_340px] items-start gap-4">
            <!-- 左侧：动态 NodeMap 渲染 -->
            <div class="grid grid-cols-2 gap-4">
                <template v-if="nodeMap && nodeMap.categories.length > 0">
                    <GenICamCategoryCard
                        v-for="cat in nodeMap.categories"
                        :key="cat.name"
                        :camera-id="deviceId"
                        :category="cat"
                        :values="nodeValues"
                        :visibility="visibility"
                        :disabled="!isOpen"
                        :loading="categoryLoading[cat.name]"
                        @refresh="void refreshCategory(cat.name)"
                        @node-updated="(n, v) => void onNodeUpdated(n, v)"
                    />
                </template>
                <div v-else class="col-span-2 rounded-lg border p-6 text-center text-sm text-muted-foreground">
                    {{
                        isOpen
                            ? nodeMapLoading
                                ? '正在加载 NodeMap…'
                                : '暂无 NodeMap 数据，请点击右上角"重新枚举"'
                            : '请先打开相机'
                    }}
                </div>
            </div>

            <!-- 右侧：预览面板（粘性定位） -->
            <div class="sticky top-4 flex flex-col gap-3 rounded-lg border p-3">
                <!-- Tab 切换 -->
                <div class="flex items-center gap-1 border-b pb-2">
                    <button
                        :class="[
                            'px-2 py-0.5 text-xs',
                            previewTab === 'video'
                                ? 'font-medium text-primary'
                                : 'text-muted-foreground hover:text-foreground',
                        ]"
                        @click="previewTab = 'video'"
                    >
                        视频预览
                    </button>
                    <button
                        :class="[
                            'px-2 py-0.5 text-xs',
                            previewTab === 'snapshot'
                                ? 'font-medium text-primary'
                                : 'text-muted-foreground hover:text-foreground',
                        ]"
                        @click="previewTab = 'snapshot'"
                    >
                        图像快照
                    </button>
                </div>

                <!-- 图像显示区域 -->
                <div class="relative aspect-video overflow-hidden rounded bg-black">
                    <img
                        v-if="previewTab === 'video' && previewUrl"
                        :src="previewUrl"
                        class="h-full w-full object-contain"
                        alt="实时预览"
                    />
                    <img
                        v-else-if="previewTab === 'snapshot' && snapshotUri"
                        :src="snapshotUri"
                        class="h-full w-full object-contain"
                        alt="快照"
                    />
                    <div v-else class="flex h-full items-center justify-center text-xs text-white/40">
                        {{ previewTab === 'video' ? (previewing ? '等待帧…' : '预览未启动') : '尚无快照' }}
                    </div>
                </div>

                <!-- 预览控制按钮 -->
                <div class="flex gap-2">
                    <button
                        :disabled="!isOpen || busy"
                        :class="[
                            'flex-1 rounded border px-2 py-1.5 text-xs font-medium disabled:opacity-40',
                            previewing ? 'bg-red-50 text-red-600 hover:bg-red-100' : 'hover:bg-muted/50',
                        ]"
                        @click="void togglePreview()"
                    >
                        {{ previewing ? '停止预览' : '开始预览' }}
                    </button>
                    <button
                        :disabled="!isOpen || busy"
                        class="rounded border px-2 py-1.5 text-xs hover:bg-muted/50 disabled:opacity-40"
                        @click="void onSnapshot()"
                    >
                        快照
                    </button>
                </div>

                <div class="flex items-center gap-2 border-t pt-2 text-xs">
                    <span class="shrink-0 text-muted-foreground">旋转角度</span>
                    <select
                        v-model.number="rotationAngle"
                        :disabled="!isOpen || rotationSaving"
                        class="min-w-0 flex-1 rounded border px-2 py-1 text-xs focus:outline-none disabled:opacity-40"
                    >
                        <option v-for="option in rotationOptions" :key="option.value" :value="option.value">
                            {{ option.label }}
                        </option>
                    </select>
                    <button
                        :disabled="!isOpen || rotationSaving || !rotationDirty"
                        class="rounded border px-2 py-1 text-xs hover:bg-muted/50 disabled:opacity-40"
                        @click="void saveRotationAngle()"
                    >
                        {{ rotationSaving ? '保存中…' : '保存' }}
                    </button>
                </div>

                <!-- 实时运行指标 -->
                <div v-if="metrics" class="grid grid-cols-2 gap-x-2 gap-y-1 text-xs">
                    <span class="text-muted-foreground">帧率</span>
                    <span>{{ displayMetric(metrics.frameRate) }} fps</span>
                    <span class="text-muted-foreground">FPGA 温度</span>
                    <span>{{ displayMetric(metrics.fpgaTemperature, 0) }} °C</span>
                    <span class="text-muted-foreground">传感器温度</span>
                    <span>{{ displayMetric(metrics.sensorTemperature) }} °C</span>
                    <span class="text-muted-foreground">AE 状态</span>
                    <span>{{ metrics.aeStatus === 1 ? '运行中' : '空闲' }}</span>
                    <span class="text-muted-foreground">缓冲帧数</span>
                    <span>{{ displayMetric(metrics.currentBufFrames, 0) }}</span>
                </div>

                <!-- 快捷操作 -->
                <div class="flex flex-col gap-1.5 border-t pt-2">
                    <span class="text-xs font-medium text-muted-foreground">快捷操作</span>
                    <div class="flex gap-2">
                        <button
                            :disabled="!isOpen || busy"
                            class="flex-1 rounded border px-2 py-1.5 text-xs hover:bg-muted/50 disabled:opacity-40"
                            @click="void onSoftTrigger()"
                        >
                            软触发
                        </button>
                        <button
                            :disabled="!isOpen || busy"
                            class="flex-1 rounded border px-2 py-1.5 text-xs hover:bg-muted/50 disabled:opacity-40"
                            @click="void onExposureAutoOncePulse()"
                        >
                            单次自动曝光
                        </button>
                    </div>
                </div>

                <!-- 设备基本信息 -->
                <div v-if="device" class="border-t pt-2 text-xs text-muted-foreground">
                    <div>型号：{{ device.model ?? '—' }}</div>
                    <div>固件：{{ device.firmwareVersion ?? '—' }}</div>
                    <div>状态：{{ realtimeState?.statusText ?? device.statusText }}</div>
                    <div v-if="realtimeState?.sensorTemperature != null">
                        实时传感器温度：{{ realtimeState.sensorTemperature.toFixed(1) }} °C
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>
