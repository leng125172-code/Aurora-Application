<script setup lang="ts">
// 动态 GenICam 驱动的相机控制页面（PrimeVue 重构版）
// 左侧：按 NodeMap.categories 动态渲染参数分组
// 右侧：预览 / 快照 / 旋转角度（软件端）/ 实时指标 / 快捷操作
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import Select from 'primevue/select'
import { useCameraStore } from '@/stores/cameras'
import { CameraCapability, CameraStatus, type CameraLiveMetricsDto, type GenICamNodeDto } from '@/api/cameras'
import GenICamCategoryCard from '@/components/camera/GenICamCategoryCard.vue'
import { useAppToast } from '@/composables/useAppToast'
import { AppCard } from '@/components/primevue'

const route = useRoute()
const router = useRouter()
const store = useCameraStore()
const { t } = useI18n()
const toast = useAppToast()

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
const supportsParameterNodes = computed(
    () => !!device.value && (device.value.capabilities & CameraCapability.ParameterNodes) !== 0
)
const supportsPreview = computed(() => !!device.value && (device.value.capabilities & CameraCapability.Preview) !== 0)
const supportsSnapshot = computed(() => !!device.value && (device.value.capabilities & CameraCapability.Snapshot) !== 0)
const supportsSoftwareTrigger = computed(
    () => !!device.value && (device.value.capabilities & CameraCapability.SoftwareTrigger) !== 0
)
const supportsTemperature = computed(
    () => !!device.value && (device.value.capabilities & CameraCapability.Temperature) !== 0
)

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
const visibilityOptions = [
    { value: 'Beginner', label: 'Beginner' },
    { value: 'Expert', label: 'Expert' },
    { value: 'Guru', label: 'Guru' },
]

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
        toast.error(t('camera.loadNodeMapFailed', { msg }))
    } finally {
        nodeMapLoading.value = false
    }
}

/** 批量读取指定节点最新值，并同步更新 access 状态 */
async function readNodes(nodeNames: string[]) {
    if (nodeNames.length === 0 || !nodeMap.value) return
    // lookup 基于 allNodes；normalize 后 categories.nodes 与之共享引用，修改同步生效
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
            // 读取成功时才更新值，避免将旧的 currentValue 覆盖为 null
            if (r.success) {
                nodeValues.value[r.nodeName] = r.value ?? null
            }
            // 同步更新节点的当前 access（Selector 切换后实时生效）
            if (r.access) {
                const node = lookup.get(r.nodeName)
                if (node) node.access = r.access
            }
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
        // 递归收集分类及所有后代子分类的节点名，确保刷新覆盖嵌套节点
        const nodeNames: string[] = []
        function collectNodeNames(c: NonNullable<typeof cat>) {
            for (const n of c.nodes) nodeNames.push(n.nodeName)
            for (const child of c.children) collectNodeNames(child)
        }
        collectNodeNames(cat)
        await readNodes(nodeNames)
    } finally {
        categoryLoading.value[categoryName] = false
    }
}

/** 节点写入后回调：立即本地更新值，再异步刷新该节点及受影响节点的最新值+access */
async function onNodeUpdated(node: GenICamNodeDto, newValue: string) {
    // 立即本地更新，无需等待网络（写入已成功，值可信）
    nodeValues.value[node.nodeName] = newValue
    // 再从后端读取最新值+access（含受影响节点）
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
    { value: 0, label: t('camera.clockwiseRotation', { angle: 0 }) },
    { value: 90, label: t('camera.clockwiseRotation', { angle: 90 }) },
    { value: 180, label: t('camera.clockwiseRotation', { angle: 180 }) },
    { value: 270, label: t('camera.clockwiseRotation', { angle: 270 }) },
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
        toast.success(t('camera.rotationSaved'))
    } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e)
        toast.error(t('camera.rotationSaveFailed', { msg }))
    } finally {
        rotationSaving.value = false
    }
}

// ─── 预览控制 ────────────────────────────────────────────────────────────
const previewing = ref(false)
const previewTab = ref<'video' | 'snapshot'>('video')
const snapshotUri = ref<string | null>(null)
const busy = ref(false)
const isFullscreen = ref(false)

async function run(fn: () => Promise<void>) {
    busy.value = true
    try {
        await fn()
    } finally {
        busy.value = false
    }
}

async function togglePreview() {
    if (!supportsPreview.value) return
    if (previewing.value) {
        try {
            await run(async () => {
                await store.stopCameraPreview(deviceId.value)
                previewing.value = false
                toast.success(t('camera.previewStopped'))
            })
        } catch (e: unknown) {
            const msg = e instanceof Error ? e.message : String(e)
            toast.error(t('camera.stopPreviewFailed', { msg }))
        }
    } else {
        try {
            await run(async () => {
                await store.startCameraPreview(deviceId.value, { enableRtp: false })
                previewing.value = true
                toast.success(t('camera.previewStarted'))
            })
        } catch (e: unknown) {
            previewing.value = false
            const msg = e instanceof Error ? e.message : String(e)
            toast.error(t('camera.startPreviewFailed', { msg }))
        }
    }
}

async function onSnapshot() {
    if (!supportsSnapshot.value) return
    await run(async () => {
        const snap = await store.snapshot(deviceId.value)
        snapshotUri.value = snap.dataUri
        previewTab.value = 'snapshot'
        toast.success(t('camera.snapshotTaken', { time: new Date(snap.capturedAt).toLocaleTimeString() }))
    })
}

async function onSoftTrigger() {
    if (!supportsSoftwareTrigger.value) return
    await run(async () => {
        await store.softTrigger(deviceId.value)
        toast.success(t('camera.softTriggerSent'))
    })
}

async function onExposureAutoOncePulse() {
    await run(async () => {
        await store.exposureAutoOncePulse(deviceId.value)
        toast.success(t('camera.exposureAutoOnceDone'))
    })
}

function toggleFullscreen() {
    isFullscreen.value = !isFullscreen.value
}

function closeFullscreen() {
    isFullscreen.value = false
}

function displayMetric(value: number | null | undefined, digits = 1): string {
    if (typeof value !== 'number' || !Number.isFinite(value)) return '—'
    return value.toFixed(digits)
}

/**
 * 根据评分（0-100）返回渐变颜色：0=红色，50=黄色，100=绿色。
 * 使用 HSL 色相线性映射：score * 1.2 → [0°, 120°]。
 */
function scoreColor(score: number): string {
    const hue = Math.round(Math.max(0, Math.min(120, score * 1.2)))
    return `hsl(${hue}, 75%, 42%)`
}

// ─── 全部刷新 ────────────────────────────────────────────────────────────
async function refreshAll() {
    if (supportsParameterNodes.value && !nodeMap.value) {
        await loadNodeMap(false)
    } else if (supportsParameterNodes.value && nodeMap.value) {
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
        if (supportsParameterNodes.value) {
            void loadNodeMap(false)
        }
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
    if (!supportsPreview.value && supportsSnapshot.value) {
        previewTab.value = 'snapshot'
    }
    // 页面加载/刷新/返回时：向 Hub 续约宽限期并拉取最新快照，恢复 UI 状态
    if (supportsParameterNodes.value) {
        try {
            const snapshot = await store.loadCameraSnapshotState(deviceId.value)
            previewing.value = snapshot.isPreviewing
            if (snapshot.nodeMap) {
                const initial: Record<string, string | null> = {}
                for (const n of snapshot.nodeMap.allNodes) {
                    initial[n.nodeName] = n.currentValue
                }
                nodeValues.value = initial
            }
        } catch {
            if (isOpen.value) {
                void loadNodeMap(false)
            }
        }
    }
    if (isOpen.value) {
        void loadImageParams()
    }
})

onUnmounted(() => {
    // 预览不在此处主动停止：Hub 断开后有 30s 宽限期，可通过 ReattachPreviewAsync 续约
    // 用户可通过"停止预览"按钮主动停止
})
</script>

<template>
    <div class="flex flex-col gap-4">
        <!-- ── 顶部标题栏 ── -->
        <div class="flex flex-wrap items-center gap-3">
            <Button
                severity="secondary"
                outlined
                size="small"
                class="!text-xs"
                @click="void router.push({ name: 'CameraManage' })"
            >
                {{ t('camera.back') }}
            </Button>
            <h1 class="text-2xl font-bold tracking-tight">{{ device?.name ?? t('camera.ctrlTitle') }}</h1>
            <span
                :class="[
                    'rounded px-2 py-0.5 text-xs font-medium',
                    isOpen
                        ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300'
                        : 'bg-muted text-muted-foreground',
                ]"
            >
                {{ isOpen ? t('camera.ctrlOpen') : t('camera.ctrlClosed') }}
            </span>
            <span v-if="realtimeState?.isXmlLoaded === false" class="text-xs text-amber-500">
                {{ t('camera.nodMapLoading') }}
            </span>

            <div class="ml-auto flex shrink-0 items-center gap-2">
                <!-- Visibility 筛选 -->
                <label class="whitespace-nowrap text-xs text-muted-foreground">{{ t('camera.visibilityLabel') }}</label>
                <Select
                    v-model="visibility"
                    :options="visibilityOptions"
                    option-label="label"
                    option-value="value"
                    size="small"
                    class="!text-xs w-[9rem]"
                    :pt="{
                        root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                        label: {
                            class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                        },
                        dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                    }"
                />
                <!-- 重新枚举（强制 NodeMap 刷新） -->
                <Button
                    severity="secondary"
                    outlined
                    size="small"
                    class="!text-xs whitespace-nowrap"
                    :disabled="!isOpen || nodeMapLoading"
                    @click="void loadNodeMap(true)"
                >
                    {{ nodeMapLoading ? t('camera.enumerating') : t('camera.reenumerate') }}
                </Button>
                <!-- 全部刷新 -->
                <Button
                    severity="secondary"
                    outlined
                    size="small"
                    class="!text-xs whitespace-nowrap"
                    :disabled="!isOpen"
                    @click="void refreshAll()"
                >
                    {{ t('camera.refreshAll') }}
                </Button>
            </div>
        </div>

        <!-- ── 主体：左侧预览与控制，右侧动态节点 ── -->
        <div class="grid grid-cols-1 gap-4 xl:grid-cols-[380px_1fr]">
            <!-- 左：预览与控制（sticky 跟随滚动） -->
            <div class="xl:sticky xl:top-4 xl:self-start xl:order-first">
                <AppCard :beam-size="80" :beam-duration="8">
                    <div class="flex flex-col gap-3 p-3">
                        <!-- Tab 切换 -->
                        <div class="flex items-center gap-1 border-b pb-2">
                            <Button
                                v-if="supportsPreview"
                                text
                                size="small"
                                :severity="previewTab === 'video' ? 'primary' : 'secondary'"
                                @click="previewTab = 'video'"
                            >
                                {{ t('camera.tabVideo') }}
                            </Button>
                            <Button
                                v-if="supportsSnapshot"
                                text
                                size="small"
                                :severity="previewTab === 'snapshot' ? 'primary' : 'secondary'"
                                @click="previewTab = 'snapshot'"
                            >
                                {{ t('camera.tabSnapshot') }}
                            </Button>
                        </div>

                        <!-- 图像显示区域 -->
                        <div class="relative aspect-video overflow-hidden rounded bg-black">
                            <img
                                v-if="previewTab === 'video' && previewUrl"
                                :src="previewUrl"
                                class="h-full w-full object-contain"
                                :alt="t('camera.tabVideo')"
                            />
                            <div
                                v-if="previewTab === 'video' && previewUrl && previewing"
                                class="preview-crosshair"
                                aria-hidden="true"
                            />
                            <img
                                v-else-if="previewTab === 'snapshot' && snapshotUri"
                                :src="snapshotUri"
                                class="h-full w-full object-contain"
                                :alt="t('camera.snapshot')"
                            />
                            <div v-else class="flex h-full items-center justify-center text-xs text-white/40">
                                {{
                                    previewTab === 'video'
                                        ? previewing
                                            ? t('camera.waitingFrame')
                                            : t('camera.previewNotStarted')
                                        : t('camera.noSnapshot')
                                }}
                            </div>
                            <Button
                                text
                                size="small"
                                class="absolute right-2 bottom-2 bg-black/50 hover:bg-black/70 text-white rounded-full p-1.5"
                                @click="toggleFullscreen"
                            >
                                <svg
                                    xmlns="http://www.w3.org/2000/svg"
                                    width="20"
                                    height="20"
                                    viewBox="0 0 24 24"
                                    fill="none"
                                    stroke="currentColor"
                                    stroke-width="2"
                                    stroke-linecap="round"
                                    stroke-linejoin="round"
                                >
                                    <path
                                        d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"
                                    />
                                </svg>
                            </Button>
                        </div>

                        <!-- 图像质量评分条（仅实时预览时显示） -->
                        <div v-if="previewing && metrics" class="rounded border bg-muted/20 px-3 py-2 text-xs">
                            <!-- 对焦清晰度 -->
                            <div class="mb-2">
                                <div class="mb-1 flex items-center justify-between">
                                    <span class="text-muted-foreground">{{ t('camera.focusScore') }}</span>
                                    <span
                                        class="font-mono font-medium"
                                        :style="{ color: scoreColor(metrics.focusScore) }"
                                    >
                                        {{ Math.round(metrics.focusScore) }}
                                    </span>
                                </div>
                                <div class="h-2.5 w-full overflow-hidden rounded-full bg-muted/40">
                                    <div
                                        class="h-full rounded-full transition-all duration-300"
                                        :style="{
                                            width: `${metrics.focusScore}%`,
                                            backgroundColor: scoreColor(metrics.focusScore),
                                        }"
                                    />
                                </div>
                            </div>
                            <!-- 曝光质量 -->
                            <div>
                                <div class="mb-1 flex items-center justify-between">
                                    <span class="text-muted-foreground">{{ t('camera.apertureScore') }}</span>
                                    <span class="flex items-center gap-1.5">
                                        <span
                                            v-if="metrics.apertureHint !== 0"
                                            class="rounded px-1 py-0.5 text-[10px] font-medium"
                                            :style="{
                                                backgroundColor: metrics.apertureHint === 1 ? '#fef3c7' : '#dbeafe',
                                                color: metrics.apertureHint === 1 ? '#92400e' : '#1e40af',
                                            }"
                                        >
                                            {{
                                                metrics.apertureHint === 1
                                                    ? t('camera.apertureDown')
                                                    : t('camera.apertureUp')
                                            }}
                                        </span>
                                        <span
                                            class="font-mono font-medium"
                                            :style="{ color: scoreColor(metrics.apertureScore) }"
                                        >
                                            {{ Math.round(metrics.apertureScore) }}
                                        </span>
                                    </span>
                                </div>
                                <div class="h-2.5 w-full overflow-hidden rounded-full bg-muted/40">
                                    <div
                                        class="h-full rounded-full transition-all duration-300"
                                        :style="{
                                            width: `${metrics.apertureScore}%`,
                                            backgroundColor: scoreColor(metrics.apertureScore),
                                        }"
                                    />
                                </div>
                            </div>
                        </div>

                        <!-- 预览控制按钮 -->
                        <div class="flex gap-2">
                            <Button
                                v-if="supportsPreview"
                                :severity="previewing ? 'danger' : 'secondary'"
                                :outlined="!previewing"
                                size="small"
                                class="!text-xs flex-1"
                                :disabled="!isOpen || busy"
                                @click="void togglePreview()"
                            >
                                {{ previewing ? t('camera.stopPreview') : t('camera.startPreview') }}
                            </Button>
                            <Button
                                v-if="supportsSnapshot"
                                severity="secondary"
                                outlined
                                size="small"
                                class="!text-xs"
                                :disabled="!isOpen || busy"
                                @click="void onSnapshot()"
                            >
                                {{ t('camera.snapshot') }}
                            </Button>
                        </div>

                        <!-- 旋转角度 -->
                        <div class="flex items-center gap-2 border-t pt-2 text-xs">
                            <span class="shrink-0 text-muted-foreground">{{ t('camera.rotationAngle') }}</span>
                            <Select
                                v-model="rotationAngle"
                                :options="rotationOptions"
                                option-label="label"
                                option-value="value"
                                :disabled="!isOpen || rotationSaving"
                                size="small"
                                class="!text-xs flex-1"
                                :pt="{
                                    root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                    label: {
                                        class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                    },
                                    dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                                }"
                            />
                            <Button
                                severity="secondary"
                                outlined
                                size="small"
                                class="!text-xs whitespace-nowrap"
                                :disabled="!isOpen || rotationSaving || !rotationDirty"
                                @click="void saveRotationAngle()"
                            >
                                {{ rotationSaving ? t('common.saving') : t('common.save') }}
                            </Button>
                        </div>

                        <!-- 实时运行指标 -->
                        <div v-if="metrics" class="grid grid-cols-2 gap-x-2 gap-y-1 text-xs">
                            <span class="text-muted-foreground">{{ t('camera.frameRate') }}</span>
                            <span>{{ displayMetric(metrics.frameRate) }} fps</span>
                            <template v-if="supportsTemperature">
                                <span class="text-muted-foreground">{{ t('camera.fpgaTemp') }}</span>
                                <span>{{ displayMetric(metrics.fpgaTemperature, 0) }} °C</span>
                                <span class="text-muted-foreground">{{ t('camera.sensorTemp') }}</span>
                                <span>{{ displayMetric(metrics.sensorTemperature) }} °C</span>
                            </template>
                            <span class="text-muted-foreground">{{ t('camera.aeStatus') }}</span>
                            <span>{{ metrics.aeStatus === 1 ? t('camera.aeRunning') : t('camera.aeIdle') }}</span>
                            <span class="text-muted-foreground">{{ t('camera.bufFrames') }}</span>
                            <span>{{ displayMetric(metrics.currentBufFrames, 0) }}</span>
                        </div>

                        <!-- 快捷操作 -->
                        <div class="flex flex-col gap-1.5 border-t pt-2">
                            <span class="text-xs font-medium text-muted-foreground">
                                {{ t('camera.quickActions') }}
                            </span>
                            <div class="flex gap-2">
                                <Button
                                    v-if="supportsSoftwareTrigger"
                                    severity="secondary"
                                    outlined
                                    size="small"
                                    :disabled="!isOpen || busy"
                                    class="flex-1 !text-xs whitespace-nowrap"
                                    @click="void onSoftTrigger()"
                                >
                                    {{ t('camera.softTrigger') }}
                                </Button>
                                <Button
                                    v-if="supportsParameterNodes"
                                    severity="secondary"
                                    outlined
                                    size="small"
                                    :disabled="!isOpen || busy"
                                    class="flex-1 !text-xs whitespace-nowrap"
                                    @click="void onExposureAutoOncePulse()"
                                >
                                    {{ t('camera.exposureAutoOnce') }}
                                </Button>
                            </div>
                        </div>

                        <!-- 设备基本信息 -->
                        <div v-if="device" class="border-t pt-2 text-xs text-muted-foreground">
                            <div>{{ t('camera.ctrlModel') }}{{ device.model ?? '—' }}</div>
                            <div>{{ t('camera.ctrlStatus') }}{{ realtimeState?.statusText ?? device.statusText }}</div>
                            <div v-if="realtimeState?.sensorTemperature != null">
                                {{ t('camera.realtimeSensorTemp') }}{{ realtimeState.sensorTemperature.toFixed(1) }} °C
                            </div>
                        </div>
                    </div>
                </AppCard>
            </div>

            <!-- 右：动态节点卡片 -->
            <div class="grid auto-rows-min grid-cols-1 gap-3 md:grid-cols-2">
                <template v-if="supportsParameterNodes && nodeMap && nodeMap.categories.length > 0">
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
                <div v-else class="rounded-lg border p-6 text-center text-sm text-muted-foreground md:col-span-2">
                    {{
                        !supportsParameterNodes
                            ? '当前相机驱动未提供动态参数节点'
                            : isOpen
                              ? nodeMapLoading
                                  ? t('camera.nodMapLoadingFull')
                                  : t('camera.noNodeMap')
                              : t('camera.cameraNotOpen')
                    }}
                </div>
            </div>
        </div>
    </div>

    <!-- 网页内全屏遮罩层 -->
    <Teleport to="body">
        <Transition name="fade">
            <div
                v-if="isFullscreen"
                class="fixed inset-0 z-[100] bg-black flex items-center justify-center"
                @click.self="closeFullscreen"
            >
                <div class="relative w-full h-full max-w-[95vw] max-h-[95vh]">
                    <img
                        v-if="previewTab === 'video' && previewUrl"
                        :src="previewUrl"
                        class="w-full h-full object-contain"
                        :alt="t('camera.tabVideo')"
                    />
                    <div
                        v-if="previewTab === 'video' && previewUrl && previewing"
                        class="preview-crosshair"
                        aria-hidden="true"
                    />
                    <img
                        v-else-if="previewTab === 'snapshot' && snapshotUri"
                        :src="snapshotUri"
                        class="w-full h-full object-contain"
                        :alt="t('camera.snapshot')"
                    />
                    <div v-else class="flex h-full items-center justify-center text-xs text-white/40">
                        {{
                            previewTab === 'video'
                                ? previewing
                                    ? t('camera.waitingFrame')
                                    : t('camera.previewNotStarted')
                                : t('camera.noSnapshot')
                        }}
                    </div>
                    <Button
                        text
                        size="small"
                        class="absolute top-4 right-4 bg-black/50 hover:bg-black/70 text-white rounded-full p-2"
                        @click="closeFullscreen"
                    >
                        <svg
                            xmlns="http://www.w3.org/2000/svg"
                            width="24"
                            height="24"
                            viewBox="0 0 24 24"
                            fill="none"
                            stroke="currentColor"
                            stroke-width="2"
                            stroke-linecap="round"
                            stroke-linejoin="round"
                        >
                            <path d="M15 3h6v6M9 21H3v-6m12 0l5-5-5-5M6 18l-5-5 5-5" />
                        </svg>
                    </Button>
                    <div class="absolute bottom-4 left-4 text-white/70 text-xs">
                        {{ t('camera.fullscreenTip') }}
                    </div>
                </div>
            </div>
        </Transition>
    </Teleport>
</template>

<style scoped>
.preview-crosshair {
    position: absolute;
    inset: 0;
    z-index: 1;
    pointer-events: none;
}

.preview-crosshair::before,
.preview-crosshair::after {
    position: absolute;
    content: '';
    background: rgb(255 64 64 / 95%);
    box-shadow: 0 0 0 1px rgb(0 0 0 / 70%), 0 0 5px rgb(255 255 255 / 70%);
}

.preview-crosshair::before {
    top: 0;
    bottom: 0;
    left: 50%;
    width: 1px;
    transform: translateX(-50%);
}

.preview-crosshair::after {
    top: 50%;
    right: 0;
    left: 0;
    height: 1px;
    transform: translateY(-50%);
}

.fade-enter-active,
.fade-leave-active {
    transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
    opacity: 0;
}
</style>
