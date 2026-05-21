<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCameraStore } from '@/stores/cameras'
import {
    CameraStatus,
    CameraAutoExposureMode,
    CameraGainMode,
    CameraBinningMode,
    CameraPixelDepth,
    CameraWhiteBalanceMode,
    type CameraDeviceInfoDto,
    type CameraImageParamsDto,
    type SetCameraImageParamsDto,
    type CameraAcquisitionParamsDto,
    type SetCameraAcquisitionParamsDto,
    type CameraTriggerParamsDto,
    type SetCameraTriggerParamsDto,
    type CameraCustomParamsDto,
    type SetCameraCustomParamsDto,
    type CameraLiveMetricsDto,
} from '@/api/cameras'
import { toast } from 'vue-sonner'

const route = useRoute()
const router = useRouter()
const store = useCameraStore()

// ─── 当前设备 ─────────────────────────────────────────────────────────────
const deviceId = computed<string>(() => route.params.id as string)
const device = computed(() => store.cameras.find((c) => c.id === deviceId.value) ?? store.selectedCamera)
const isOpen = computed(
    () => device.value?.status === CameraStatus.Ready || device.value?.status === CameraStatus.Capturing
)
const previewUrl = computed(() => store.previewFrames.get(deviceId.value) ?? null)
const metrics = computed<CameraLiveMetricsDto | null>(() => store.liveMetrics.get(deviceId.value) ?? null)

// ─── 设备信息 ─────────────────────────────────────────────────────────────
const deviceInfo = ref<CameraDeviceInfoDto | null>(null)

// ─── 图像参数 ─────────────────────────────────────────────────────────────
const imageParams = ref<CameraImageParamsDto | null>(null)
const imageParamsEditing = ref(false)
const imgForm = ref<SetCameraImageParamsDto>({})

// ─── 采集参数 ─────────────────────────────────────────────────────────────
const acquisitionParams = ref<CameraAcquisitionParamsDto | null>(null)
const acquisitionParamsEditing = ref(false)
const acqForm = ref<SetCameraAcquisitionParamsDto>({})

// ─── 触发参数 ─────────────────────────────────────────────────────────────
const triggerParams = ref<CameraTriggerParamsDto | null>(null)
const triggerEditing = ref(false)
const trgForm = ref<SetCameraTriggerParamsDto>({})

// ─── 自定义参数（WB / LED）────────────────────────────────────────────────
const customParams = ref<CameraCustomParamsDto | null>(null)
const customEditing = ref(false)
const customForm = ref<SetCameraCustomParamsDto>({})

// ─── 预览控制 ─────────────────────────────────────────────────────────────
const previewing = ref(false)
const busy = ref(false)

// ─── 通用执行包装 ──────────────────────────────────────────────────────────
async function run(fn: () => Promise<void>) {
    busy.value = true
    try {
        await fn()
    } finally {
        busy.value = false
    }
}

// ─── 加载各节参数 ─────────────────────────────────────────────────────────

async function loadDeviceInfo() {
    try {
        deviceInfo.value = await store.fetchDeviceInfo(deviceId.value)
    } catch {
        /* 忽略 */
    }
}

async function loadImageParams() {
    try {
        const p = await store.fetchImageParams(deviceId.value)
        imageParams.value = p
        imgForm.value = {
            roiEnabled: p.roiEnabled,
            roiHOffset: p.roiHOffset,
            roiVOffset: p.roiVOffset,
            roiWidth: p.roiWidth,
            roiHeight: p.roiHeight,
            pixelDepth: p.pixelDepth,
            horizontalFlip: p.horizontalFlip,
            verticalFlip: p.verticalFlip,
            binning: p.binning,
            gammaEnabled: p.gammaEnabled,
            gamma: p.gamma,
            contrast: p.contrast,
            brightness: p.brightness,
            frameRate: p.frameRate,
        }
    } catch {
        /* 忽略 */
    }
}

async function loadAcquisitionParams() {
    try {
        const p = await store.fetchAcquisitionParams(deviceId.value)
        acquisitionParams.value = p
        acqForm.value = {
            aeMode: p.aeMode,
            aeTargetGray: p.aeTargetGray,
            aeMaxExposure: p.aeMaxExposure,
            aeMinExposure: p.aeMinExposure,
            gainMode: p.gainMode,
            exposureTime: p.exposureTime,
            globalGain: p.globalGain,
        }
    } catch {
        /* 忽略 */
    }
}

async function loadTriggerParams() {
    try {
        const p = await store.fetchTriggerParams(deviceId.value)
        triggerParams.value = p
        trgForm.value = {
            triggerMode: p.triggerMode,
            expMode: p.expMode,
            edgeMode: p.edgeMode,
            delayTm: p.delayTm,
            frames: p.frames,
            bufFrames: p.bufFrames,
        }
    } catch {
        /* 忽略 */
    }
}

async function loadCustomParams() {
    try {
        const p = await store.fetchCustomParams(deviceId.value)
        customParams.value = p
        customForm.value = {
            wbMode: p.wbMode,
            channelGainR: p.channelGainR,
            channelGainG: p.channelGainG,
            channelGainB: p.channelGainB,
            saturation: p.saturation,
            colorTemperature: p.colorTemperature,
            ledEnabled: p.ledEnabled,
        }
    } catch {
        /* 忽略 */
    }
}

async function loadAll() {
    if (!isOpen.value) return
    await Promise.all([
        loadDeviceInfo(),
        loadImageParams(),
        loadAcquisitionParams(),
        loadTriggerParams(),
        loadCustomParams(),
    ])
}

// ─── 取消编辑 ─────────────────────────────────────────────────────────────

function cancelImageParams() {
    imageParamsEditing.value = false
    void loadImageParams()
}

function cancelAcquisitionParams() {
    acquisitionParamsEditing.value = false
    void loadAcquisitionParams()
}

function cancelTriggerParams() {
    triggerEditing.value = false
    void loadTriggerParams()
}

function cancelCustomParams() {
    customEditing.value = false
    void loadCustomParams()
}

// ─── 保存操作 ─────────────────────────────────────────────────────────────

async function saveImageParams() {
    await run(async () => {
        await store.applyImageParams(deviceId.value, imgForm.value)
        toast.success('图像参数已保存')
        imageParamsEditing.value = false
        await loadImageParams()
    })
}

async function saveAcquisitionParams() {
    await run(async () => {
        await store.applyAcquisitionParams(deviceId.value, acqForm.value)
        toast.success('采集参数已保存')
        acquisitionParamsEditing.value = false
        await loadAcquisitionParams()
    })
}

async function saveTriggerParams() {
    await run(async () => {
        await store.applyTriggerParams(deviceId.value, trgForm.value)
        toast.success('触发参数已保存')
        triggerEditing.value = false
        await loadTriggerParams()
    })
}

async function saveCustomParams() {
    await run(async () => {
        await store.applyCustomParams(deviceId.value, customForm.value)
        toast.success('自定义参数已保存')
        customEditing.value = false
        await loadCustomParams()
    })
}

// ─── 预览控制 ─────────────────────────────────────────────────────────────

async function togglePreview() {
    if (previewing.value) {
        await run(async () => {
            await store.stopCameraPreview(deviceId.value)
            previewing.value = false
            toast.success('预览已停止')
        })
    } else {
        await run(async () => {
            await store.startCameraPreview(deviceId.value, {
                connectionId: undefined,
                enableRtp: false,
            })
            previewing.value = true
            toast.success('预览已启动（SignalR 30FPS）')
        })
    }
}

async function onSnapshot() {
    await run(async () => {
        const snap = await store.snapshot(deviceId.value)
        // 在新标签页打开快照
        const win = window.open()
        if (win) {
            win.document.write(`<img src="${snap.dataUri}" style="max-width:100%" />`)
        }
        toast.success(`快照已拍摄：${new Date(snap.capturedAt).toLocaleTimeString()}`)
    })
}

async function onSoftTrigger() {
    await run(async () => {
        await store.softTrigger(deviceId.value)
        toast.success('软触发已发送')
    })
}

// ─── 枚举选项 ─────────────────────────────────────────────────────────────

const aeModeOptions = [
    { value: CameraAutoExposureMode.Off, label: '关闭' },
    { value: CameraAutoExposureMode.Once, label: '单次' },
    { value: CameraAutoExposureMode.Continuous, label: '连续' },
]

const gainModeOptions = [
    { value: CameraGainMode.Hdr, label: 'HDR' },
    { value: CameraGainMode.High, label: '高增益' },
    { value: CameraGainMode.Low, label: '低增益' },
]

const binningOptions = [
    { value: CameraBinningMode.Off, label: '关闭' },
    { value: CameraBinningMode.X2, label: '×2' },
    { value: CameraBinningMode.X4, label: '×4' },
]

const pixelDepthOptions = [
    { value: CameraPixelDepth.Bit8, label: '8 bit' },
    { value: CameraPixelDepth.Bit12, label: '12 bit' },
]

const wbModeOptions = [
    { value: CameraWhiteBalanceMode.Manual, label: '手动' },
    { value: CameraWhiteBalanceMode.Once, label: '单次' },
    { value: CameraWhiteBalanceMode.Continuous, label: '连续' },
]

// ─── 侦听设备打开状态，自动加载参数 ──────────────────────────────────────
watch(isOpen, (val) => {
    if (val) void loadAll()
})

onMounted(async () => {
    if (!device.value) {
        await store.refreshCamera(deviceId.value).catch(() => {
            void router.push({ name: 'CameraManage' })
        })
    }
    await loadAll()
})

onUnmounted(async () => {
    // 离开控制页时停止预览
    if (previewing.value) {
        await store.stopCameraPreview(deviceId.value).catch(() => {})
    }
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <!-- 顶部标题与状态 -->
        <div class="flex items-center justify-between">
            <div class="flex items-center gap-3">
                <button
                    class="rounded border px-2 py-1 text-xs hover:bg-muted/50"
                    @click="void router.push({ name: 'CameraManage' })"
                >
                    ← 返回
                </button>
                <h1 class="text-lg font-semibold">
                    {{ device?.name ?? '相机控制' }}
                </h1>
                <span
                    :class="[
                        'rounded px-2 py-0.5 text-xs font-medium',
                        isOpen ? 'bg-green-100 text-green-700' : 'bg-muted text-muted-foreground',
                    ]"
                >
                    {{ isOpen ? '已打开' : '已关闭' }}
                </span>
            </div>
            <div class="flex items-center gap-2">
                <span class="text-xs text-muted-foreground">SN: {{ device?.serialNumber ?? '—' }}</span>
            </div>
        </div>

        <div class="grid grid-cols-[1fr_320px] gap-4">
            <!-- 左侧参数面板 -->
            <div class="flex flex-col gap-4">
                <!-- ── 设备信息 ── -->
                <section class="rounded-lg border">
                    <div class="flex items-center justify-between border-b px-4 py-2">
                        <span class="text-sm font-medium">设备信息</span>
                        <button
                            :disabled="!isOpen"
                            class="text-xs text-primary hover:underline disabled:opacity-40"
                            @click="loadDeviceInfo"
                        >
                            刷新
                        </button>
                    </div>
                    <div v-if="deviceInfo" class="grid grid-cols-2 gap-x-4 gap-y-1.5 p-4 text-sm">
                        <div class="text-muted-foreground">型号</div>
                        <div>{{ deviceInfo.model }}</div>
                        <div class="text-muted-foreground">序列号</div>
                        <div class="font-mono">{{ deviceInfo.serialNumber }}</div>
                        <div class="text-muted-foreground">固件版本</div>
                        <div>{{ deviceInfo.firmwareVersion }}</div>
                        <div class="text-muted-foreground">FPGA 版本</div>
                        <div>{{ deviceInfo.fpgaVersion }}</div>
                        <div class="text-muted-foreground">图像分辨率</div>
                        <div>{{ deviceInfo.currentWidth }} × {{ deviceInfo.currentHeight }}</div>
                        <div class="text-muted-foreground">FPGA 温度</div>
                        <div>{{ deviceInfo.fpgaTemperature }} °C</div>
                        <div class="text-muted-foreground">传感器温度</div>
                        <div>{{ deviceInfo.sensorTemperature.toFixed(1) }} °C</div>
                    </div>
                    <div v-else class="px-4 py-3 text-sm text-muted-foreground">
                        {{ isOpen ? '加载中…' : '请先打开相机' }}
                    </div>
                </section>

                <!-- ── 图像参数 ── -->
                <section class="rounded-lg border">
                    <div class="flex items-center justify-between border-b px-4 py-2">
                        <span class="text-sm font-medium">图像参数</span>
                        <div class="flex gap-2">
                            <button
                                v-if="!imageParamsEditing"
                                :disabled="!isOpen"
                                class="text-xs text-primary hover:underline disabled:opacity-40"
                                @click="imageParamsEditing = true"
                            >
                                编辑
                            </button>
                            <template v-else>
                                <button
                                    class="text-xs text-muted-foreground hover:underline"
                                    @click="cancelImageParams"
                                >
                                    取消
                                </button>
                                <button
                                    :disabled="busy"
                                    class="text-xs text-primary hover:underline disabled:opacity-40"
                                    @click="saveImageParams"
                                >
                                    保存
                                </button>
                            </template>
                        </div>
                    </div>
                    <div v-if="imageParams" class="p-4">
                        <div class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                            <!-- 像素位深 -->
                            <label class="text-muted-foreground">像素位深</label>
                            <div v-if="!imageParamsEditing">
                                {{ imgForm.pixelDepth === CameraPixelDepth.Bit8 ? '8 bit' : '12 bit' }}
                            </div>
                            <select
                                v-else
                                v-model="imgForm.pixelDepth"
                                class="rounded border bg-background px-1 py-0.5 text-sm"
                            >
                                <option v-for="opt in pixelDepthOptions" :key="opt.value" :value="opt.value">
                                    {{ opt.label }}
                                </option>
                            </select>

                            <!-- Binning -->
                            <label class="text-muted-foreground">Binning</label>
                            <div v-if="!imageParamsEditing">
                                {{ binningOptions.find((o) => o.value === imgForm.binning)?.label }}
                            </div>
                            <select
                                v-else
                                v-model="imgForm.binning"
                                class="rounded border bg-background px-1 py-0.5 text-sm"
                            >
                                <option v-for="opt in binningOptions" :key="opt.value" :value="opt.value">
                                    {{ opt.label }}
                                </option>
                            </select>

                            <!-- 帧率 -->
                            <label class="text-muted-foreground">目标帧率</label>
                            <div v-if="!imageParamsEditing">
                                {{ imgForm.frameRate }} FPS（最大 {{ imageParams.frameRateMax }}）
                            </div>
                            <input
                                v-else
                                v-model.number="imgForm.frameRate"
                                type="number"
                                min="1"
                                class="rounded border bg-background px-1 py-0.5 text-sm"
                            />

                            <!-- 水平翻转 -->
                            <label class="text-muted-foreground">水平翻转</label>
                            <div v-if="!imageParamsEditing">{{ imgForm.horizontalFlip ? '是' : '否' }}</div>
                            <input v-else v-model="imgForm.horizontalFlip" type="checkbox" />

                            <!-- 垂直翻转 -->
                            <label class="text-muted-foreground">垂直翻转</label>
                            <div v-if="!imageParamsEditing">{{ imgForm.verticalFlip ? '是' : '否' }}</div>
                            <input v-else v-model="imgForm.verticalFlip" type="checkbox" />

                            <!-- Gamma -->
                            <label class="text-muted-foreground">Gamma 使能</label>
                            <div v-if="!imageParamsEditing">{{ imgForm.gammaEnabled ? '开启' : '关闭' }}</div>
                            <input v-else v-model="imgForm.gammaEnabled" type="checkbox" />

                            <label class="text-muted-foreground">Gamma 值</label>
                            <div v-if="!imageParamsEditing">{{ imgForm.gamma }}</div>
                            <input
                                v-else
                                v-model.number="imgForm.gamma"
                                type="number"
                                step="0.01"
                                class="rounded border bg-background px-1 py-0.5 text-sm"
                            />

                            <!-- ROI -->
                            <label class="text-muted-foreground">ROI 使能</label>
                            <div v-if="!imageParamsEditing">{{ imgForm.roiEnabled ? '开启' : '关闭' }}</div>
                            <input v-else v-model="imgForm.roiEnabled" type="checkbox" />

                            <template v-if="imageParamsEditing && imgForm.roiEnabled">
                                <label class="text-muted-foreground">ROI 位置 (X, Y)</label>
                                <div class="flex gap-1">
                                    <input
                                        v-model.number="imgForm.roiHOffset"
                                        type="number"
                                        min="0"
                                        class="w-20 rounded border bg-background px-1 py-0.5 text-sm"
                                        placeholder="X"
                                    />
                                    <input
                                        v-model.number="imgForm.roiVOffset"
                                        type="number"
                                        min="0"
                                        class="w-20 rounded border bg-background px-1 py-0.5 text-sm"
                                        placeholder="Y"
                                    />
                                </div>
                                <label class="text-muted-foreground">ROI 大小 (W×H)</label>
                                <div class="flex gap-1">
                                    <input
                                        v-model.number="imgForm.roiWidth"
                                        type="number"
                                        min="1"
                                        class="w-20 rounded border bg-background px-1 py-0.5 text-sm"
                                        placeholder="W"
                                    />
                                    <input
                                        v-model.number="imgForm.roiHeight"
                                        type="number"
                                        min="1"
                                        class="w-20 rounded border bg-background px-1 py-0.5 text-sm"
                                        placeholder="H"
                                    />
                                </div>
                            </template>
                        </div>
                    </div>
                    <div v-else class="px-4 py-3 text-sm text-muted-foreground">
                        {{ isOpen ? '加载中…' : '请先打开相机' }}
                    </div>
                </section>

                <!-- ── 采集参数 ── -->
                <section class="rounded-lg border">
                    <div class="flex items-center justify-between border-b px-4 py-2">
                        <span class="text-sm font-medium">采集参数（曝光 / 增益）</span>
                        <div class="flex gap-2">
                            <button
                                v-if="!acquisitionParamsEditing"
                                :disabled="!isOpen"
                                class="text-xs text-primary hover:underline disabled:opacity-40"
                                @click="acquisitionParamsEditing = true"
                            >
                                编辑
                            </button>
                            <template v-else>
                                <button
                                    class="text-xs text-muted-foreground hover:underline"
                                    @click="cancelAcquisitionParams"
                                >
                                    取消
                                </button>
                                <button
                                    :disabled="busy"
                                    class="text-xs text-primary hover:underline disabled:opacity-40"
                                    @click="saveAcquisitionParams"
                                >
                                    保存
                                </button>
                            </template>
                        </div>
                    </div>
                    <div v-if="acquisitionParams" class="grid grid-cols-2 gap-x-4 gap-y-2 p-4 text-sm">
                        <label class="text-muted-foreground">AE 模式</label>
                        <div v-if="!acquisitionParamsEditing">
                            {{ aeModeOptions.find((o) => o.value === acqForm.aeMode)?.label }}
                        </div>
                        <select
                            v-else
                            v-model="acqForm.aeMode"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        >
                            <option v-for="opt in aeModeOptions" :key="opt.value" :value="opt.value">
                                {{ opt.label }}
                            </option>
                        </select>

                        <label class="text-muted-foreground">曝光时间 (μs)</label>
                        <div v-if="!acquisitionParamsEditing">{{ acqForm.exposureTime }}</div>
                        <input
                            v-else
                            v-model.number="acqForm.exposureTime"
                            type="number"
                            min="1"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />

                        <label class="text-muted-foreground">增益模式</label>
                        <div v-if="!acquisitionParamsEditing">
                            {{ gainModeOptions.find((o) => o.value === acqForm.gainMode)?.label }}
                        </div>
                        <select
                            v-else
                            v-model="acqForm.gainMode"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        >
                            <option v-for="opt in gainModeOptions" :key="opt.value" :value="opt.value">
                                {{ opt.label }}
                            </option>
                        </select>

                        <label class="text-muted-foreground">全局增益</label>
                        <div v-if="!acquisitionParamsEditing">{{ acqForm.globalGain }}</div>
                        <input
                            v-else
                            v-model.number="acqForm.globalGain"
                            type="number"
                            step="0.1"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />

                        <template v-if="acqForm.aeMode !== CameraAutoExposureMode.Off">
                            <label class="text-muted-foreground">AE 目标灰度</label>
                            <div v-if="!acquisitionParamsEditing">{{ acqForm.aeTargetGray }}</div>
                            <input
                                v-else
                                v-model.number="acqForm.aeTargetGray"
                                type="number"
                                min="0"
                                max="255"
                                class="rounded border bg-background px-1 py-0.5 text-sm"
                            />

                            <label class="text-muted-foreground">AE 最大曝光 (μs)</label>
                            <div v-if="!acquisitionParamsEditing">{{ acqForm.aeMaxExposure }}</div>
                            <input
                                v-else
                                v-model.number="acqForm.aeMaxExposure"
                                type="number"
                                min="1"
                                class="rounded border bg-background px-1 py-0.5 text-sm"
                            />

                            <label class="text-muted-foreground">AE 最小曝光 (μs)</label>
                            <div v-if="!acquisitionParamsEditing">{{ acqForm.aeMinExposure }}</div>
                            <input
                                v-else
                                v-model.number="acqForm.aeMinExposure"
                                type="number"
                                min="1"
                                class="rounded border bg-background px-1 py-0.5 text-sm"
                            />
                        </template>
                    </div>
                    <div v-else class="px-4 py-3 text-sm text-muted-foreground">
                        {{ isOpen ? '加载中…' : '请先打开相机' }}
                    </div>
                </section>

                <!-- ── 触发参数 ── -->
                <section class="rounded-lg border">
                    <div class="flex items-center justify-between border-b px-4 py-2">
                        <span class="text-sm font-medium">触发参数</span>
                        <div class="flex gap-2">
                            <button
                                v-if="!triggerEditing"
                                :disabled="!isOpen"
                                class="text-xs text-primary hover:underline disabled:opacity-40"
                                @click="triggerEditing = true"
                            >
                                编辑
                            </button>
                            <template v-else>
                                <button
                                    class="text-xs text-muted-foreground hover:underline"
                                    @click="cancelTriggerParams"
                                >
                                    取消
                                </button>
                                <button
                                    :disabled="busy"
                                    class="text-xs text-primary hover:underline disabled:opacity-40"
                                    @click="saveTriggerParams"
                                >
                                    保存
                                </button>
                            </template>
                        </div>
                    </div>
                    <div v-if="triggerParams" class="grid grid-cols-2 gap-x-4 gap-y-2 p-4 text-sm">
                        <label class="text-muted-foreground">触发模式</label>
                        <div v-if="!triggerEditing">{{ trgForm.triggerMode }}</div>
                        <select
                            v-else
                            v-model="trgForm.triggerMode"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        >
                            <option :value="0">0 - 连续</option>
                            <option :value="1">1 - 标准</option>
                            <option :value="2">2 - 同步</option>
                            <option :value="3">3 - 全局</option>
                            <option :value="4">4 - 软件</option>
                        </select>

                        <label class="text-muted-foreground">曝光模式</label>
                        <div v-if="!triggerEditing">{{ trgForm.expMode === 0 ? '全局' : '电子滚动' }}</div>
                        <select
                            v-else
                            v-model="trgForm.expMode"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        >
                            <option :value="0">全局</option>
                            <option :value="1">电子滚动</option>
                        </select>

                        <label class="text-muted-foreground">触发边沿</label>
                        <div v-if="!triggerEditing">{{ trgForm.edgeMode === 0 ? '上升沿' : '下降沿' }}</div>
                        <select
                            v-else
                            v-model="trgForm.edgeMode"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        >
                            <option :value="0">上升沿</option>
                            <option :value="1">下降沿</option>
                        </select>

                        <label class="text-muted-foreground">延迟时间</label>
                        <div v-if="!triggerEditing">{{ trgForm.delayTm }}</div>
                        <input
                            v-else
                            v-model.number="trgForm.delayTm"
                            type="number"
                            min="0"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />

                        <label class="text-muted-foreground">单次触发帧数</label>
                        <div v-if="!triggerEditing">{{ trgForm.frames }}</div>
                        <input
                            v-else
                            v-model.number="trgForm.frames"
                            type="number"
                            min="1"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />

                        <label class="text-muted-foreground">缓冲帧数</label>
                        <div v-if="!triggerEditing">{{ trgForm.bufFrames }}</div>
                        <input
                            v-else
                            v-model.number="trgForm.bufFrames"
                            type="number"
                            min="1"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />
                    </div>
                    <div v-else class="px-4 py-3 text-sm text-muted-foreground">
                        {{ isOpen ? '加载中…' : '请先打开相机' }}
                    </div>
                </section>

                <!-- ── 自定义参数 ── -->
                <section class="rounded-lg border">
                    <div class="flex items-center justify-between border-b px-4 py-2">
                        <span class="text-sm font-medium">自定义参数（白平衡 / LED）</span>
                        <div class="flex gap-2">
                            <button
                                v-if="!customEditing"
                                :disabled="!isOpen"
                                class="text-xs text-primary hover:underline disabled:opacity-40"
                                @click="customEditing = true"
                            >
                                编辑
                            </button>
                            <template v-else>
                                <button
                                    class="text-xs text-muted-foreground hover:underline"
                                    @click="cancelCustomParams"
                                >
                                    取消
                                </button>
                                <button
                                    :disabled="busy"
                                    class="text-xs text-primary hover:underline disabled:opacity-40"
                                    @click="saveCustomParams"
                                >
                                    保存
                                </button>
                            </template>
                        </div>
                    </div>
                    <div v-if="customParams" class="grid grid-cols-2 gap-x-4 gap-y-2 p-4 text-sm">
                        <label class="text-muted-foreground">白平衡模式</label>
                        <div v-if="!customEditing">
                            {{ wbModeOptions.find((o) => o.value === customForm.wbMode)?.label }}
                        </div>
                        <select
                            v-else
                            v-model="customForm.wbMode"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        >
                            <option v-for="opt in wbModeOptions" :key="opt.value" :value="opt.value">
                                {{ opt.label }}
                            </option>
                        </select>

                        <label class="text-muted-foreground">R 通道增益</label>
                        <div v-if="!customEditing">{{ customForm.channelGainR }}</div>
                        <input
                            v-else
                            v-model.number="customForm.channelGainR"
                            type="number"
                            step="0.01"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />

                        <label class="text-muted-foreground">G 通道增益</label>
                        <div v-if="!customEditing">{{ customForm.channelGainG }}</div>
                        <input
                            v-else
                            v-model.number="customForm.channelGainG"
                            type="number"
                            step="0.01"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />

                        <label class="text-muted-foreground">B 通道增益</label>
                        <div v-if="!customEditing">{{ customForm.channelGainB }}</div>
                        <input
                            v-else
                            v-model.number="customForm.channelGainB"
                            type="number"
                            step="0.01"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />

                        <label class="text-muted-foreground">饱和度</label>
                        <div v-if="!customEditing">{{ customForm.saturation }}</div>
                        <input
                            v-else
                            v-model.number="customForm.saturation"
                            type="number"
                            step="1"
                            class="rounded border bg-background px-1 py-0.5 text-sm"
                        />

                        <label class="text-muted-foreground">LED 使能</label>
                        <div v-if="!customEditing">{{ customForm.ledEnabled ? '开启' : '关闭' }}</div>
                        <input v-else v-model="customForm.ledEnabled" type="checkbox" />
                    </div>
                    <div v-else class="px-4 py-3 text-sm text-muted-foreground">
                        {{ isOpen ? '加载中…' : '请先打开相机' }}
                    </div>
                </section>
            </div>

            <!-- 右侧预览面板 -->
            <div class="flex flex-col gap-3">
                <!-- 预览图 -->
                <div class="overflow-hidden rounded-lg border bg-black">
                    <div class="flex items-center justify-between border-b border-white/10 px-3 py-1.5">
                        <span class="text-xs text-white/70">实时预览（SignalR 30FPS）</span>
                        <span :class="['size-2 rounded-full', previewing ? 'bg-green-400' : 'bg-white/20']" />
                    </div>
                    <div class="flex aspect-[4/3] items-center justify-center">
                        <img v-if="previewUrl" :src="previewUrl" class="h-full w-full object-contain" alt="相机预览" />
                        <span v-else class="text-xs text-white/30">无画面</span>
                    </div>
                </div>

                <!-- 预览控制按钮 -->
                <div class="flex gap-2">
                    <button
                        :disabled="!isOpen || busy"
                        :class="[
                            'flex-1 rounded border py-1.5 text-sm disabled:opacity-40',
                            previewing
                                ? 'border-orange-400 text-orange-500 hover:bg-orange-50'
                                : 'border-green-500 text-green-600 hover:bg-green-50',
                        ]"
                        @click="togglePreview"
                    >
                        {{ previewing ? '停止预览' : '开始预览' }}
                    </button>
                    <button
                        :disabled="!isOpen || busy"
                        class="rounded border px-3 py-1.5 text-sm hover:bg-muted/50 disabled:opacity-40"
                        @click="onSnapshot"
                    >
                        快照
                    </button>
                </div>

                <!-- 软触发按钮 -->
                <button
                    :disabled="!isOpen || busy"
                    class="w-full rounded border py-1.5 text-sm hover:bg-muted/50 disabled:opacity-40"
                    @click="onSoftTrigger"
                >
                    软件触发
                </button>

                <!-- 实时指标 -->
                <section v-if="metrics" class="rounded-lg border p-3">
                    <div class="mb-2 text-xs font-medium text-muted-foreground">实时指标</div>
                    <div class="grid grid-cols-2 gap-x-2 gap-y-1 text-xs">
                        <div class="text-muted-foreground">帧率</div>
                        <div>{{ metrics.frameRate.toFixed(1) }} FPS</div>
                        <div class="text-muted-foreground">FPGA 温度</div>
                        <div>{{ metrics.fpgaTemperature }} °C</div>
                        <div class="text-muted-foreground">传感器温度</div>
                        <div>{{ metrics.sensorTemperature.toFixed(1) }} °C</div>
                        <div class="text-muted-foreground">AE 状态</div>
                        <div>{{ metrics.aeStatus === 1 ? '运行中' : '停止' }}</div>
                        <div class="text-muted-foreground">缓冲帧数</div>
                        <div>{{ metrics.currentBufFrames }}</div>
                    </div>
                </section>
            </div>
        </div>
    </div>
</template>
