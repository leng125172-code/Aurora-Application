<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useProjectorStore } from '@/stores/projectors'
import {
    ProjectorConnectionStatus,
    ProjectorDisplayMode,
    ProjectorColor,
    ProjectorFlipMode,
    ProjectorTriggerMode,
    ProjectorBootImage,
} from '@/api/projectors'
import { toast } from 'vue-sonner'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { BorderBeam } from '@/components/ui/border-beam'
import { Button } from '@/components/ui/button'

const route = useRoute()
const router = useRouter()
const store = useProjectorStore()
const { t } = useI18n()

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
    await run(() => store.ledOn(deviceId.value))
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
// 单色亮度（10~175）
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

// 与 <input type="color"> 双向绑定
const rgbHex = computed({
    get() {
        const h = (v: number) => v.toString(16).padStart(2, '0')
        return `#${h(rgbR.value)}${h(rgbG.value)}${h(rgbB.value)}`
    },
    set(hex: string) {
        if (hex.length !== 7) return
        rgbR.value = parseInt(hex.slice(1, 3), 16)
        rgbG.value = parseInt(hex.slice(3, 5), 16)
        rgbB.value = parseInt(hex.slice(5, 7), 16)
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

// ─── 高级操作 ─────────────────────────────────────────────────────────────
async function onSoftReset() {
    if (!confirm(t('projector.confirmSoftReset'))) return
    await run(() => store.softReset(deviceId.value))
    toast.success(t('projector.softResetSent'))
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
        colorBrightness.value = device.value.lastLightValue || 100
        checkerboardPixelSize.value = device.value.checkerboardPixelSize
        rgbR.value = device.value.ledRgbR
        rgbG.value = device.value.ledRgbG
        rgbB.value = device.value.ledRgbB
    }
})

watch(device, (d) => {
    if (!d) return
    selectedColor.value = d.lastColor
    colorBrightness.value = d.lastLightValue || 100
    checkerboardPixelSize.value = d.checkerboardPixelSize
    rgbR.value = d.ledRgbR
    rgbG.value = d.ledRgbG
    rgbB.value = d.ledRgbB
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <!-- ─── 顶部：返回 + 设备信息 ────────────────────────────── -->
        <div class="flex items-center gap-3">
            <Button variant="outline" size="sm" @click="router.back()">{{ t('camera.back') }}</Button>
            <div>
                <h1 class="text-2xl font-bold tracking-tight">{{ device?.name ?? t('projector.ctrlTitle') }}</h1>
                <p class="text-xs text-muted-foreground">
                    {{ device?.connectionStatusText }}
                    <span v-if="device?.firmwareVersion">
                        · {{ t('projector.ctrlFirmware') }} {{ device.firmwareVersion }}
                    </span>
                </p>
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
            <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
                <!-- LED 控制 -->
                <Card class="relative">
                    <BorderBeam :size="80" :duration="8" />
                    <CardHeader class="pb-3">
                        <CardTitle class="text-sm font-semibold">{{ t('projector.ledControl') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="flex gap-3">
                            <Button
                                size="sm"
                                class="bg-yellow-500 text-white hover:bg-yellow-400"
                                :disabled="!isConnected || busy"
                                @click="onLedOn"
                            >
                                {{ t('projector.ledOn') }}
                            </Button>
                            <Button variant="outline" size="sm" :disabled="!isConnected || busy" @click="onLedOff">
                                {{ t('projector.ledOff') }}
                            </Button>
                        </div>
                    </CardContent>
                </Card>

                <!-- 触发 / 高级 -->
                <Card class="relative">
                    <BorderBeam :size="80" :duration="8" :delay="2" />
                    <CardHeader class="pb-3">
                        <CardTitle class="text-sm font-semibold">{{ t('projector.triggerAdvanced') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="flex flex-wrap gap-2">
                            <Button size="sm" :disabled="!isConnected || busy" @click="onTriggerOnce">
                                {{ t('projector.triggerOnce') }}
                            </Button>
                            <Button variant="outline" size="sm" :disabled="!isConnected || busy" @click="onSaveParams">
                                {{ t('projector.saveParams') }}
                            </Button>
                            <Button
                                variant="destructive"
                                size="sm"
                                :disabled="!isConnected || busy"
                                @click="onSoftReset"
                            >
                                {{ t('projector.softReset') }}
                            </Button>
                        </div>
                    </CardContent>
                </Card>
            </div>

            <!-- ─── 第二行：显示模式 + 颜色（多光谱） ────────────── -->
            <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
                <!-- 显示模式（含设为开机图） -->
                <Card class="relative">
                    <BorderBeam :size="80" :duration="8" :delay="1" />
                    <CardHeader class="pb-3">
                        <CardTitle class="text-sm font-semibold">{{ t('projector.displayMode') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="flex flex-wrap gap-2">
                            <Button
                                v-for="opt in displayModeOptions"
                                :key="opt.value"
                                variant="outline"
                                size="sm"
                                :disabled="!isConnected || busy"
                                :class="
                                    device.lastDisplayMode === opt.value
                                        ? 'border-primary bg-primary/5 text-primary'
                                        : ''
                                "
                                @click="onSetDisplayMode(opt.value)"
                            >
                                {{ opt.label }}
                            </Button>
                        </div>
                        <div class="mt-3 border-t pt-3">
                            <Button
                                variant="outline"
                                size="sm"
                                :disabled="!isConnected || busy"
                                class="border-dashed text-muted-foreground hover:border-primary hover:text-primary"
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
                    </CardContent>
                </Card>

                <!-- 颜色（多光谱） -->
                <Card class="relative">
                    <BorderBeam :size="80" :duration="8" :delay="3" />
                    <CardHeader class="pb-3">
                        <CardTitle class="text-sm font-semibold">{{ t('projector.colorMultiSpectral') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <!-- 颜色 Tab 按钮 -->
                        <div class="flex flex-wrap gap-2">
                            <!-- Aura RGB Tab -->
                            <Button
                                variant="outline"
                                size="sm"
                                :disabled="!isConnected || busy"
                                :class="
                                    selectedColor === ProjectorColor.AuraSync
                                        ? 'border-purple-500 bg-purple-50 text-purple-600'
                                        : ''
                                "
                                @click="onSelectAuraRgb"
                            >
                                🌈 Aura RGB
                            </Button>
                            <!-- 单色 Tab：白 红 绿 蓝 -->
                            <Button
                                v-for="opt in colorOptions.filter((c) => c.value !== ProjectorColor.AuraSync)"
                                :key="opt.value"
                                variant="outline"
                                size="sm"
                                :disabled="!isConnected || busy"
                                :class="selectedColor === opt.value ? 'border-primary bg-primary/5 text-primary' : ''"
                                @click="onSelectMonoColor(opt.value)"
                            >
                                {{ opt.label }}
                            </Button>
                        </div>

                        <!-- Aura RGB：圆形色轮调色盘 -->
                        <div v-if="selectedColor === ProjectorColor.AuraSync" class="mt-4 flex items-center gap-4">
                            <input
                                v-model="rgbHex"
                                type="color"
                                :disabled="!isConnected || busy"
                                class="h-16 w-16 cursor-pointer rounded-full border-0 bg-transparent p-0.5 disabled:cursor-not-allowed disabled:opacity-40"
                                style="border-radius: 50%"
                            />
                            <div class="flex flex-col gap-1 text-xs text-muted-foreground">
                                <span>R {{ rgbR }} &nbsp;G {{ rgbG }} &nbsp;B {{ rgbB }}</span>
                                <span class="font-mono uppercase">{{ rgbHex }}</span>
                            </div>
                            <Button
                                size="sm"
                                class="ml-auto bg-purple-500 text-white hover:bg-purple-400"
                                :disabled="!isConnected || busy"
                                @click="onApplyRgb"
                            >
                                {{ t('projector.apply') }}
                            </Button>
                        </div>

                        <!-- 单色：亮度调节 0-175 -->
                        <div v-else class="mt-4 flex items-center gap-3">
                            <span class="text-sm text-muted-foreground">{{ t('projector.brightness') }}</span>
                            <input
                                v-model.number="colorBrightness"
                                type="range"
                                min="10"
                                max="175"
                                step="5"
                                class="flex-1"
                                :disabled="!isConnected || busy"
                            />
                            <span class="w-8 text-right text-sm tabular-nums">{{ colorBrightness }}</span>
                            <Button
                                variant="outline"
                                size="sm"
                                :disabled="!isConnected || busy"
                                @click="onApplyMonoBrightness"
                            >
                                {{ t('projector.apply') }}
                            </Button>
                        </div>
                    </CardContent>
                </Card>
            </div>

            <!-- ─── 第三行：图像翻转 + 触发模式 + 棋盘格 ────────── -->
            <div class="grid grid-cols-1 gap-4 md:grid-cols-3">
                <!-- 图像翻转 -->
                <Card class="relative">
                    <BorderBeam :size="80" :duration="8" :delay="1" />
                    <CardHeader class="pb-3">
                        <CardTitle class="text-sm font-semibold">{{ t('projector.imageFlip') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="flex flex-col gap-1.5">
                            <Button
                                v-for="opt in flipOptions"
                                :key="opt.value"
                                variant="outline"
                                size="sm"
                                :disabled="!isConnected || busy"
                                :class="device.flipMode === opt.value ? 'border-primary bg-primary/5 text-primary' : ''"
                                @click="onSetFlip(opt.value)"
                            >
                                {{ opt.label }}
                            </Button>
                        </div>
                    </CardContent>
                </Card>

                <!-- 触发模式 -->
                <Card class="relative">
                    <BorderBeam :size="80" :duration="8" :delay="2" />
                    <CardHeader class="pb-3">
                        <CardTitle class="text-sm font-semibold">{{ t('projector.triggerMode') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="flex flex-col gap-1.5">
                            <Button
                                v-for="opt in triggerModeOptions"
                                :key="opt.value"
                                variant="outline"
                                size="sm"
                                :disabled="!isConnected || busy"
                                :class="
                                    device.triggerMode === opt.value ? 'border-primary bg-primary/5 text-primary' : ''
                                "
                                @click="onSetTriggerMode(opt.value)"
                            >
                                {{ opt.label }}
                            </Button>
                        </div>
                    </CardContent>
                </Card>

                <!-- 棋盘格像素尺寸 -->
                <Card class="relative">
                    <BorderBeam :size="80" :duration="8" :delay="3" />
                    <CardHeader class="pb-3">
                        <CardTitle class="text-sm font-semibold">{{ t('projector.checkerboardSize') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="flex items-center gap-3">
                            <input
                                v-model.number="checkerboardPixelSize"
                                type="number"
                                min="1"
                                max="128"
                                :disabled="!isConnected || busy"
                                class="w-20 rounded border bg-background px-2 py-1.5 text-sm disabled:opacity-40"
                            />
                            <span class="text-xs text-muted-foreground">px</span>
                            <Button
                                variant="outline"
                                size="sm"
                                :disabled="!isConnected || busy"
                                @click="onSetCheckerboard"
                            >
                                {{ t('projector.apply') }}
                            </Button>
                        </div>
                    </CardContent>
                </Card>
            </div>

            <!-- ─── 第四行：寄存器读写 ───────────────────────────── -->
            <Card class="relative">
                <BorderBeam :size="80" :duration="8" :delay="4" />
                <CardHeader class="pb-3">
                    <CardTitle class="text-sm font-semibold">{{ t('projector.registerRW') }}</CardTitle>
                </CardHeader>
                <CardContent>
                    <div class="flex flex-wrap items-center gap-3 text-sm">
                        <label class="flex items-center gap-1.5">
                            {{ t('projector.registerAddress') }}
                            <input
                                v-model.number="registerAddr"
                                type="number"
                                min="0"
                                :disabled="busy"
                                class="w-20 rounded border bg-background px-2 py-1.5 text-sm disabled:opacity-40"
                            />
                        </label>
                        <label class="flex items-center gap-1.5">
                            {{ t('projector.registerValue') }}
                            <input
                                v-model.number="registerValue"
                                type="number"
                                :disabled="busy"
                                class="w-20 rounded border bg-background px-2 py-1.5 text-sm disabled:opacity-40"
                            />
                        </label>
                        <Button variant="outline" size="sm" :disabled="!isConnected || busy" @click="onReadRegister">
                            {{ t('projector.readRegister') }}
                        </Button>
                        <Button variant="outline" size="sm" :disabled="!isConnected || busy" @click="onWriteRegister">
                            {{ t('projector.writeRegister') }}
                        </Button>
                        <span v-if="registerReadResult !== null" class="font-mono text-xs">
                            {{ t('projector.registerResult') }}: {{ registerReadResult }}
                        </span>
                    </div>
                </CardContent>
            </Card>
        </template>
    </div>
</template>
