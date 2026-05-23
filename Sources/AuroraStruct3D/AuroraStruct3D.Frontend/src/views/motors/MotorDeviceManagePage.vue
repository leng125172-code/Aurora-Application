<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import {
    ArrowDownToLine,
    ArrowUpToLine,
    CircleStop,
    Home,
    Pencil,
    Play,
    RefreshCw,
    RotateCcw,
    ScanLine,
    ShieldAlert,
    Zap,
    ZapOff,
} from 'lucide-vue-next'
import { toast } from 'vue-sonner'

import {
    MotorBrand,
    MotorDeviceStatus,
    MotorRotationAngleLimitKind,
    type MotorAxisDto,
    type UpdateMotorAxisDto,
} from '@/api/motors'
import { SerialPortConsts } from '@/constants/serialPortConsts'
import { useMotorStore } from '@/stores/motors'

const store = useMotorStore()
const selectedId = ref('')
const scanning = ref(false)
const acting = ref(false)
const scanResultText = ref('')

const baudRateOptions = SerialPortConsts.supportedBaudRates
const commonBaudRates = [115200, 38400, 9600, 19200, 57600]
const scanForm = ref({
    startSlaveId: 1,
    endSlaveId: 32,
    baudRates: [...commonBaudRates],
    probeTimeoutMs: 150,
})
const moveForm = ref({
    position: 1000,
    speedRpm: 100,
})
const rotationSaving = ref(false)
const rotationRangeForm = ref({
    minRotationAngleText: '',
    maxRotationAngleText: '',
})
const registerForm = ref({
    startAddressText: '0x1003',
    quantity: 1,
    writeAddressText: '0x1801',
    writeValueText: '0x0000',
})
const registerReadResult = ref('')

const showEditDialog = ref(false)
const editing = ref(false)
const editingId = ref('')
const editForm = ref<UpdateMotorAxisDto>({
    name: '',
    description: '',
    isEnabled: true,
    model: '',
})

const selectedMotor = computed(() => store.motors.find((motor) => motor.id === selectedId.value) ?? null)
const isKtechSelected = computed(() => selectedMotor.value?.brand === MotorBrand.KtechKtech)
const currentSingleTurnAngle = computed(() => {
    const motor = selectedMotor.value
    if (!motor || motor.brand !== MotorBrand.KtechKtech) {
        return null
    }
    return normalizeKtechSingleTurnAngle(motor.lastKnownPosition)
})

watch(
    () => store.motors,
    (motors) => {
        if (motors.length === 0) {
            selectedId.value = ''
            return
        }
        if (!selectedId.value || !motors.some((motor) => motor.id === selectedId.value)) {
            selectedId.value = motors[0].id
        }
    },
    { deep: true }
)

watch(
    selectedMotor,
    (motor) => {
        rotationRangeForm.value = {
            minRotationAngleText: formatOptionalAngle(motor?.minRotationAngle),
            maxRotationAngleText: formatOptionalAngle(motor?.maxRotationAngle),
        }
    },
    { immediate: true }
)

async function refreshList() {
    await store.fetchList()
}

async function handleScan() {
    scanning.value = true
    scanResultText.value = ''
    try {
        const result = await store.scan({
            startSlaveId: scanForm.value.startSlaveId,
            endSlaveId: scanForm.value.endSlaveId,
            baudRates: [...scanForm.value.baudRates],
            probeTimeoutMs: scanForm.value.probeTimeoutMs,
        })
        scanResultText.value = `尝试 ${result.triedCount} 次，发现 ${result.foundCount} 台，用时 ${Math.round(result.elapsedMs / 1000)} 秒`
        toast.success(scanResultText.value)
    } catch {
        // httpClient 已统一弹出错误
    } finally {
        scanning.value = false
    }
}

function setAllBaudRates(checked: boolean) {
    scanForm.value.baudRates = checked ? [...baudRateOptions] : [...commonBaudRates]
}

function toggleBaudRate(rate: number, checked: boolean) {
    const current = new Set(scanForm.value.baudRates)
    if (checked) {
        current.add(rate)
    } else {
        current.delete(rate)
    }
    scanForm.value.baudRates = [...current].sort((left, right) => left - right)
}

async function runAction(action: () => Promise<MotorAxisDto>, message: string) {
    acting.value = true
    try {
        await action()
        toast.success(message)
    } catch {
        // 忽略
    } finally {
        acting.value = false
    }
}

async function runSelectedAction(action: (id: string) => Promise<MotorAxisDto>, message: string) {
    const motor = selectedMotor.value
    if (!motor) return
    await runAction(() => action(motor.id), message)
}

function openEdit(motor: MotorAxisDto) {
    editingId.value = motor.id
    editForm.value = {
        name: motor.name,
        description: motor.description ?? '',
        isEnabled: motor.isEnabled,
        model: motor.model ?? '',
    }
    showEditDialog.value = true
}

async function handleEdit() {
    editing.value = true
    try {
        await store.update(editingId.value, editForm.value)
        toast.success('电机信息已更新')
        showEditDialog.value = false
    } catch {
        // 忽略
    } finally {
        editing.value = false
    }
}

async function handleMoveAbsolute() {
    const motor = selectedMotor.value
    if (!motor) return
    await runAction(() => store.moveAbsolute(motor.id, moveForm.value), '绝对运动命令已发送')
}

async function handleMoveRelative() {
    const motor = selectedMotor.value
    if (!motor) return
    await runAction(() => store.moveRelative(motor.id, moveForm.value), '相对运动命令已发送')
}

async function handleSaveRotationRange() {
    const motor = selectedMotor.value
    if (!motor) return

    let minRotationAngle: number | null
    let maxRotationAngle: number | null
    try {
        minRotationAngle = parseOptionalAngle(rotationRangeForm.value.minRotationAngleText, '最小角度')
        maxRotationAngle = parseOptionalAngle(rotationRangeForm.value.maxRotationAngleText, '最大角度')
        if (minRotationAngle !== null && maxRotationAngle !== null && minRotationAngle > maxRotationAngle) {
            toast.error('最小角度不能大于最大角度')
            return
        }
    } catch (e: unknown) {
        toast.error(e instanceof Error ? e.message : String(e))
        return
    }

    rotationSaving.value = true
    try {
        await store.setRotationAngleRange(motor.id, {
            minRotationAngle,
            maxRotationAngle,
        })
        toast.success('旋转角度范围已保存')
    } catch {
        // 忽略
    } finally {
        rotationSaving.value = false
    }
}

async function handleSetRotationLimitFromCurrent(limitKind: MotorRotationAngleLimitKind) {
    const motor = selectedMotor.value
    if (!motor) return

    rotationSaving.value = true
    try {
        await store.setRotationAngleLimitFromCurrent(motor.id, { limitKind })
        toast.success(
            limitKind === MotorRotationAngleLimitKind.Minimum ? '当前角度已设为最小值' : '当前角度已设为最大值'
        )
    } catch {
        // 忽略
    } finally {
        rotationSaving.value = false
    }
}

async function handleReadRegisters() {
    const motor = selectedMotor.value
    if (!motor) return
    acting.value = true
    try {
        const result = await store.readRegisters(motor.id, {
            startAddress: parseNumber(registerForm.value.startAddressText),
            quantity: registerForm.value.quantity,
        })
        registerReadResult.value = `${result.responseHex}  =>  ${result.values.join(', ')}`
    } catch {
        // 忽略
    } finally {
        acting.value = false
    }
}

async function handleWriteRegister() {
    const motor = selectedMotor.value
    if (!motor) return
    await runAction(
        () =>
            store
                .writeRegister(motor.id, {
                    address: parseNumber(registerForm.value.writeAddressText),
                    value: parseNumber(registerForm.value.writeValueText),
                })
                .then(async () => await store.refreshStatus(motor.id)),
        '寄存器写入完成'
    )
}

function parseNumber(value: string): number {
    const text = value.trim()
    if (text.startsWith('0x') || text.startsWith('0X')) {
        return Number.parseInt(text.substring(2), 16)
    }
    return Number.parseInt(text, 10)
}

function parseOptionalAngle(value: string, label: string): number | null {
    const text = value.trim()
    if (!text) {
        return null
    }
    if (!/^\d+$/.test(text)) {
        throw new Error(`${label}必须是整数`)
    }
    const parsed = Number.parseInt(text, 10)
    if (parsed < 0 || parsed > 36000) {
        throw new Error(`${label}必须在 0~36000 之间`)
    }
    return parsed
}

function formatOptionalAngle(value: number | null | undefined): string {
    return value === null || value === undefined ? '' : value.toString()
}

function normalizeKtechSingleTurnAngle(position: number): number {
    const angle = position % 36000
    return angle < 0 ? angle + 36000 : angle
}

function statusClass(status: MotorDeviceStatus): string {
    switch (status) {
        case MotorDeviceStatus.Online:
            return 'text-blue-600'
        case MotorDeviceStatus.Enabled:
            return 'text-green-600'
        case MotorDeviceStatus.Moving:
            return 'text-indigo-600'
        case MotorDeviceStatus.Faulted:
            return 'text-red-600'
        case MotorDeviceStatus.Offline:
            return 'text-muted-foreground'
        default:
            return 'text-muted-foreground'
    }
}

onMounted(() => {
    void refreshList()
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
            <h1 class="text-lg font-semibold">485 电机设备管理</h1>
            <div class="flex flex-wrap gap-2">
                <button
                    class="inline-flex items-center gap-1.5 rounded border px-3 py-1.5 text-sm hover:bg-muted/50"
                    @click="refreshList"
                >
                    <RefreshCw class="size-4" />
                    刷新
                </button>
                <button
                    class="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-60"
                    :disabled="scanning || scanForm.baudRates.length === 0"
                    @click="handleScan"
                >
                    <ScanLine class="size-4" />
                    {{ scanning ? '扫描中…' : '扫描设备' }}
                </button>
            </div>
        </div>

        <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_430px]">
            <main class="min-w-0 space-y-4">
                <section class="rounded-lg border p-4">
                    <div class="grid gap-3 md:grid-cols-[120px_120px_120px_1fr]">
                        <label class="flex flex-col gap-1 text-sm">
                            起始 ID
                            <input
                                v-model.number="scanForm.startSlaveId"
                                class="rounded border bg-background px-2 py-1.5 text-sm"
                                max="32"
                                min="1"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-sm">
                            结束 ID
                            <input
                                v-model.number="scanForm.endSlaveId"
                                class="rounded border bg-background px-2 py-1.5 text-sm"
                                max="32"
                                min="1"
                                type="number"
                            />
                        </label>
                        <label class="flex flex-col gap-1 text-sm">
                            超时 ms
                            <input
                                v-model.number="scanForm.probeTimeoutMs"
                                class="rounded border bg-background px-2 py-1.5 text-sm"
                                min="50"
                                type="number"
                            />
                        </label>
                        <div class="flex items-end gap-2">
                            <button
                                class="rounded border px-3 py-1.5 text-sm hover:bg-muted/50"
                                type="button"
                                @click="setAllBaudRates(false)"
                            >
                                常用
                            </button>
                            <button
                                class="rounded border px-3 py-1.5 text-sm hover:bg-muted/50"
                                type="button"
                                @click="setAllBaudRates(true)"
                            >
                                全选
                            </button>
                        </div>
                    </div>
                    <div class="mt-3 flex flex-wrap gap-2">
                        <label
                            v-for="rate in baudRateOptions"
                            :key="rate"
                            class="inline-flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-muted/50"
                        >
                            <input
                                :checked="scanForm.baudRates.includes(rate)"
                                type="checkbox"
                                @change="toggleBaudRate(rate, ($event.target as HTMLInputElement).checked)"
                            />
                            {{ rate }}
                        </label>
                    </div>
                    <div v-if="scanning || scanResultText" class="mt-3 text-xs text-muted-foreground">
                        {{
                            scanning
                                ? '正在按串口、波特率和 ID 探测设备，耗时取决于串口数量与超时设置。'
                                : scanResultText
                        }}
                    </div>
                </section>

                <section>
                    <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">加载中…</div>
                    <div v-else-if="store.motors.length === 0" class="py-8 text-center text-sm text-muted-foreground">
                        暂无电机设备，请点击「扫描设备」自动发现
                    </div>
                    <div v-else class="overflow-auto rounded-lg border">
                        <table class="w-full min-w-[960px] text-sm">
                            <thead class="border-b bg-muted/50">
                                <tr>
                                    <th class="px-3 py-2 text-left font-medium">轴</th>
                                    <th class="px-3 py-2 text-left font-medium">名称</th>
                                    <th class="px-3 py-2 text-left font-medium">品牌</th>
                                    <th class="px-3 py-2 text-left font-medium">串口</th>
                                    <th class="px-3 py-2 text-left font-medium">从站</th>
                                    <th class="px-3 py-2 text-left font-medium">状态</th>
                                    <th class="px-3 py-2 text-left font-medium">位置/速度</th>
                                    <th class="px-3 py-2 text-left font-medium">操作</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr
                                    v-for="motor in store.motors"
                                    :key="motor.id"
                                    :class="[
                                        'cursor-pointer border-b last:border-0 hover:bg-muted/30',
                                        selectedId === motor.id ? 'bg-muted/40' : '',
                                    ]"
                                    @click="selectedId = motor.id"
                                >
                                    <td class="px-3 py-2">{{ motor.axisIndex }}</td>
                                    <td class="px-3 py-2">
                                        <div class="font-medium">{{ motor.name }}</div>
                                        <div class="max-w-[180px] truncate text-xs text-muted-foreground">
                                            {{ motor.description || '—' }}
                                        </div>
                                    </td>
                                    <td class="px-3 py-2 text-xs text-muted-foreground">{{ motor.brandText }}</td>
                                    <td class="px-3 py-2">
                                        <div class="font-mono text-xs">{{ motor.portName }}</div>
                                        <div class="text-xs text-muted-foreground">
                                            {{ motor.baudRate || '未设置' }}
                                        </div>
                                    </td>
                                    <td class="px-3 py-2 font-mono text-xs">{{ motor.slaveId }}</td>
                                    <td :class="['px-3 py-2', statusClass(motor.status)]">{{ motor.statusText }}</td>
                                    <td class="px-3 py-2 font-mono text-xs">
                                        {{ motor.lastKnownPosition }} / {{ motor.lastKnownSpeed }}
                                    </td>
                                    <td class="px-3 py-2">
                                        <div class="flex flex-wrap gap-1.5">
                                            <button
                                                class="rounded border px-2 py-0.5 text-xs hover:bg-muted/50"
                                                @click.stop="selectedId = motor.id"
                                            >
                                                控制
                                            </button>
                                            <button
                                                class="inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-muted/50"
                                                @click.stop="openEdit(motor)"
                                            >
                                                <Pencil class="size-3.5" />
                                                编辑
                                            </button>
                                        </div>
                                    </td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                </section>
            </main>

            <aside class="rounded-lg border p-4">
                <div v-if="!selectedMotor" class="py-10 text-center text-sm text-muted-foreground">
                    请选择一个电机设备
                </div>
                <div v-else class="flex flex-col gap-4">
                    <div class="flex items-start justify-between gap-3">
                        <div>
                            <h2 class="text-base font-semibold">{{ selectedMotor.name }}</h2>
                            <div class="mt-1 text-xs text-muted-foreground">
                                {{ selectedMotor.brandText }} · {{ selectedMotor.portName }} · ID
                                {{ selectedMotor.slaveId }}
                            </div>
                        </div>
                        <span :class="['text-sm', statusClass(selectedMotor.status)]">
                            {{ selectedMotor.statusText }}
                        </span>
                    </div>

                    <div class="grid grid-cols-3 gap-2 rounded border bg-muted/20 p-3 text-xs">
                        <div>
                            <div class="text-muted-foreground">位置</div>
                            <div class="mt-1 font-mono">{{ selectedMotor.lastKnownPosition }}</div>
                        </div>
                        <div>
                            <div class="text-muted-foreground">速度</div>
                            <div class="mt-1 font-mono">{{ selectedMotor.lastKnownSpeed }}</div>
                        </div>
                        <div>
                            <div class="text-muted-foreground">回零</div>
                            <div class="mt-1">{{ selectedMotor.isHomed ? '完成' : '未完成' }}</div>
                        </div>
                    </div>

                    <div v-if="isKtechSelected" class="border-t pt-4">
                        <div class="mb-3 flex items-center justify-between gap-2">
                            <h3 class="text-sm font-medium">旋转角度范围</h3>
                            <span class="font-mono text-xs text-muted-foreground">
                                {{ currentSingleTurnAngle ?? '—' }}
                            </span>
                        </div>
                        <div class="grid grid-cols-2 gap-2">
                            <label class="flex flex-col gap-1 text-sm">
                                最小角度(0.01°)
                                <input
                                    v-model="rotationRangeForm.minRotationAngleText"
                                    class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                    inputmode="numeric"
                                    placeholder="0"
                                />
                            </label>
                            <label class="flex flex-col gap-1 text-sm">
                                最大角度(0.01°)
                                <input
                                    v-model="rotationRangeForm.maxRotationAngleText"
                                    class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                    inputmode="numeric"
                                    placeholder="36000"
                                />
                            </label>
                        </div>
                        <div class="mt-2 grid grid-cols-3 gap-2">
                            <button
                                class="inline-flex items-center justify-center gap-1.5 rounded border px-2 py-1.5 text-xs hover:bg-muted/50 disabled:opacity-60"
                                :disabled="rotationSaving"
                                @click="handleSaveRotationRange"
                            >
                                保存输入
                            </button>
                            <button
                                class="inline-flex items-center justify-center gap-1.5 rounded border px-2 py-1.5 text-xs hover:bg-muted/50 disabled:opacity-60"
                                :disabled="rotationSaving"
                                @click="handleSetRotationLimitFromCurrent(MotorRotationAngleLimitKind.Minimum)"
                            >
                                <ArrowDownToLine class="size-3.5" />
                                读取设最小
                            </button>
                            <button
                                class="inline-flex items-center justify-center gap-1.5 rounded border px-2 py-1.5 text-xs hover:bg-muted/50 disabled:opacity-60"
                                :disabled="rotationSaving"
                                @click="handleSetRotationLimitFromCurrent(MotorRotationAngleLimitKind.Maximum)"
                            >
                                <ArrowUpToLine class="size-3.5" />
                                读取设最大
                            </button>
                        </div>
                    </div>

                    <div class="grid grid-cols-2 gap-2">
                        <button
                            class="inline-flex items-center justify-center gap-1.5 rounded border px-3 py-1.5 text-sm hover:bg-muted/50"
                            :disabled="acting"
                            @click="runSelectedAction(store.refreshStatus, '状态已刷新')"
                        >
                            <RefreshCw class="size-4" />
                            刷新状态
                        </button>
                        <button
                            class="inline-flex items-center justify-center gap-1.5 rounded border px-3 py-1.5 text-sm text-green-600 hover:bg-green-50"
                            :disabled="acting"
                            @click="runSelectedAction(store.enable, '电机已使能')"
                        >
                            <Zap class="size-4" />
                            使能
                        </button>
                        <button
                            class="inline-flex items-center justify-center gap-1.5 rounded border px-3 py-1.5 text-sm text-orange-500 hover:bg-orange-50"
                            :disabled="acting"
                            @click="runSelectedAction(store.disable, '电机已去使能')"
                        >
                            <ZapOff class="size-4" />
                            去使能
                        </button>
                        <button
                            class="inline-flex items-center justify-center gap-1.5 rounded border px-3 py-1.5 text-sm hover:bg-muted/50"
                            :disabled="acting"
                            @click="runSelectedAction(store.home, '回零命令已发送')"
                        >
                            <Home class="size-4" />
                            回零
                        </button>
                        <button
                            class="inline-flex items-center justify-center gap-1.5 rounded border px-3 py-1.5 text-sm hover:bg-muted/50"
                            :disabled="acting"
                            @click="runSelectedAction(store.clearFault, '故障已清除')"
                        >
                            <RotateCcw class="size-4" />
                            清故障
                        </button>
                        <button
                            class="inline-flex items-center justify-center gap-1.5 rounded border px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
                            :disabled="acting"
                            @click="runSelectedAction(store.stop, '停止命令已发送')"
                        >
                            <CircleStop class="size-4" />
                            停止
                        </button>
                    </div>
                    <button
                        class="inline-flex items-center justify-center gap-1.5 rounded bg-red-600 px-3 py-2 text-sm text-white hover:bg-red-700 disabled:opacity-60"
                        :disabled="acting"
                        @click="runSelectedAction(store.emergencyStop, '急停命令已发送')"
                    >
                        <ShieldAlert class="size-4" />
                        急停
                    </button>

                    <div class="border-t pt-4">
                        <h3 class="mb-3 text-sm font-medium">运动控制</h3>
                        <div class="grid grid-cols-2 gap-2">
                            <label class="flex flex-col gap-1 text-sm">
                                位置
                                <input
                                    v-model.number="moveForm.position"
                                    class="rounded border bg-background px-2 py-1.5 text-sm"
                                    type="number"
                                />
                            </label>
                            <label class="flex flex-col gap-1 text-sm">
                                速度 rpm
                                <input
                                    v-model.number="moveForm.speedRpm"
                                    class="rounded border bg-background px-2 py-1.5 text-sm"
                                    min="1"
                                    type="number"
                                />
                            </label>
                        </div>
                        <div class="mt-2 grid grid-cols-2 gap-2">
                            <button
                                class="inline-flex items-center justify-center gap-1.5 rounded border px-3 py-1.5 text-sm hover:bg-muted/50"
                                :disabled="acting"
                                @click="handleMoveAbsolute"
                            >
                                <Play class="size-4" />
                                绝对
                            </button>
                            <button
                                class="inline-flex items-center justify-center gap-1.5 rounded border px-3 py-1.5 text-sm hover:bg-muted/50"
                                :disabled="acting"
                                @click="handleMoveRelative"
                            >
                                <Play class="size-4" />
                                相对
                            </button>
                        </div>
                    </div>

                    <div class="border-t pt-4">
                        <h3 class="mb-3 text-sm font-medium">寄存器</h3>
                        <div class="grid grid-cols-[1fr_90px] gap-2">
                            <label class="flex flex-col gap-1 text-sm">
                                读取地址
                                <input
                                    v-model="registerForm.startAddressText"
                                    class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                />
                            </label>
                            <label class="flex flex-col gap-1 text-sm">
                                数量
                                <input
                                    v-model.number="registerForm.quantity"
                                    class="rounded border bg-background px-2 py-1.5 text-sm"
                                    max="20"
                                    min="1"
                                    type="number"
                                />
                            </label>
                        </div>
                        <button
                            class="mt-2 w-full rounded border px-3 py-1.5 text-sm hover:bg-muted/50 disabled:opacity-60"
                            :disabled="acting"
                            @click="handleReadRegisters"
                        >
                            读取寄存器
                        </button>
                        <div
                            v-if="registerReadResult"
                            class="mt-2 break-words rounded border bg-muted/20 p-2 font-mono text-xs text-muted-foreground"
                        >
                            {{ registerReadResult }}
                        </div>

                        <div class="mt-3 grid grid-cols-2 gap-2">
                            <label class="flex flex-col gap-1 text-sm">
                                写入地址
                                <input
                                    v-model="registerForm.writeAddressText"
                                    class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                />
                            </label>
                            <label class="flex flex-col gap-1 text-sm">
                                写入值
                                <input
                                    v-model="registerForm.writeValueText"
                                    class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                                />
                            </label>
                        </div>
                        <button
                            class="mt-2 w-full rounded border px-3 py-1.5 text-sm hover:bg-muted/50 disabled:opacity-60"
                            :disabled="acting"
                            @click="handleWriteRegister"
                        >
                            写单寄存器
                        </button>
                    </div>
                </div>
            </aside>
        </div>

        <div
            v-if="showEditDialog"
            class="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
            @click.self="showEditDialog = false"
        >
            <div class="w-[440px] rounded-lg border bg-background p-6 shadow-lg">
                <h2 class="mb-4 text-base font-semibold">编辑电机设备</h2>
                <div class="grid gap-3">
                    <label class="flex flex-col gap-1 text-sm">
                        名称
                        <input v-model="editForm.name" class="rounded border bg-background px-2 py-1.5 text-sm" />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        型号
                        <input v-model="editForm.model" class="rounded border bg-background px-2 py-1.5 text-sm" />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        描述
                        <input
                            v-model="editForm.description"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                        />
                    </label>
                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="editForm.isEnabled" type="checkbox" />
                        启用
                    </label>
                </div>
                <div class="mt-5 flex justify-end gap-2">
                    <button
                        class="rounded border px-4 py-1.5 text-sm hover:bg-muted/50"
                        @click="showEditDialog = false"
                    >
                        取消
                    </button>
                    <button
                        class="rounded bg-primary px-4 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                        :disabled="editing"
                        @click="handleEdit"
                    >
                        {{ editing ? '保存中…' : '保存' }}
                    </button>
                </div>
            </div>
        </div>
    </div>
</template>
