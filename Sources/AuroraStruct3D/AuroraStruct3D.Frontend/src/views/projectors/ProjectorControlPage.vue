<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
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

const route = useRoute()
const router = useRouter()
const store = useProjectorStore()

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
        if (ok === false) toast.error('操作失败，请检查连接')
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
const displayModeOptions = [
    { value: ProjectorDisplayMode.Black, label: '黑屏' },
    { value: ProjectorDisplayMode.White, label: '白屏' },
    { value: ProjectorDisplayMode.Cross, label: '十字' },
    { value: ProjectorDisplayMode.Checkerboard, label: '棋盘格' },
    { value: ProjectorDisplayMode.Internal1, label: '内置图案1' },
    { value: ProjectorDisplayMode.Internal2, label: '内置图案2' },
]
async function onSetDisplayMode(mode: ProjectorDisplayMode) {
    await run(() => store.setDisplayMode({ projectorDeviceId: deviceId.value, mode }))
}
// DisplayMode 与 BootImage 枚举值一一对应，直接转换
async function onSetBootFromDisplayMode() {
    const bootImage = device.value!.lastDisplayMode as unknown as ProjectorBootImage
    await run(() => store.setBootImage({ projectorDeviceId: deviceId.value, bootImage }))
    toast.success('已设为开机默认图案')
}

// ─── 颜色（多光谱） ────────────────────────────────────────────────────────
const colorOptions = [
    { value: ProjectorColor.AuraSync, label: 'Aura RGB' },
    { value: ProjectorColor.White, label: '白' },
    { value: ProjectorColor.Red, label: '红' },
    { value: ProjectorColor.Green, label: '绿' },
    { value: ProjectorColor.Blue, label: '蓝' },
]
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
const flipOptions = [
    { value: ProjectorFlipMode.None, label: '不翻转' },
    { value: ProjectorFlipMode.FlipX, label: '水平翻转' },
    { value: ProjectorFlipMode.FlipY, label: '垂直翻转' },
    { value: ProjectorFlipMode.FlipXY, label: '双向翻转' },
]
async function onSetFlip(flipMode: ProjectorFlipMode) {
    await run(() => store.setFlip({ projectorDeviceId: deviceId.value, flipMode }))
}

// ─── 触发模式 ─────────────────────────────────────────────────────────────
const triggerModeOptions = [
    { value: ProjectorTriggerMode.Normal, label: '普通' },
    { value: ProjectorTriggerMode.Loop, label: '循环' },
    { value: ProjectorTriggerMode.SingleFrame, label: '单帧' },
]
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
    if (!confirm('确定软复位投影机？')) return
    await run(() => store.softReset(deviceId.value))
    toast.success('软复位指令已发送')
}
async function onSaveParams() {
    await run(() => store.saveParams(deviceId.value))
    toast.success('参数已保存到投影机 NVM')
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
            <button class="rounded border px-3 py-1.5 text-sm hover:bg-muted/50" @click="router.back()">← 返回</button>
            <div>
                <h1 class="text-lg font-semibold">{{ device?.name ?? '投影机控制' }}</h1>
                <p class="text-xs text-muted-foreground">
                    {{ device?.connectionStatusText }}
                    <span v-if="device?.firmwareVersion">· 固件 {{ device.firmwareVersion }}</span>
                </p>
            </div>
            <span
                :class="[
                    'ml-auto rounded-full px-2 py-0.5 text-xs font-medium',
                    isConnected ? 'bg-green-100 text-green-700' : 'bg-muted text-muted-foreground',
                ]"
            >
                {{ isConnected ? '已连接' : '未连接' }}
            </span>
        </div>

        <div v-if="!device" class="py-12 text-center text-sm text-muted-foreground">
            设备未找到，请返回管理页重新选择
        </div>

        <template v-else>
            <!-- ─── 第一行：LED 控制 + 触发 / 高级 ───────────────── -->
            <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
                <!-- LED 控制 -->
                <section class="rounded-lg border p-4">
                    <h2 class="mb-3 text-sm font-semibold text-muted-foreground">LED 控制</h2>
                    <div class="flex gap-3">
                        <button
                            :disabled="!isConnected || busy"
                            class="rounded bg-yellow-500 px-5 py-2 text-sm font-medium text-white hover:bg-yellow-400 disabled:opacity-40"
                            @click="onLedOn"
                        >
                            开灯
                        </button>
                        <button
                            :disabled="!isConnected || busy"
                            class="rounded border px-5 py-2 text-sm hover:bg-muted/50 disabled:opacity-40"
                            @click="onLedOff"
                        >
                            关灯
                        </button>
                    </div>
                </section>

                <!-- 触发 / 高级 -->
                <section class="rounded-lg border p-4">
                    <h2 class="mb-3 text-sm font-semibold text-muted-foreground">触发 / 高级</h2>
                    <div class="flex flex-wrap gap-2">
                        <button
                            :disabled="!isConnected || busy"
                            class="rounded bg-primary px-4 py-2 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-40"
                            @click="onTriggerOnce"
                        >
                            触发一次
                        </button>
                        <button
                            :disabled="!isConnected || busy"
                            class="rounded border px-4 py-2 text-sm hover:bg-muted/50 disabled:opacity-40"
                            @click="onSaveParams"
                        >
                            保存参数
                        </button>
                        <button
                            :disabled="!isConnected || busy"
                            class="rounded border border-destructive px-4 py-2 text-sm text-destructive hover:bg-destructive/10 disabled:opacity-40"
                            @click="onSoftReset"
                        >
                            软复位
                        </button>
                    </div>
                </section>
            </div>

            <!-- ─── 第二行：显示模式 + 颜色（多光谱） ────────────── -->
            <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
                <!-- 显示模式（含设为开机图） -->
                <section class="rounded-lg border p-4">
                    <h2 class="mb-3 text-sm font-semibold text-muted-foreground">显示模式</h2>
                    <div class="flex flex-wrap gap-2">
                        <button
                            v-for="opt in displayModeOptions"
                            :key="opt.value"
                            :disabled="!isConnected || busy"
                            :class="[
                                'rounded border px-3 py-1.5 text-sm hover:bg-muted/50 disabled:opacity-40',
                                device.lastDisplayMode === opt.value
                                    ? 'border-primary bg-primary/5 font-medium text-primary'
                                    : '',
                            ]"
                            @click="onSetDisplayMode(opt.value)"
                        >
                            {{ opt.label }}
                        </button>
                    </div>
                    <div class="mt-3 border-t pt-3">
                        <button
                            :disabled="!isConnected || busy"
                            class="rounded border border-dashed px-3 py-1.5 text-sm text-muted-foreground hover:border-primary hover:text-primary disabled:opacity-40"
                            @click="onSetBootFromDisplayMode"
                        >
                            将「{{
                                displayModeOptions.find((o) => o.value === device.lastDisplayMode)?.label ?? '当前'
                            }}」设为开机默认图
                        </button>
                    </div>
                </section>

                <!-- 颜色（多光谱） -->
                <section class="rounded-lg border p-4">
                    <h2 class="mb-3 text-sm font-semibold text-muted-foreground">颜色（多光谱）</h2>

                    <!-- 颜色 Tab 按钮 -->
                    <div class="flex flex-wrap gap-2">
                        <!-- Aura RGB Tab -->
                        <button
                            :disabled="!isConnected || busy"
                            :class="[
                                'rounded border px-3 py-1.5 text-sm disabled:opacity-40',
                                selectedColor === ProjectorColor.AuraSync
                                    ? 'border-purple-500 bg-purple-50 font-medium text-purple-600'
                                    : 'hover:bg-muted/50',
                            ]"
                            @click="onSelectAuraRgb"
                        >
                            🌈 Aura RGB
                        </button>
                        <!-- 单色 Tab：白 红 绿 蓝 -->
                        <button
                            v-for="opt in colorOptions.filter((c) => c.value !== ProjectorColor.AuraSync)"
                            :key="opt.value"
                            :disabled="!isConnected || busy"
                            :class="[
                                'rounded border px-3 py-1.5 text-sm disabled:opacity-40',
                                selectedColor === opt.value
                                    ? 'border-primary bg-primary/5 font-medium text-primary'
                                    : 'hover:bg-muted/50',
                            ]"
                            @click="onSelectMonoColor(opt.value)"
                        >
                            {{ opt.label }}
                        </button>
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
                        <button
                            :disabled="!isConnected || busy"
                            class="ml-auto rounded bg-purple-500 px-4 py-1.5 text-sm text-white hover:bg-purple-400 disabled:opacity-40"
                            @click="onApplyRgb"
                        >
                            应用
                        </button>
                    </div>

                    <!-- 单色：亮度调节 0-175 -->
                    <div v-else class="mt-4 flex items-center gap-3">
                        <span class="text-sm text-muted-foreground">亮度</span>
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
                        <button
                            :disabled="!isConnected || busy"
                            class="rounded border px-3 py-1 text-sm hover:bg-muted/50 disabled:opacity-40"
                            @click="onApplyMonoBrightness"
                        >
                            应用
                        </button>
                    </div>
                </section>
            </div>

            <!-- ─── 第三行：图像翻转 + 触发模式 + 棋盘格 ────────── -->
            <div class="grid grid-cols-1 gap-4 md:grid-cols-3">
                <!-- 图像翻转 -->
                <section class="rounded-lg border p-4">
                    <h2 class="mb-3 text-sm font-semibold text-muted-foreground">图像翻转</h2>
                    <div class="flex flex-col gap-1.5">
                        <button
                            v-for="opt in flipOptions"
                            :key="opt.value"
                            :disabled="!isConnected || busy"
                            :class="[
                                'rounded border px-3 py-1.5 text-sm hover:bg-muted/50 disabled:opacity-40',
                                device.flipMode === opt.value
                                    ? 'border-primary bg-primary/5 font-medium text-primary'
                                    : '',
                            ]"
                            @click="onSetFlip(opt.value)"
                        >
                            {{ opt.label }}
                        </button>
                    </div>
                </section>

                <!-- 触发模式 -->
                <section class="rounded-lg border p-4">
                    <h2 class="mb-3 text-sm font-semibold text-muted-foreground">触发模式</h2>
                    <div class="flex flex-col gap-1.5">
                        <button
                            v-for="opt in triggerModeOptions"
                            :key="opt.value"
                            :disabled="!isConnected || busy"
                            :class="[
                                'rounded border px-3 py-1.5 text-sm hover:bg-muted/50 disabled:opacity-40',
                                device.triggerMode === opt.value
                                    ? 'border-primary bg-primary/5 font-medium text-primary'
                                    : '',
                            ]"
                            @click="onSetTriggerMode(opt.value)"
                        >
                            {{ opt.label }}
                        </button>
                    </div>
                </section>

                <!-- 棋盘格像素尺寸 -->
                <section class="rounded-lg border p-4">
                    <h2 class="mb-3 text-sm font-semibold text-muted-foreground">棋盘格像素尺寸</h2>
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
                        <button
                            :disabled="!isConnected || busy"
                            class="rounded border px-3 py-1 text-sm hover:bg-muted/50 disabled:opacity-40"
                            @click="onSetCheckerboard"
                        >
                            应用
                        </button>
                    </div>
                </section>
            </div>

            <!-- ─── 第四行：寄存器读写 ───────────────────────────── -->
            <section class="rounded-lg border p-4">
                <h2 class="mb-3 text-sm font-semibold text-muted-foreground">寄存器读写</h2>
                <div class="flex flex-wrap items-center gap-3 text-sm">
                    <label class="flex items-center gap-1.5">
                        地址
                        <input
                            v-model.number="registerAddr"
                            type="number"
                            min="0"
                            :disabled="busy"
                            class="w-20 rounded border bg-background px-2 py-1.5 text-sm disabled:opacity-40"
                        />
                    </label>
                    <label class="flex items-center gap-1.5">
                        值
                        <input
                            v-model.number="registerValue"
                            type="number"
                            :disabled="busy"
                            class="w-20 rounded border bg-background px-2 py-1.5 text-sm disabled:opacity-40"
                        />
                    </label>
                    <button
                        :disabled="!isConnected || busy"
                        class="rounded border px-3 py-1.5 hover:bg-muted/50 disabled:opacity-40"
                        @click="onReadRegister"
                    >
                        读取
                    </button>
                    <button
                        :disabled="!isConnected || busy"
                        class="rounded border px-3 py-1.5 hover:bg-muted/50 disabled:opacity-40"
                        @click="onWriteRegister"
                    >
                        写入
                    </button>
                    <span v-if="registerReadResult !== null" class="font-mono text-xs">
                        结果：{{ registerReadResult }}
                    </span>
                </div>
            </section>
        </template>
    </div>
</template>
