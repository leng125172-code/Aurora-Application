<script setup lang="ts">
// 光机控制台：PrimeVue 重构版（AppCard + Button + InputNumber + Slider + ColorPicker）
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import InputNumber from 'primevue/inputnumber'
import Slider from 'primevue/slider'
import ColorPicker from 'primevue/colorpicker'
import ConfirmDialog from 'primevue/confirmdialog'
import { useProjectorStore } from '@/stores/projectors'
import {
    ProjectorConnectionStatus,
    ProjectorDisplayMode,
    ProjectorColor,
    ProjectorFlipMode,
    ProjectorTriggerMode,
    ProjectorBootImage,
} from '@/api/projectors'
import { useAppToast } from '@/composables/useAppToast'
import { AppCard } from '@/components/primevue'

const route = useRoute()
const router = useRouter()
const store = useProjectorStore()
const { t } = useI18n()
const toast = useAppToast()
const confirm = useConfirm()

// ─── 当前设备 ─────────────────────────────────────────────────────────────
const deviceId = computed<string>(() => route.params.id as string)
const device = computed(() => store.projectors.find((p) => p.id === deviceId.value) ?? store.selectedProjector)
const isConnected = computed(() => device.value?.connectionStatus === ProjectorConnectionStatus.Connected)

// ─── 操作忙碌状态 ─────────────────────────────────────────────────────────
const busy = ref(false)

async function run(fn: () => Promise<boolean | void>) {
    busy.value = true
    try {
        const ok = await fn()
        if (ok === false) toast.error(t('projector.operationFailed'))
    } finally {
        busy.value = false
    }
}

// ─── LED 控制 ─────────────────────────────────────────────────────────────
async function onLedOn() {
    await run(async () => {
        await store.ledOn(deviceId.value)
    })
}
async function onLedOff() {
    await run(() => store.ledOff(deviceId.value))
}

// ─── 显示模式（含设为开机图） ──────────────────────────────────────────────
const displayModeOptions = computed(() => [
    { value: ProjectorDisplayMode.Black, label: t('projector.displayBlack') },
    { value: ProjectorDisplayMode.White, label: t('projector.displayWhite') },
    { value: ProjectorDisplayMode.Cross, label: t('projector.displayCross') },
    { value: ProjectorDisplayMode.Checkerboard, label: t('projector.displayCheckerboard') },
    { value: ProjectorDisplayMode.Internal1, label: t('projector.displayInternal1') },
    { value: ProjectorDisplayMode.Internal2, label: t('projector.displayInternal2') },
])
async function onSetDisplayMode(mode: ProjectorDisplayMode) {
    await run(() => store.setDisplayMode({ projectorDeviceId: deviceId.value, mode }))
}
// DisplayMode 与 BootImage 枚举值一一对应，直接转换
async function onSetBootFromDisplayMode() {
    const bootImage = device.value!.lastDisplayMode as unknown as ProjectorBootImage
    await run(() => store.setBootImage({ projectorDeviceId: deviceId.value, bootImage }))
    toast.success(t('projector.bootImageSet'))
}

// ─── 颜色（多光谱） ────────────────────────────────────────────────────────
const colorOptions = computed(() => [
    { value: ProjectorColor.AuraSync, label: 'Aura RGB' },
    { value: ProjectorColor.White, label: t('projector.colorWhite') },
    { value: ProjectorColor.Red, label: t('projector.colorRed') },
    { value: ProjectorColor.Green, label: t('projector.colorGreen') },
    { value: ProjectorColor.Blue, label: t('projector.colorBlue') },
])
// 本地选中的颜色 tab
const selectedColor = ref<ProjectorColor>(ProjectorColor.White)
// 单色亮度（0~175）
const colorBrightness = ref(100)

// 单色点击：立即切换颜色模式
async function onSelectMonoColor(color: ProjectorColor) {
    selectedColor.value = color
    await run(() => store.setColor({ projectorDeviceId: deviceId.value, color }))
}
// 单色亮度应用
async function onApplyMonoBrightness() {
    await run(() => store.setLight({ projectorDeviceId: deviceId.value, light: colorBrightness.value }))
}
// 仅切换到 Aura RGB tab（不发送命令，等用户选色后 apply）
function onSelectAuraRgb() {
    selectedColor.value = ProjectorColor.AuraSync
}

// ─── Aura RGB 色轮 ────────────────────────────────────────────────────────
const rgbR = ref(75)
const rgbG = ref(75)
const rgbB = ref(75)

// PrimeVue ColorPicker 使用 hex（不带 #），与 R/G/B 双向绑定
const rgbHex = computed({
    get() {
        const h = (v: number) => v.toString(16).padStart(2, '0')
        return `${h(rgbR.value)}${h(rgbG.value)}${h(rgbB.value)}`
    },
    set(hex: string) {
        const v = hex.startsWith('#') ? hex.slice(1) : hex
        if (v.length !== 6) return
        rgbR.value = parseInt(v.slice(0, 2), 16)
        rgbG.value = parseInt(v.slice(2, 4), 16)
        rgbB.value = parseInt(v.slice(4, 6), 16)
    },
})
async function onApplyRgb() {
    await run(() => store.setRgb({ projectorDeviceId: deviceId.value, r: rgbR.value, g: rgbG.value, b: rgbB.value }))
    // RGB 应用后保持 AuraSync 标签选中状态（防止 watch(device) 跳回白光标签）
    selectedColor.value = ProjectorColor.AuraSync
}

// ─── 翻转模式 ─────────────────────────────────────────────────────────────
const flipOptions = computed(() => [
    { value: ProjectorFlipMode.None, label: t('projector.flipNone') },
    { value: ProjectorFlipMode.FlipX, label: t('projector.flipX') },
    { value: ProjectorFlipMode.FlipY, label: t('projector.flipY') },
    { value: ProjectorFlipMode.FlipXY, label: t('projector.flipXY') },
])
async function onSetFlip(flipMode: ProjectorFlipMode) {
    await run(() => store.setFlip({ projectorDeviceId: deviceId.value, flipMode }))
}

// ─── 触发模式 ─────────────────────────────────────────────────────────────
const triggerModeOptions = computed(() => [
    { value: ProjectorTriggerMode.Normal, label: t('projector.triggerNormal') },
    { value: ProjectorTriggerMode.Loop, label: t('projector.triggerLoop') },
    { value: ProjectorTriggerMode.SingleFrame, label: t('projector.triggerSingleFrame') },
])
async function onSetTriggerMode(triggerMode: ProjectorTriggerMode) {
    await run(() => store.setTriggerMode({ projectorDeviceId: deviceId.value, triggerMode }))
}

// ─── 棋盘格像素尺寸 ────────────────────────────────────────────────────────
const checkerboardPixelSize = ref(30)
async function onSetCheckerboard() {
    await run(() =>
        store.setCheckerboard({ projectorDeviceId: deviceId.value, pixelSize: checkerboardPixelSize.value })
    )
}

// ─── 触发一次 ─────────────────────────────────────────────────────────────
async function onTriggerOnce() {
    await run(() => store.triggerOnce({ projectorDeviceId: deviceId.value }))
}

// ─── 下一帧（仅在单帧触发模式下有效） ─────────────────────────────────────
async function onNextFrame() {
    await run(() => store.nextFrame({ projectorDeviceId: deviceId.value }))
}

// ─── 高级操作 ─────────────────────────────────────────────────────────────
function onSoftReset() {
    confirm.require({
        message: t('projector.confirmSoftReset'),
        header: t('projector.softReset'),
        icon: 'pi pi-exclamation-triangle',
        acceptProps: { severity: 'danger', size: 'small' },
        rejectProps: { severity: 'secondary', outlined: true, size: 'small' },
        accept: async () => {
            await run(() => store.softReset(deviceId.value))
            toast.success(t('projector.softResetSent'))
        },
    })
}
async function onSaveParams() {
    await run(() => store.saveParams(deviceId.value))
    toast.success(t('projector.paramsSaved'))
}

// ─── 寄存器读写 ────────────────────────────────────────────────────────────
const registerAddr = ref(0)
const registerValue = ref(0)
const registerReadResult = ref<string | null>(null)

async function onReadRegister() {
    busy.value = true
    try {
        registerReadResult.value = await store.readRegister(deviceId.value, registerAddr.value)
    } finally {
        busy.value = false
    }
}
async function onWriteRegister() {
    await run(() =>
        store.writeRegister({
            projectorDeviceId: deviceId.value,
            address: registerAddr.value,
            value: registerValue.value,
        })
    )
}

// ─── 初始化 ────────────────────────────────────────────────────────────────
onMounted(async () => {
    if (store.projectors.length === 0) {
        await store.fetchList()
    }
    if (device.value) {
        selectedColor.value = device.value.lastColor
        colorBrightness.value = device.value.lastLightValue
        checkerboardPixelSize.value = device.value.checkerboardPixelSize
        rgbR.value = device.value.ledRgbR
        rgbG.value = device.value.ledRgbG
        rgbB.value = device.value.ledRgbB
    }
})

watch(device, (d) => {
    if (!d) return
    selectedColor.value = d.lastColor
    colorBrightness.value = d.lastLightValue
    checkerboardPixelSize.value = d.checkerboardPixelSize
    rgbR.value = d.ledRgbR
    rgbG.value = d.ledRgbG
    rgbB.value = d.ledRgbB
})
</script>

<template>
    <div class="flex flex-col gap-4">
        <ConfirmDialog />

        <!-- ─── 顶部：返回 + 设备信息 ────────────────────────────── -->
        <div class="flex items-center gap-3">
            <Button severity="secondary" outlined size="small" @click="router.back()">{{ t('camera.back') }}</Button>
            <div>
                <h1 class="text-2xl font-bold tracking-tight">{{ device?.name ?? t('projector.ctrlTitle') }}</h1>
            </div>
            <span
                :class="[
                    'ml-auto rounded-full px-2 py-0.5 text-xs font-medium',
                    isConnected
                        ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300'
                        : 'bg-muted text-muted-foreground',
                ]"
            >
                {{ isConnected ? t('projector.ctrlConnected') : t('projector.ctrlDisconnected') }}
            </span>
        </div>

        <div v-if="!device" class="py-12 text-center text-sm text-muted-foreground">
            {{ t('projector.deviceNotFound') }}
        </div>

        <template v-else>
            <!-- ─── 第一行：LED 控制 + 触发 / 高级 ───────────────── -->
            <div class="grid gap-4 grid-cols-[repeat(auto-fill,minmax(min(100%,20rem),1fr))]">
                <!-- LED 控制 -->
                <AppCard :beam-size="80" :beam-duration="8">
                    <div class="p-4">
                        <div class="mb-3 text-sm font-semibold">{{ t('projector.ledControl') }}</div>
                        <div class="flex gap-3">
                            <Button size="small" severity="warn" :disabled="!isConnected || busy" @click="onLedOn">
                                {{ t('projector.ledOn') }}
                            </Button>
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="!isConnected || busy"
                                @click="onLedOff"
                            >
                                {{ t('projector.ledOff') }}
                            </Button>
                        </div>
                    </div>
                </AppCard>

                <!-- 触发 / 高级 -->
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="2">
                    <div class="p-4">
                        <div class="mb-3 text-sm font-semibold">{{ t('projector.triggerAdvanced') }}</div>
                        <div class="flex flex-wrap gap-2">
                            <Button size="small" :disabled="!isConnected || busy" @click="onTriggerOnce">
                                {{ t('projector.triggerOnce') }}
                            </Button>
                            <Button
                                v-if="device.triggerMode === ProjectorTriggerMode.SingleFrame"
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="!isConnected || busy"
                                @click="onNextFrame"
                            >
                                {{ t('projector.triggerNextFrame') }}
                            </Button>
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="!isConnected || busy"
                                @click="onSaveParams"
                            >
                                {{ t('projector.saveParams') }}
                            </Button>
                            <Button
                                size="small"
                                severity="danger"
                                :disabled="!isConnected || busy"
                                @click="onSoftReset"
                            >
                                {{ t('projector.softReset') }}
                            </Button>
                        </div>
                    </div>
                </AppCard>
            </div>

            <!-- ─── 第二行：显示模式 + 颜色（多光谱） ────────────── -->
            <div class="grid gap-4 grid-cols-[repeat(auto-fill,minmax(min(100%,20rem),1fr))]">
                <!-- 显示模式（含设为开机图） -->
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="1">
                    <div class="p-4">
                        <div class="mb-3 text-sm font-semibold">{{ t('projector.displayMode') }}</div>
                        <div class="flex flex-wrap gap-2">
                            <Button
                                v-for="opt in displayModeOptions"
                                :key="opt.value"
                                size="small"
                                :severity="device.lastDisplayMode === opt.value ? 'primary' : 'secondary'"
                                :outlined="device.lastDisplayMode !== opt.value"
                                :disabled="!isConnected || busy"
                                @click="onSetDisplayMode(opt.value)"
                            >
                                {{ opt.label }}
                            </Button>
                        </div>
                        <div class="mt-3 border-t pt-3">
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="!isConnected || busy"
                                @click="onSetBootFromDisplayMode"
                            >
                                {{
                                    t('projector.setAsBootImage', {
                                        label:
                                            displayModeOptions.find((o) => o.value === device?.lastDisplayMode)
                                                ?.label ?? t('projector.displayBlack'),
                                    })
                                }}
                            </Button>
                        </div>
                    </div>
                </AppCard>

                <!-- 颜色（多光谱） -->
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="3">
                    <div class="p-4">
                        <div class="mb-3 text-sm font-semibold">{{ t('projector.colorMultiSpectral') }}</div>
                        <!-- 颜色 Tab 按钮 -->
                        <div class="flex flex-wrap gap-2">
                            <!-- Aura RGB Tab -->
                            <Button
                                size="small"
                                :severity="selectedColor === ProjectorColor.AuraSync ? 'help' : 'secondary'"
                                :outlined="selectedColor !== ProjectorColor.AuraSync"
                                :disabled="!isConnected || busy"
                                @click="onSelectAuraRgb"
                            >
                                🌈 Aura RGB
                            </Button>
                            <!-- 单色 Tab：白 红 绿 蓝 -->
                            <Button
                                v-for="opt in colorOptions.filter((c) => c.value !== ProjectorColor.AuraSync)"
                                :key="opt.value"
                                size="small"
                                :severity="selectedColor === opt.value ? 'primary' : 'secondary'"
                                :outlined="selectedColor !== opt.value"
                                :disabled="!isConnected || busy"
                                @click="onSelectMonoColor(opt.value)"
                            >
                                {{ opt.label }}
                            </Button>
                        </div>

                        <!-- Aura RGB：色轮调色盘 -->
                        <div v-if="selectedColor === ProjectorColor.AuraSync" class="mt-4 flex items-center gap-4">
                            <ColorPicker v-model="rgbHex" :disabled="!isConnected || busy" />
                            <div class="flex flex-col gap-1 text-xs text-muted-foreground">
                                <span>R {{ rgbR }} &nbsp;G {{ rgbG }} &nbsp;B {{ rgbB }}</span>
                                <span class="font-mono uppercase">#{{ rgbHex }}</span>
                            </div>
                            <Button
                                size="small"
                                severity="help"
                                class="ml-auto"
                                :disabled="!isConnected || busy"
                                @click="onApplyRgb"
                            >
                                {{ t('projector.apply') }}
                            </Button>
                        </div>

                        <!-- 单色：亮度调节 0-175 -->
                        <div v-else class="mt-4 flex items-center gap-3">
                            <span class="text-sm text-muted-foreground">{{ t('projector.brightness') }}</span>
                            <Slider
                                v-model="colorBrightness"
                                :min="0"
                                :max="175"
                                :step="5"
                                class="flex-1"
                                :disabled="!isConnected || busy"
                            />
                            <span class="w-8 text-right text-sm tabular-nums">{{ colorBrightness }}</span>
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="!isConnected || busy"
                                @click="onApplyMonoBrightness"
                            >
                                {{ t('projector.apply') }}
                            </Button>
                        </div>
                    </div>
                </AppCard>
            </div>

            <!-- ─── 第三行：图像翻转 + 触发模式 + 棋盘格 ────────── -->
            <div class="grid gap-4 grid-cols-[repeat(auto-fill,minmax(min(100%,14rem),1fr))]">
                <!-- 图像翻转 -->
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="1">
                    <div class="p-4">
                        <div class="mb-3 text-sm font-semibold">{{ t('projector.imageFlip') }}</div>
                        <div class="flex flex-col gap-1.5">
                            <Button
                                v-for="opt in flipOptions"
                                :key="opt.value"
                                size="small"
                                :severity="device.flipMode === opt.value ? 'primary' : 'secondary'"
                                :outlined="device.flipMode !== opt.value"
                                :disabled="!isConnected || busy"
                                @click="onSetFlip(opt.value)"
                            >
                                {{ opt.label }}
                            </Button>
                        </div>
                    </div>
                </AppCard>

                <!-- 触发模式 -->
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="2">
                    <div class="p-4">
                        <div class="mb-3 text-sm font-semibold">{{ t('projector.triggerMode') }}</div>
                        <div class="flex flex-col gap-1.5">
                            <Button
                                v-for="opt in triggerModeOptions"
                                :key="opt.value"
                                size="small"
                                :severity="device.triggerMode === opt.value ? 'primary' : 'secondary'"
                                :outlined="device.triggerMode !== opt.value"
                                :disabled="!isConnected || busy"
                                @click="onSetTriggerMode(opt.value)"
                            >
                                {{ opt.label }}
                            </Button>
                        </div>
                    </div>
                </AppCard>

                <!-- 棋盘格像素尺寸 -->
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="3">
                    <div class="p-4">
                        <div class="mb-3 text-sm font-semibold">
                            {{ t('projector.checkerboardSize') }}
                            <span class="font-normal text-muted-foreground">(px)</span>
                        </div>
                        <div class="flex items-center gap-2">
                            <InputNumber
                                v-model="checkerboardPixelSize"
                                :min="1"
                                :max="128"
                                :disabled="!isConnected || busy"
                                show-buttons
                                button-layout="horizontal"
                                size="small"
                                input-class="w-12 text-center"
                            />
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                class="shrink-0 whitespace-nowrap"
                                :disabled="!isConnected || busy"
                                @click="onSetCheckerboard"
                            >
                                {{ t('projector.apply') }}
                            </Button>
                        </div>
                    </div>
                </AppCard>
            </div>

            <!-- ─── 第四行：寄存器读写 ───────────────────────────── -->
            <AppCard :beam-size="80" :beam-duration="8" :beam-delay="4">
                <div class="p-4">
                    <div class="mb-3 text-sm font-semibold">{{ t('projector.registerRW') }}</div>
                    <div class="flex flex-wrap items-center gap-3 text-sm">
                        <label class="flex items-center gap-1.5">
                            <span>{{ t('projector.registerAddress') }}</span>
                            <InputNumber
                                v-model="registerAddr"
                                :min="0"
                                :disabled="busy"
                                size="small"
                                input-class="w-20"
                            />
                        </label>
                        <label class="flex items-center gap-1.5">
                            <span>{{ t('projector.registerValue') }}</span>
                            <InputNumber v-model="registerValue" :disabled="busy" size="small" input-class="w-20" />
                        </label>
                        <Button
                            size="small"
                            severity="secondary"
                            outlined
                            :disabled="!isConnected || busy"
                            @click="onReadRegister"
                        >
                            {{ t('projector.readRegister') }}
                        </Button>
                        <Button
                            size="small"
                            severity="secondary"
                            outlined
                            :disabled="!isConnected || busy"
                            @click="onWriteRegister"
                        >
                            {{ t('projector.writeRegister') }}
                        </Button>
                        <span v-if="registerReadResult !== null" class="font-mono text-xs">
                            {{ t('projector.registerResult') }}: {{ registerReadResult }}
                        </span>
                    </div>
                </div>
            </AppCard>
        </template>
    </div>
</template>
