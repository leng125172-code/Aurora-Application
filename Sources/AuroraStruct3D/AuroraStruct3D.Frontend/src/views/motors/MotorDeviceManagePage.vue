<script setup lang="ts">
// 电机设备管理页（PrimeVue 重构版）
// 提供：扫描参数表单 + 进度日志 + 设备 DataTable（支持编辑/跳转对应操作台）
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Pencil, RefreshCw, ScanLine, Sliders } from '@lucide/vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import Checkbox from 'primevue/checkbox'
import ToggleSwitch from 'primevue/toggleswitch'
import Dialog from 'primevue/dialog'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import { AppCard } from '@/components/primevue'
import { useAppToast } from '@/composables/useAppToast'

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
const toast = useAppToast()
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

// 编辑表单本地类型（description 固定为 string，避免 null 与 InputText v-model 冲突）
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
const modelOptionsForEditing = computed<string[]>(() => {
    const brand = editingMotor.value?.brand
    if (brand === MotorBrand.KtechKtech) return ['MS4010']
    if (brand === MotorBrand.LeisaiIclRs) return ['iCL42-RS06']
    return []
})

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

/** 扫描进度类型对应的标签 */
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

/** 扫描进度类型的颜色样式 */
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

/** 格式化进度推送的时间戳为 HH:mm:ss.fff */
function formatProgressTime(timestamp: string): string {
    const d = new Date(timestamp)
    if (Number.isNaN(d.getTime())) return timestamp
    const pad = (n: number, w = 2) => n.toString().padStart(w, '0')
    return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`
}

/** 拼装进度详细描述 */
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
                <Button severity="secondary" outlined size="small" @click="refreshList">
                    <RefreshCw class="size-4" />
                    {{ t('motor.refresh') }}
                </Button>
                <Button
                    severity="primary"
                    size="small"
                    :disabled="scanning || scanForm.baudRates.length === 0"
                    @click="handleScan"
                >
                    <ScanLine class="size-4" />
                    {{ scanning ? t('motor.scanning') : t('motor.scan') }}
                </Button>
            </div>
        </div>

        <main class="min-w-0 space-y-4">
            <!-- 扫描参数 + 进度日志 -->
            <AppCard :beam-size="80" :beam-duration="8">
                <div class="p-4">
                    <div class="grid gap-3 md:grid-cols-[120px_120px_120px_1fr]">
                        <div class="flex flex-col gap-1">
                            <label class="text-sm">{{ t('motor.startId') }}</label>
                            <InputNumber v-model="scanForm.startSlaveId" :max="32" :min="1" show-buttons />
                        </div>
                        <div class="flex flex-col gap-1">
                            <label class="text-sm">{{ t('motor.endId') }}</label>
                            <InputNumber v-model="scanForm.endSlaveId" :max="32" :min="1" show-buttons />
                        </div>
                        <div class="flex flex-col gap-1">
                            <label class="text-sm">{{ t('motor.timeoutMs') }}</label>
                            <InputNumber v-model="scanForm.probeTimeoutMs" :min="50" show-buttons />
                        </div>
                        <div class="flex items-end gap-2">
                            <Button
                                severity="secondary"
                                outlined
                                size="small"
                                type="button"
                                @click="setAllBaudRates(false)"
                            >
                                {{ t('motor.common') }}
                            </Button>
                            <Button
                                severity="secondary"
                                outlined
                                size="small"
                                type="button"
                                @click="setAllBaudRates(true)"
                            >
                                {{ t('motor.selectAll') }}
                            </Button>
                        </div>
                    </div>
                    <div class="mt-3 flex flex-wrap gap-3">
                        <div
                            v-for="rate in baudRateOptions"
                            :key="rate"
                            class="inline-flex items-center gap-2 rounded border px-2 py-1 text-xs"
                        >
                            <Checkbox
                                v-model="scanForm.baudRates"
                                :input-id="`baud-${rate}`"
                                :value="rate"
                            />
                            <label :for="`baud-${rate}`">{{ rate }}</label>
                        </div>
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
                </div>
            </AppCard>

            <!-- 设备列表 -->
            <section>
                <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">
                    {{ t('common.loading') }}
                </div>
                <div v-else-if="store.motors.length === 0" class="py-8 text-center text-sm text-muted-foreground">
                    {{ t('motor.noDevices') }}
                </div>
                <AppCard v-else :beam="false">
                    <DataTable
                        :value="store.motors"
                        data-key="id"
                        size="small"
                        striped-rows
                        scrollable
                    >
                        <Column field="axisIndex" :header="t('motor.axis')" style="width: 80px" />
                        <Column :header="t('motor.name')">
                            <template #body="{ data }: { data: MotorAxisDto }">
                                <div class="font-medium">{{ data.name }}</div>
                                <div
                                    v-if="data.description"
                                    class="max-w-[180px] truncate text-xs text-muted-foreground"
                                >
                                    {{ data.description }}
                                </div>
                            </template>
                        </Column>
                        <Column :header="t('motor.brandModel')">
                            <template #body="{ data }: { data: MotorAxisDto }">
                                <div class="text-xs text-muted-foreground">{{ data.brandText }}</div>
                                <div v-if="data.model" class="mt-0.5 text-xs font-medium text-foreground">
                                    {{ data.model }}
                                </div>
                                <div v-else class="mt-0.5 text-xs text-amber-500">{{ t('motor.noModel') }}</div>
                            </template>
                        </Column>
                        <Column :header="t('motor.port')">
                            <template #body="{ data }: { data: MotorAxisDto }">
                                <div class="font-mono text-xs">{{ data.portName }}</div>
                                <div class="text-xs text-muted-foreground">
                                    {{ data.baudRate || t('motor.notSet') }}
                                </div>
                            </template>
                        </Column>
                        <Column field="slaveId" :header="t('motor.slaveId')">
                            <template #body="{ data }: { data: MotorAxisDto }">
                                <span class="font-mono text-xs">{{ data.slaveId }}</span>
                            </template>
                        </Column>
                        <Column :header="t('motor.status')">
                            <template #body="{ data }: { data: MotorAxisDto }">
                                <span :class="statusClass(data.status)">{{ data.statusText }}</span>
                            </template>
                        </Column>
                        <Column :header="t('common.actions')">
                            <template #body="{ data }: { data: MotorAxisDto }">
                                <div class="flex flex-wrap gap-1.5">
                                    <Button
                                        severity="secondary"
                                        outlined
                                        size="small"
                                        @click.stop="openEdit(data)"
                                    >
                                        <Pencil class="size-3.5" />
                                        {{ t('motor.edit') }}
                                    </Button>
                                    <Button
                                        v-if="data.brand === MotorBrand.KtechKtech"
                                        severity="info"
                                        outlined
                                        size="small"
                                        :disabled="!data.model"
                                        :title="
                                            data.model ? t('motor.ktechConsoleTitle') : t('motor.needModel')
                                        "
                                        @click.stop="openKtechConsole(data.id)"
                                    >
                                        <Sliders class="size-3.5" />
                                        {{ t('motor.console') }}
                                    </Button>
                                    <Button
                                        v-if="data.brand === MotorBrand.LeisaiIclRs"
                                        severity="info"
                                        outlined
                                        size="small"
                                        :disabled="!data.model"
                                        :title="
                                            data.model ? t('motor.leisaiConsoleTitle') : t('motor.needModel')
                                        "
                                        @click.stop="openLeisaiConsole(data.id)"
                                    >
                                        <Sliders class="size-3.5" />
                                        {{ t('motor.console') }}
                                    </Button>
                                </div>
                            </template>
                        </Column>
                    </DataTable>
                </AppCard>
            </section>
        </main>

        <!-- 编辑电机对话框 -->
        <Dialog v-model:visible="showEditDialog" modal :header="t('motor.editTitle')" :style="{ width: '440px' }">
            <div class="grid gap-3">
                <div class="flex flex-col gap-1">
                    <label class="text-sm">{{ t('motor.name') }}</label>
                    <InputText v-model="editForm.name" />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm">{{ t('motor.model') }}</label>
                    <Select
                        v-model="editForm.model"
                        :options="modelOptionsForEditing"
                        :placeholder="t('motor.selectModel')"
                        show-clear
                    />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm">{{ t('motor.description') }}</label>
                    <InputText v-model="editForm.description" />
                </div>
                <div class="flex items-center gap-2 text-sm">
                    <ToggleSwitch v-model="editForm.isEnabled" input-id="motor-enabled" />
                    <label for="motor-enabled">{{ t('motor.enabled') }}</label>
                </div>
            </div>
            <template #footer>
                <Button severity="secondary" outlined @click="showEditDialog = false">
                    {{ t('common.cancel') }}
                </Button>
                <Button :disabled="editing" @click="handleEdit">
                    {{ editing ? t('common.saving') : t('common.save') }}
                </Button>
            </template>
        </Dialog>
    </div>
</template>
