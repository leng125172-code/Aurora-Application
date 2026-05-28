<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Pencil, RefreshCw, ScanLine, Sliders } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { Card, CardContent } from '@/components/ui/card'
import { BorderBeam } from '@/components/ui/border-beam'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogClose } from '@/components/ui/dialog'

import {
    MotorBrand,
    MotorDeviceStatus,
    MotorScanProgressKind,
    type MotorAxisDto,
    type MotorScanProgressDto,
} from '@/api/motors'
import { SerialPortConsts } from '@/constants/serialPortConsts'
import { useMotorStore } from '@/stores/motors'

const { t } = useI18n()
const store = useMotorStore()
const router = useRouter()
const selectedId = ref('')
const scanning = ref(false)
const scanResultText = ref('')

const baudRateOptions = SerialPortConsts.supportedBaudRates
const commonBaudRates = [115200, 38400, 9600, 19200, 57600]
const scanForm = ref({
    startSlaveId: 1,
    endSlaveId: 32,
    baudRates: [...commonBaudRates],
    probeTimeoutMs: 150,
})
const showEditDialog = ref(false)
const editing = ref(false)
const editingId = ref('')

// 编辑表单本地类型（description 固定为 string，避免 null 与 Input v-model 冲突）
interface EditMotorForm {
    name: string
    description: string
    isEnabled: boolean
    model: string
}
const editForm = ref<EditMotorForm>({
    name: '',
    description: '',
    isEnabled: true,
    model: '',
})

// 当前正在编辑的电机对象，用于根据品牌提供对应的型号选项
const editingMotor = computed(() => store.motors.find((motor) => motor.id === editingId.value) ?? null)
// 根据品牌返回可选型号列表：瓴控→MS4010，雷赛→iCL42-RS06
const modelOptionsForEditing = computed(() => {
    const brand = editingMotor.value?.brand
    if (brand === MotorBrand.KtechKtech) return ['MS4010']
    if (brand === MotorBrand.LeisaiIclRs) return ['iCL42-RS06']
    return []
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
        scanResultText.value = t('motor.scanSuccess', {
            tried: result.triedCount,
            found: result.foundCount,
            elapsed: Math.round(result.elapsedMs / 1000),
        })
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

// 跳转到瓴控 KTECH 电机操作台
function openKtechConsole(axisId: string) {
    router.push({ name: 'KtechMotorConsole', params: { axisId } })
}

// 跳转到雷赛 iCL-RS 电机操作台
function openLeisaiConsole(axisId: string) {
    router.push({ name: 'LeisaiMotorConsole', params: { axisId } })
}

async function handleEdit() {
    editing.value = true
    try {
        await store.update(editingId.value, editForm.value)
        toast.success(t('motor.updateSuccess'))
        showEditDialog.value = false
    } catch {
        // 忽略
    } finally {
        editing.value = false
    }
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
    void store.startScanHub()
})

onUnmounted(() => {
    void store.stopScanHub()
})

/**
 * 扫描进度类型对应的标签。
 */
function progressKindLabel(kind: MotorScanProgressKind): string {
    switch (kind) {
        case MotorScanProgressKind.Started:
            return t('motor.progressStarted')
        case MotorScanProgressKind.PortStarted:
            return t('motor.progressPortStarted')
        case MotorScanProgressKind.Probing:
            return t('motor.progressProbing')
        case MotorScanProgressKind.DeviceFound:
            return t('motor.progressDeviceFound')
        case MotorScanProgressKind.PortFinished:
            return t('motor.progressPortFinished')
        case MotorScanProgressKind.PortError:
            return t('motor.progressPortError')
        case MotorScanProgressKind.Completed:
            return t('motor.progressCompleted')
        default:
            return t('motor.progressOther')
    }
}

/**
 * 扫描进度类型的颜色样式。
 */
function progressKindClass(kind: MotorScanProgressKind): string {
    switch (kind) {
        case MotorScanProgressKind.DeviceFound:
            return 'text-emerald-600'
        case MotorScanProgressKind.PortError:
            return 'text-red-600'
        case MotorScanProgressKind.Started:
        case MotorScanProgressKind.Completed:
            return 'text-blue-600'
        case MotorScanProgressKind.PortStarted:
        case MotorScanProgressKind.PortFinished:
            return 'text-amber-600'
        default:
            return 'text-muted-foreground'
    }
}

/**
 * 格式化进度推送的时间戳为 HH:mm:ss.fff。
 */
function formatProgressTime(timestamp: string): string {
    const d = new Date(timestamp)
    if (Number.isNaN(d.getTime())) return timestamp
    const pad = (n: number, w = 2) => n.toString().padStart(w, '0')
    return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`
}

/**
 * 拼装进度详细描述。
 */
function progressDetail(p: MotorScanProgressDto): string {
    const parts: string[] = []
    if (p.portName) parts.push(p.portName)
    if (p.baudRate) parts.push(`${p.baudRate}bps`)
    if (p.slaveId != null) parts.push(`ID=${p.slaveId}`)
    if (p.brand) parts.push(p.brand)
    return parts.join(' · ')
}
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('motor.title') }}</h1>
            <div class="flex flex-wrap gap-2">
                <Button variant="outline" size="sm" @click="refreshList">
                    <RefreshCw class="size-4" />
                    {{ t('motor.refresh') }}
                </Button>
                <Button size="sm" :disabled="scanning || scanForm.baudRates.length === 0" @click="handleScan">
                    <ScanLine class="size-4" />
                    {{ scanning ? t('motor.scanning') : t('motor.scan') }}
                </Button>
            </div>
        </div>

        <div class="flex flex-col gap-4">
            <main class="min-w-0 space-y-4">
                <Card class="relative">
                    <BorderBeam :size="80" :duration="8" />
                    <CardContent class="p-4">
                        <div class="grid gap-3 md:grid-cols-[120px_120px_120px_1fr]">
                            <div class="flex flex-col gap-1">
                                <Label class="text-sm">{{ t('motor.startId') }}</Label>
                                <Input v-model.number="scanForm.startSlaveId" max="32" min="1" type="number" />
                            </div>
                            <div class="flex flex-col gap-1">
                                <Label class="text-sm">{{ t('motor.endId') }}</Label>
                                <Input v-model.number="scanForm.endSlaveId" max="32" min="1" type="number" />
                            </div>
                            <div class="flex flex-col gap-1">
                                <Label class="text-sm">{{ t('motor.timeoutMs') }}</Label>
                                <Input v-model.number="scanForm.probeTimeoutMs" min="50" type="number" />
                            </div>
                            <div class="flex items-end gap-2">
                                <Button variant="outline" size="sm" type="button" @click="setAllBaudRates(false)">
                                    {{ t('motor.common') }}
                                </Button>
                                <Button variant="outline" size="sm" type="button" @click="setAllBaudRates(true)">
                                    {{ t('motor.selectAll') }}
                                </Button>
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
                            {{ scanning ? t('motor.scanDesc') : scanResultText }}
                        </div>
                        <!-- 扫描进度实时日志（SignalR 推送） -->
                        <div
                            v-if="store.scanProgress.length > 0"
                            class="mt-2 max-h-56 overflow-auto rounded border bg-muted/30 p-2 font-mono text-[11px] leading-5"
                        >
                            <div
                                v-for="(p, idx) in store.scanProgress"
                                :key="idx"
                                class="flex items-start gap-2 border-b border-border/40 py-0.5 last:border-0"
                            >
                                <span class="shrink-0 text-muted-foreground">
                                    {{ formatProgressTime(p.timestamp) }}
                                </span>
                                <span class="shrink-0 w-16" :class="progressKindClass(p.kind)">
                                    [{{ progressKindLabel(p.kind) }}]
                                </span>
                                <span class="shrink-0 text-muted-foreground">
                                    {{
                                        t('motor.progressSummary', {
                                            finished: p.finishedPorts,
                                            total: p.totalPorts,
                                            tried: p.triedCount,
                                            found: p.foundCount,
                                        })
                                    }}
                                </span>
                                <span class="flex-1 break-all">
                                    <span v-if="progressDetail(p)" class="text-foreground/80">
                                        {{ progressDetail(p) }}
                                    </span>
                                    <span v-if="progressDetail(p)" class="mx-1 text-muted-foreground">|</span>
                                    <span>{{ p.message }}</span>
                                </span>
                            </div>
                        </div>
                    </CardContent>
                </Card>

                <section>
                    <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">
                        {{ t('common.loading') }}
                    </div>
                    <div v-else-if="store.motors.length === 0" class="py-8 text-center text-sm text-muted-foreground">
                        {{ t('motor.noDevices') }}
                    </div>
                    <div v-else class="overflow-auto">
                        <Card class="overflow-auto">
                            <CardContent class="p-0">
                                <table class="w-full min-w-[960px] text-sm">
                                    <thead class="border-b bg-muted/50">
                                        <tr>
                                            <th class="px-3 py-2 text-left font-medium">{{ t('motor.axis') }}</th>
                                            <th class="px-3 py-2 text-left font-medium">{{ t('motor.name') }}</th>
                                            <th class="px-3 py-2 text-left font-medium">{{ t('motor.brandModel') }}</th>
                                            <th class="px-3 py-2 text-left font-medium">{{ t('motor.port') }}</th>
                                            <th class="px-3 py-2 text-left font-medium">{{ t('motor.slaveId') }}</th>
                                            <th class="px-3 py-2 text-left font-medium">{{ t('motor.status') }}</th>
                                            <th class="px-3 py-2 text-left font-medium">{{ t('common.actions') }}</th>
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
                                                <div
                                                    v-if="motor.description"
                                                    class="max-w-[180px] truncate text-xs text-muted-foreground"
                                                >
                                                    {{ motor.description }}
                                                </div>
                                            </td>
                                            <td class="px-3 py-2 text-xs">
                                                <div class="text-muted-foreground">{{ motor.brandText }}</div>
                                                <div v-if="motor.model" class="mt-0.5 font-medium text-foreground">
                                                    {{ motor.model }}
                                                </div>
                                                <div v-else class="mt-0.5 text-amber-500">{{ t('motor.noModel') }}</div>
                                            </td>
                                            <td class="px-3 py-2">
                                                <div class="font-mono text-xs">{{ motor.portName }}</div>
                                                <div class="text-xs text-muted-foreground">
                                                    {{ motor.baudRate || t('motor.notSet') }}
                                                </div>
                                            </td>
                                            <td class="px-3 py-2 font-mono text-xs">{{ motor.slaveId }}</td>
                                            <td :class="['px-3 py-2', statusClass(motor.status)]">
                                                {{ motor.statusText }}
                                            </td>
                                            <td class="px-3 py-2">
                                                <div class="flex flex-wrap gap-1.5">
                                                    <Button variant="outline" size="xs" @click.stop="openEdit(motor)">
                                                        <Pencil class="size-3.5" />
                                                        {{ t('motor.edit') }}
                                                    </Button>
                                                    <Button
                                                        v-if="motor.brand === MotorBrand.KtechKtech"
                                                        variant="outline"
                                                        size="xs"
                                                        :class="
                                                            motor.model
                                                                ? 'text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/30'
                                                                : 'opacity-50'
                                                        "
                                                        :disabled="!motor.model"
                                                        :title="
                                                            motor.model
                                                                ? t('motor.ktechConsoleTitle')
                                                                : t('motor.needModel')
                                                        "
                                                        @click.stop="openKtechConsole(motor.id)"
                                                    >
                                                        <Sliders class="size-3.5" />
                                                        {{ t('motor.console') }}
                                                    </Button>
                                                    <Button
                                                        v-if="motor.brand === MotorBrand.LeisaiIclRs"
                                                        variant="outline"
                                                        size="xs"
                                                        :class="
                                                            motor.model
                                                                ? 'text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/30'
                                                                : 'opacity-50'
                                                        "
                                                        :disabled="!motor.model"
                                                        :title="
                                                            motor.model
                                                                ? t('motor.leisaiConsoleTitle')
                                                                : t('motor.needModel')
                                                        "
                                                        @click.stop="openLeisaiConsole(motor.id)"
                                                    >
                                                        <Sliders class="size-3.5" />
                                                        {{ t('motor.console') }}
                                                    </Button>
                                                </div>
                                            </td>
                                        </tr>
                                    </tbody>
                                </table>
                            </CardContent>
                        </Card>
                    </div>
                </section>
            </main>
        </div>

        <!-- 编辑电机对话框 -->
        <Dialog v-model:open="showEditDialog">
            <DialogContent class="w-[440px]">
                <BorderBeam :size="80" :duration="8" />
                <DialogHeader>
                    <DialogTitle>{{ t('motor.editTitle') }}</DialogTitle>
                </DialogHeader>
                <div class="grid gap-3">
                    <div class="flex flex-col gap-1">
                        <Label>{{ t('motor.name') }}</Label>
                        <Input v-model="editForm.name" />
                    </div>
                    <div class="flex flex-col gap-1">
                        <Label>{{ t('motor.model') }}</Label>
                        <Select v-model="editForm.model">
                            <SelectTrigger>
                                <SelectValue :placeholder="t('motor.selectModel')" />
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem value="">{{ t('motor.selectModel') }}</SelectItem>
                                <SelectItem v-for="opt in modelOptionsForEditing" :key="opt" :value="opt">
                                    {{ opt }}
                                </SelectItem>
                            </SelectContent>
                        </Select>
                    </div>
                    <div class="flex flex-col gap-1">
                        <Label>{{ t('motor.description') }}</Label>
                        <Input v-model="editForm.description" />
                    </div>
                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="editForm.isEnabled" type="checkbox" />
                        {{ t('motor.enabled') }}
                    </label>
                </div>
                <DialogFooter class="gap-2">
                    <DialogClose as-child>
                        <Button variant="outline">{{ t('common.cancel') }}</Button>
                    </DialogClose>
                    <Button :disabled="editing" @click="handleEdit">
                        {{ editing ? t('common.saving') : t('common.save') }}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    </div>
</template>
