<script setup lang="ts">
/**
 * 雷赛 iCL-RS 电机操作台
 *  - 6 个 Tab：图表 / 状态 / IO / 故障 / 手动 Modbus / 参数
 *  - 通过 SignalR 实时接收状态推送（250 ms 周期）
 *  - REST 调用统一走 @/api/leisai
 */
import * as echarts from 'echarts'
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft, AlertTriangle, RefreshCw, Save } from '@lucide/vue'
import Button from 'primevue/button'
import Select from 'primevue/select'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import ToggleSwitch from 'primevue/toggleswitch'
import { AppCard } from '@/components/primevue'
import { useAppToast } from '@/composables/useAppToast'
import { useAppConfirm } from '@/composables/useAppConfirm'
import {
    LeisaiParamGroupNames,
    type LeisaiBatchReadResultDto,
    type LeisaiIoConfigDto,
    type LeisaiParameterMetadataDto,
    type LeisaiRawModbusResultDto,
    type LeisaiTraceDto,
} from '@/api/leisai'
import { useLeisaiMotorStore } from '@/stores/leisai'
import { useThemeStore } from '@/stores/theme'

type TabKey = 'chart' | 'status' | 'io' | 'fault' | 'raw' | 'params'

const route = useRoute()
const router = useRouter()
const store = useLeisaiMotorStore()
const themeStore = useThemeStore()
const { t } = useI18n()
const toast = useAppToast()
const confirmAction = useAppConfirm()

const axisId = computed(() => String(route.params.axisId ?? ''))
const activeTab = ref<TabKey>('chart')
const acting = ref(false)

const liveState = computed(() => store.stateByAxis[axisId.value] ?? null)
const trace = computed<LeisaiTraceDto>(
    () =>
        store.traceByAxis[axisId.value] ?? {
            axisId: axisId.value,
            actualPositions: [],
            commandPositions: [],
            speeds: [],
        }
)

// ───── 采样开关 ─────────────────────────────────────────────────────
const isSamplingEnabled = ref(false)

/** 切换实时采样开关；ToggleSwitch v-model 已将 isSamplingEnabled 更新为新值后调用。 */
async function toggleSampling(): Promise<void> {
    const id = axisId.value
    if (!id) return
    const newValue = isSamplingEnabled.value // v-model 已更新为期望的新值
    try {
        if (newValue) {
            await store.api.enableSampling(id)
        } else {
            await store.api.disableSampling(id)
        }
    } catch (err) {
        isSamplingEnabled.value = !newValue // 失败时还原
        toast.error(t('leisaiConsole.samplingToggleFailed', { err: String(err) }))
    }
}

// ───── IO 配置 ──────────────────────────────────────────────────────
const ioConfig = ref<LeisaiIoConfigDto | null>(null)

async function loadIoConfig(): Promise<void> {
    try {
        ioConfig.value = await store.api.getIoConfig(axisId.value)
    } catch (err) {
        toast.error(t('leisaiConsole.ioConfigLoadFailed', { err: String(err) }))
    }
}

/** DI 功能码 → i18n 键映射 */
const DI_FUNC_KEYS: Record<number, string> = {
    0x00: 'leisaiConsole.diFuncNone',
    0x01: 'leisaiConsole.diFuncEnable',
    0x02: 'leisaiConsole.diFuncFaultClear',
    0x03: 'leisaiConsole.diFuncForwardJog',
    0x04: 'leisaiConsole.diFuncReverseJog',
    0x05: 'leisaiConsole.diFuncForceStop',
    0x06: 'leisaiConsole.diFuncPosLimit',
    0x07: 'leisaiConsole.diFuncNegLimit',
    0x08: 'leisaiConsole.diFuncOrigin',
    0x09: 'leisaiConsole.diFuncPathTrigger',
    0x0a: 'leisaiConsole.diFuncPathAddr0',
    0x0b: 'leisaiConsole.diFuncPathAddr1',
    0x0c: 'leisaiConsole.diFuncPathAddr2',
    0x0d: 'leisaiConsole.diFuncPathAddr3',
}
/** DO 功能码 → i18n 键映射 */
const DO_FUNC_KEYS: Record<number, string> = {
    0x00: 'leisaiConsole.doFuncNone',
    0x01: 'leisaiConsole.doFuncReady',
    0x02: 'leisaiConsole.doFuncMotorEnable',
    0x03: 'leisaiConsole.doFuncBrake',
    0x04: 'leisaiConsole.doFuncFault',
    0x05: 'leisaiConsole.doFuncInPosition',
    0x06: 'leisaiConsole.doFuncHomeDone',
    0x07: 'leisaiConsole.doFuncCmdDone',
}

function diLabel(index: number): string {
    const code = ioConfig.value?.diFunctionCodes?.[index] ?? 0
    const base = code & 0x7f
    const nc = (code & 0x80) !== 0
    const key = DI_FUNC_KEYS[base]
    const name = key ? t(key) : `0x${base.toString(16).padStart(2, '0')}`
    return nc ? t('leisaiConsole.ncLabel', { name }) : name
}

function doLabel(index: number): string {
    const code = ioConfig.value?.doFunctionCodes?.[index] ?? 0
    const base = code & 0x7f
    const nc = (code & 0x80) !== 0
    const key = DO_FUNC_KEYS[base]
    const name = key ? t(key) : `0x${base.toString(16).padStart(2, '0')}`
    return nc ? t('leisaiConsole.ncLabel', { name }) : name
}

function diBit(index: number): boolean {
    if (!liveState.value) return false
    return (liveState.value.inputIoBitmap & (1 << index)) !== 0
}

function doBit(index: number): boolean {
    if (!liveState.value) return false
    return (liveState.value.outputIoBitmap & (1 << index)) !== 0
}

async function writeDo(doIndex: number, value: boolean): Promise<void> {
    if (acting.value) return
    acting.value = true
    try {
        await store.api.writeOutput(axisId.value, { doIndex, value })
        toast.success(t('leisaiConsole.doWriteSuccess', { index: doIndex, state: value ? 'ON' : 'OFF' }))
    } catch (err) {
        toast.error(t('leisaiConsole.doWriteFailed', { err: String(err) }))
    } finally {
        acting.value = false
    }
}

// ───── 故障操作 ─────────────────────────────────────────────────────
async function clearFault(mode: 'current' | 'all'): Promise<void> {
    if (acting.value) return
    acting.value = true
    try {
        await store.api.clearFault(axisId.value, { mode })
        toast.success(mode === 'current' ? t('leisaiConsole.faultClearedCurrent') : t('leisaiConsole.faultClearedAll'))
    } catch (err) {
        toast.error(t('leisaiConsole.faultClearFailed', { err: String(err) }))
    } finally {
        acting.value = false
    }
}

async function saveToEeprom(): Promise<void> {
    if (acting.value) return
    acting.value = true
    try {
        await store.api.saveToEeprom(axisId.value)
        toast.success(t('leisaiConsole.eepromSaved'))
    } catch (err) {
        toast.error(t('leisaiConsole.saveFailed', { err: String(err) }))
    } finally {
        acting.value = false
    }
}

// ───── 手动 Modbus ──────────────────────────────────────────────────
const rawForm = reactive({
    op: 'read' as 'read' | 'write',
    fc: 3,
    startAddress: 0x0145,
    quantity: 1,
    valuesText: '0',
})
const lastRawResult = ref<LeisaiRawModbusResultDto | null>(null)

async function runRawCommand(): Promise<void> {
    if (acting.value) return
    acting.value = true
    try {
        if (rawForm.op === 'read') {
            lastRawResult.value = await store.api.rawRead(axisId.value, {
                functionCode: 3,
                startAddress: rawForm.startAddress,
                quantity: Math.max(1, Math.min(125, rawForm.quantity)),
            })
        } else {
            // 解析 valuesText：支持十进制 / 0x 十六进制，逗号或空白分隔
            const values = rawForm.valuesText
                .split(/[,\s]+/)
                .filter(Boolean)
                .map((s) => {
                    const v = s.startsWith('0x') || s.startsWith('0X') ? parseInt(s, 16) : parseInt(s, 10)
                    return Number.isFinite(v) ? v & 0xffff : 0
                })
            if (values.length === 0) {
                toast.error(t('leisaiConsole.rawWriteEmpty'))
                acting.value = false
                return
            }
            lastRawResult.value = await store.api.rawWrite(axisId.value, {
                functionCode: rawForm.fc === 6 ? 6 : 16,
                startAddress: rawForm.startAddress,
                values,
            })
        }
        if (lastRawResult.value && !lastRawResult.value.success) {
            toast.error(lastRawResult.value.errorMessage ?? t('leisaiConsole.modbusFailed'))
        }
    } catch (err) {
        toast.error(t('leisaiConsole.modbusError', { err: String(err) }))
    } finally {
        acting.value = false
    }
}

// ───── 参数配置 (Phase 2) ───────────────────────────────────────────
/** 全部参数元数据（首次进入 Tab 时按需加载）。 */
const paramMetadata = ref<LeisaiParameterMetadataDto[]>([])
/** 各地址当前读取到的值（key=AddressLow）。 */
const paramValues = ref<Record<number, number>>({})
/** 各地址正在编辑的值（key=AddressLow）。 */
const paramEdits = ref<Record<number, number>>({})
/** 各地址最近一次错误。 */
const paramErrors = ref<Record<number, string>>({})
/** 当前选中的分组（0..9）。 */
const currentGroup = ref<number>(0)
/** 加载/写入状态。 */
const isLoadingParams = ref(false)
const isWritingParams = ref(false)
/** 搜索关键字（名称 / Pr 编号 / 地址）。 */
const paramSearch = ref('')

/** 可用分组列表（按目录中实际出现的 group 去重排序）。 */
const availableGroups = computed<number[]>(() => {
    const set = new Set<number>()
    paramMetadata.value.forEach((p) => set.add(p.group))
    return Array.from(set).sort((a, b) => a - b)
})

/** 当前分组的全部参数。 */
const currentGroupParams = computed<LeisaiParameterMetadataDto[]>(() => {
    const list = paramMetadata.value.filter((p) => p.group === currentGroup.value)
    const kw = paramSearch.value.trim().toLowerCase()
    if (!kw) return list
    return list.filter(
        (p) =>
            p.pr.toLowerCase().includes(kw) ||
            p.name.toLowerCase().includes(kw) ||
            `0x${p.addressLow.toString(16).padStart(4, '0')}`.includes(kw)
    )
})

/** 是否修改过。 */
function isParamDirty(addr: number): boolean {
    return paramEdits.value[addr] !== undefined && paramEdits.value[addr] !== paramValues.value[addr]
}

/** 修改过的项目数。 */
const dirtyCount = computed<number>(
    () =>
        Object.keys(paramEdits.value)
            .map((k) => Number(k))
            .filter((a) => isParamDirty(a)).length
)

async function ensureMetadataLoaded(): Promise<void> {
    if (paramMetadata.value.length > 0) return
    isLoadingParams.value = true
    try {
        paramMetadata.value = await store.api.getParameterMetadata(axisId.value)
        // 默认聚焦到第一个分组
        if (availableGroups.value.length > 0 && !availableGroups.value.includes(currentGroup.value)) {
            currentGroup.value = availableGroups.value[0]
        }
    } catch (err) {
        toast.error(t('leisaiConsole.paramMetaLoadFailed', { err: String(err) }))
    } finally {
        isLoadingParams.value = false
    }
}

function applyBatchResult(result: LeisaiBatchReadResultDto): void {
    for (const item of result.items) {
        if (item.isSuccess) {
            paramValues.value[item.addressLow] = item.currentValue
            // 仅在用户没在编辑时同步到 edit 槽
            if (paramEdits.value[item.addressLow] === undefined) {
                paramEdits.value[item.addressLow] = item.currentValue
            }
            delete paramErrors.value[item.addressLow]
        } else {
            paramErrors.value[item.addressLow] = item.errorMessage ?? t('leisaiConsole.readFailed')
        }
    }
}

/** 读取当前分组（方式1：parameter-metadata + group，返回含 currentValue 的元数据，同时刷新元数据列表）。 */
async function readCurrentGroupV1(): Promise<void> {
    if (isLoadingParams.value) return
    isLoadingParams.value = true
    try {
        const items = await store.api.getParameterMetadata(axisId.value, currentGroup.value)
        // 刷新元数据（仅当前组）
        const otherGroups = paramMetadata.value.filter((p) => p.group !== currentGroup.value)
        paramMetadata.value = [...otherGroups, ...items]
        // 将 currentValue 回填到 paramValues
        for (const item of items) {
            if (item.currentValue !== null && item.currentValue !== undefined) {
                paramValues.value[item.addressLow] = item.currentValue
                if (paramEdits.value[item.addressLow] === undefined) {
                    paramEdits.value[item.addressLow] = item.currentValue
                }
                delete paramErrors.value[item.addressLow]
            }
        }
        toast.success(
            t('leisaiConsole.readGroupV1Success', {
                group: LeisaiParamGroupNames[currentGroup.value] ?? `Pr${currentGroup.value}`,
                count: items.length,
            })
        )
    } catch (err) {
        toast.error(t('leisaiConsole.readGroupV1Failed', { err: String(err) }))
    } finally {
        isLoadingParams.value = false
    }
}

/** 读取当前分组（方式2：read-group-parameters + group，直接批量 Modbus 读寄存器）。 */
async function readCurrentGroupV2(): Promise<void> {
    if (isLoadingParams.value) return
    isLoadingParams.value = true
    try {
        const result = await store.api.readGroupParameters(axisId.value, currentGroup.value)
        applyBatchResult(result)
        toast.success(
            t('leisaiConsole.readGroupV2Success', {
                group: LeisaiParamGroupNames[currentGroup.value] ?? `Pr${currentGroup.value}`,
                success: result.successCount,
                fail: result.failureCount,
                ms: result.elapsedMs,
            })
        )
    } catch (err) {
        toast.error(t('leisaiConsole.readGroupV2Failed', { err: String(err) }))
    } finally {
        isLoadingParams.value = false
    }
}

/** 仅写入已修改的参数。 */
async function writeDirtyParams(): Promise<void> {
    if (isWritingParams.value) return
    const items = Object.keys(paramEdits.value)
        .map((k) => Number(k))
        .filter((a) => isParamDirty(a))
        .map((a) => ({ addressLow: a, value: paramEdits.value[a] & 0xffff }))
    if (items.length === 0) {
        toast.info(t('leisaiConsole.noChanges'))
        return
    }
    isWritingParams.value = true
    try {
        const result = await store.api.batchWriteParameters(axisId.value, items)
        // 写入成功后同步到 values
        for (const item of result.items) {
            if (item.isSuccess) {
                paramValues.value[item.addressLow] = item.value
                delete paramErrors.value[item.addressLow]
            } else {
                paramErrors.value[item.addressLow] = item.errorMessage ?? t('leisaiConsole.readFailed')
            }
        }
        if (result.failureCount === 0) {
            toast.success(t('leisaiConsole.writeSuccess', { count: result.successCount, ms: result.elapsedMs }))
        } else {
            toast.warning(
                t('leisaiConsole.writePartialFail', { success: result.successCount, fail: result.failureCount })
            )
        }
    } catch (err) {
        toast.error(t('leisaiConsole.writeFailed', { err: String(err) }))
    } finally {
        isWritingParams.value = false
    }
}

/** 将当前分组恢复为手册默认值（仅有 DefaultValue 的项目）。 */
async function resetCurrentGroupToDefault(): Promise<void> {
    if (isWritingParams.value) return
    const addrs = currentGroupParams.value
        .filter((p) => p.defaultValue !== null && p.defaultValue !== undefined)
        .map((p) => p.addressLow)
    if (addrs.length === 0) {
        toast.warning(t('leisaiConsole.noDefaultValue'))
        return
    }
    if (!(await confirmAction({ message: t('leisaiConsole.resetConfirm', { count: addrs.length }) }))) {
        return
    }
    isWritingParams.value = true
    try {
        const result = await store.api.resetParametersToDefault(axisId.value, addrs)
        for (const item of result.items) {
            if (item.isSuccess) {
                paramValues.value[item.addressLow] = item.value
                paramEdits.value[item.addressLow] = item.value
                delete paramErrors.value[item.addressLow]
            } else {
                paramErrors.value[item.addressLow] = item.errorMessage ?? t('leisaiConsole.readFailed')
            }
        }
        toast.success(
            t('leisaiConsole.resetSuccess', {
                success: result.successCount,
                fail: result.failureCount,
                ms: result.elapsedMs,
            })
        )
    } catch (err) {
        toast.error(t('leisaiConsole.resetFailed', { err: String(err) }))
    } finally {
        isWritingParams.value = false
    }
}

/** 导出当前已读取的全部参数为 JSON 文件。 */
function exportParamConfig(): void {
    const payload = {
        axisId: axisId.value,
        exportedAt: new Date().toISOString(),
        values: Object.fromEntries(
            Object.entries(paramValues.value).map(([k, v]) => [`0x${Number(k).toString(16).padStart(4, '0')}`, v])
        ),
    }
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `leisai-params-${axisId.value.slice(0, 8)}-${Date.now()}.json`
    a.click()
    URL.revokeObjectURL(url)
    toast.success(t('leisaiConsole.exportSuccess'))
}

/** 从 JSON 文件导入（仅填入 edit 槽，不自动写入设备）。 */
function importParamConfig(ev: Event): void {
    const input = ev.target as HTMLInputElement
    const file = input.files?.[0]
    if (!file) return
    const reader = new FileReader()
    reader.onload = () => {
        try {
            const text = String(reader.result ?? '')
            const json = JSON.parse(text) as { values?: Record<string, number> }
            if (!json.values || typeof json.values !== 'object') {
                toast.error(t('leisaiConsole.importInvalidJson'))
                return
            }
            let count = 0
            for (const [addrStr, v] of Object.entries(json.values)) {
                const addr = addrStr.startsWith('0x') ? parseInt(addrStr, 16) : Number(addrStr)
                if (!Number.isFinite(addr) || !Number.isFinite(Number(v))) continue
                paramEdits.value[addr] = Number(v) & 0xffff
                count++
            }
            toast.success(t('leisaiConsole.importSuccess', { count }))
        } catch (err) {
            toast.error(t('leisaiConsole.importFailed', { err: String(err) }))
        } finally {
            input.value = ''
        }
    }
    reader.readAsText(file)
}

/** 监听 Tab 切换：首次进入参数页加载元数据；切回图表页时刷新图表尺寸。 */
watch(activeTab, async (tab) => {
    if (tab === 'chart') {
        await nextTick()
        chart1?.resize()
        chart2?.resize()
    }
    if (tab === 'params') {
        await ensureMetadataLoaded()
    }
})

// ───── ECharts 图表 ──────────────────────────────────────────────────────────

/** 图表1容器：伺服运行状态综合分析（双Y轴时序图）。 */
const chart1El = ref<HTMLDivElement | null>(null)
/** 图表2容器：位置-速度相图（颜色映射散点图）。 */
const chart2El = ref<HTMLDivElement | null>(null)
/** ECharts 实例。 */
let chart1: echarts.ECharts | null = null
let chart2: echarts.ECharts | null = null
/** 图表更新帧句柄：用于合并高频推送下的重复刷新。 */
let chartUpdateRafId: number | null = null

/** 构建图表1（伺服运行状态综合分析）的 ECharts option。 */
function buildChart1Option() {
    const traceData = trace.value
    const n = traceData.actualPositions.length
    // 跟随误差 = 指令位置 - 实际位置（客户端计算）
    const followErrors = traceData.commandPositions.map((cmd, i) => cmd - traceData.actualPositions[i])
    const isDark = themeStore.isDark
    const axisColor = isDark ? '#9ca3af' : '#6b7280'
    const gridColor = isDark ? '#374151' : '#e5e7eb'
    return {
        backgroundColor: 'transparent',
        textStyle: { fontSize: 11, color: isDark ? '#e5e7eb' : '#374151' },
        tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
        legend: {
            top: 0,
            right: 0,
            selectedMode: false,
            textStyle: { fontSize: 11, color: isDark ? '#e5e7eb' : '#374151' },
        },
        grid: { top: 28, bottom: 8, left: 8, right: 8, containLabel: true },
        xAxis: {
            type: 'category',
            data: Array.from({ length: n }, (_, i) => i),
            axisLabel: { fontSize: 10, color: axisColor },
            axisLine: { lineStyle: { color: axisColor } },
            splitLine: { show: false },
        },
        yAxis: [
            {
                // 左轴：位置（脉冲）
                type: 'value',
                axisLabel: { fontSize: 10, color: axisColor },
                axisLine: { lineStyle: { color: axisColor } },
                splitLine: { lineStyle: { color: gridColor } },
            },
            {
                // 右轴：跟随误差
                type: 'value',
                axisLabel: { fontSize: 10, color: axisColor },
                axisLine: { lineStyle: { color: axisColor } },
                splitLine: { show: false },
            },
        ],
        series: [
            {
                name: t('leisaiConsole.chartSeriesCmdPos'),
                type: 'line',
                yAxisIndex: 0,
                data: traceData.commandPositions,
                showSymbol: false,
                lineStyle: { color: '#ef4444', width: 2.5 },
                itemStyle: { color: '#ef4444' },
            },
            {
                name: t('leisaiConsole.chartSeriesActPos'),
                type: 'line',
                yAxisIndex: 0,
                data: traceData.actualPositions,
                showSymbol: false,
                lineStyle: { color: '#3b82f6', width: 1.5 },
                itemStyle: { color: '#3b82f6' },
            },
            {
                name: t('leisaiConsole.chartSeriesFollowErr'),
                type: 'line',
                yAxisIndex: 1,
                data: followErrors,
                showSymbol: false,
                lineStyle: { color: '#a855f7', width: 2 },
                itemStyle: { color: '#a855f7' },
            },
        ],
    }
}

/** 构建图表2（位置-速度相图）的 ECharts option。 */
function buildChart2Option() {
    const traceData = trace.value
    const n = traceData.actualPositions.length
    // |跟随误差| 用于颜色映射
    const followErrors = traceData.commandPositions.map((cmd, i) => Math.abs(cmd - traceData.actualPositions[i]))
    const maxError = n > 0 ? Math.max(...followErrors, 1) : 1
    // 实际轨迹数据：[位置, 速度, |误差|]，第3维驱动 visualMap 颜色
    const actualData = traceData.actualPositions.map((pos, i) => [pos, traceData.speeds[i], followErrors[i]])
    // 指令轨迹数据：[位置, 速度]
    const commandData = traceData.commandPositions.map((pos, i) => [pos, traceData.speeds[i]])
    const isDark = themeStore.isDark
    const axisColor = isDark ? '#9ca3af' : '#6b7280'
    const gridColor = isDark ? '#374151' : '#e5e7eb'
    return {
        backgroundColor: 'transparent',
        textStyle: { fontSize: 11, color: isDark ? '#e5e7eb' : '#374151' },
        tooltip: { trigger: 'item' },
        legend: {
            top: 0,
            right: 64,
            selectedMode: false,
            textStyle: { fontSize: 11, color: isDark ? '#e5e7eb' : '#374151' },
        },
        visualMap: {
            min: 0,
            max: maxError,
            calculable: true,
            orient: 'vertical',
            right: 4,
            top: 'middle',
            itemHeight: 100,
            dimension: 2,
            text: [t('leisaiConsole.chartErrLarge'), t('leisaiConsole.chartErrSmall')],
            textStyle: { fontSize: 9, color: isDark ? '#e5e7eb' : '#374151' },
            inRange: { color: ['#3b82f6', '#fbbf24', '#ef4444'] },
            seriesIndex: 0,
        },
        grid: { top: 28, bottom: 8, left: 8, right: 64, containLabel: true },
        xAxis: {
            type: 'value',
            axisLabel: { fontSize: 10, color: axisColor },
            axisLine: { lineStyle: { color: axisColor } },
            splitLine: { lineStyle: { color: gridColor } },
        },
        yAxis: {
            type: 'value',
            axisLabel: { fontSize: 10, color: axisColor },
            axisLine: { lineStyle: { color: axisColor } },
            splitLine: { lineStyle: { color: gridColor } },
        },
        series: [
            {
                name: t('leisaiConsole.chartSeriesActTrace'),
                type: 'scatter',
                symbolSize: 4,
                data: actualData,
                itemStyle: { opacity: 0.8 },
            },
            {
                name: t('leisaiConsole.chartSeriesCmdTrace'),
                type: 'line',
                data: commandData,
                showSymbol: false,
                lineStyle: { color: '#ef4444', type: 'dashed', width: 2 },
                itemStyle: { color: '#ef4444' },
                markLine: {
                    silent: true,
                    symbol: 'none',
                    lineStyle: { color: isDark ? '#9ca3af' : '#374151', type: 'dashed', width: 1 },
                    label: { formatter: t('leisaiConsole.chartZeroSpeedLine'), fontSize: 10 },
                    data: [{ yAxis: 0 }],
                },
            },
        ],
    }
}

/** 初始化两个 ECharts 实例并渲染初始数据。 */
function initCharts(): void {
    const theme = themeStore.isDark ? 'dark' : undefined
    if (chart1El.value) {
        chart1?.dispose()
        chart1 = echarts.init(chart1El.value, theme, { renderer: 'canvas' })
        chart1.setOption(buildChart1Option())
    }
    if (chart2El.value) {
        chart2?.dispose()
        chart2 = echarts.init(chart2El.value, theme, { renderer: 'canvas' })
        chart2.setOption(buildChart2Option())
    }
}

/** 仅更新数据，不重建实例（保留用户的缩放/平移状态）。 */
function updateCharts(): void {
    chart1?.setOption(buildChart1Option())
    chart2?.setOption(buildChart2Option())
}

/** 合并一帧内的多次更新请求，避免高频推送挤占交互事件。 */
function scheduleChartsUpdate(): void {
    if (chartUpdateRafId !== null) {
        return
    }
    chartUpdateRafId = window.requestAnimationFrame(() => {
        chartUpdateRafId = null
        updateCharts()
    })
}

/** window resize 时刷新图表尺寸。 */
function handleChartsResize(): void {
    chart1?.resize()
    chart2?.resize()
}

/** 收到新轨迹推送时更新图表。 */
watch(trace, () => {
    scheduleChartsUpdate()
})

// ───── 生命周期 ─────────────────────────────────────────────────────
onMounted(async () => {
    await store.acquireHub()
    try {
        const samp = await store.api.getSamplingState(axisId.value)
        isSamplingEnabled.value = samp.isPollingEnabled
    } catch {
        /* 忽略 */
    }
    await loadIoConfig()
    // 页面加载后初始化 ECharts 图表（图表 Tab 默认选中，div 已在 DOM 中）
    await nextTick()
    initCharts()
    window.addEventListener('resize', handleChartsResize)
})

onBeforeUnmount(async () => {
    // 销毁 ECharts 实例，释放资源
    chart1?.dispose()
    chart2?.dispose()
    if (chartUpdateRafId !== null) {
        window.cancelAnimationFrame(chartUpdateRafId)
        chartUpdateRafId = null
    }
    window.removeEventListener('resize', handleChartsResize)
    await store.releaseHub()
})

watch(axisId, async (newId, oldId) => {
    if (newId && newId !== oldId) {
        await loadIoConfig()
        // 轴切换时重置并重新初始化图表
        await nextTick()
        initCharts()
    }
})

watch(
    () => themeStore.isDark,
    () => {
        // 主题变化时重新初始化图表以应用新颜色方案
        initCharts()
    }
)

function goBack(): void {
    router.back()
}

/** Tab 标签（响应式，切换语言时自动更新）。 */
const tabLabels = computed(() => ({
    chart: t('leisaiConsole.tabChart'),
    status: t('leisaiConsole.tabStatus'),
    io: t('leisaiConsole.tabIo'),
    fault: t('leisaiConsole.tabFault'),
    raw: t('leisaiConsole.tabRaw'),
    params: t('leisaiConsole.tabParams'),
}))
</script>

<template>
    <div class="p-4 space-y-4">
        <!-- 顶部：返回 + 标题 + Hub 状态 -->
        <div class="flex items-center gap-3">
            <Button severity="secondary" outlined size="small" @click="goBack">
                <ArrowLeft class="size-4" />
                {{ t('leisaiConsole.back') }}
            </Button>
            <div>
                <h1 class="text-lg font-semibold">{{ t('leisaiConsole.title') }}</h1>
                <div class="text-xs text-muted-foreground">
                    {{ t('leisaiConsole.axisId') }}
                    <span class="font-mono">{{ axisId }}</span>
                </div>
            </div>
            <span class="ml-auto text-xs" :class="store.hubConnected ? 'text-green-600' : 'text-red-500'">
                SignalR：{{ store.hubConnected ? t('leisaiConsole.hubConnected') : t('leisaiConsole.hubDisconnected') }}
            </span>
        </div>

        <!-- 实时状态摘要条 -->
        <AppCard :beam="false">
            <div class="p-3">
                <div class="mb-2 flex items-center gap-2">
                    <ToggleSwitch v-model="isSamplingEnabled" @change="toggleSampling" />
                    <span class="select-none text-xs">{{ t('leisaiConsole.realtimeSampling') }}</span>
                    <span
                        v-if="!isSamplingEnabled"
                        class="ml-1 rounded bg-yellow-100 px-1.5 text-xs text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300"
                    >
                        {{ t('leisaiConsole.paused') }}
                    </span>
                    <span class="ml-auto text-xs text-muted-foreground" v-if="liveState">
                        {{ t('leisaiConsole.responseTime', { ms: liveState.elapsedMs }) }}
                    </span>
                </div>

                <div v-if="!liveState" class="text-sm text-muted-foreground">{{ t('leisaiConsole.noStatePush') }}</div>
                <div v-else class="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4 lg:grid-cols-8">
                    <div>
                        <div class="text-muted-foreground">{{ t('leisaiConsole.fieldEnabled') }}</div>
                        <div :class="liveState.isEnabled ? 'text-green-600' : 'text-muted-foreground'">
                            {{ liveState.isEnabled ? 'ON' : 'OFF' }}
                        </div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('leisaiConsole.fieldRunning') }}</div>
                        <div :class="liveState.isRunning ? 'text-blue-600' : 'text-muted-foreground'">
                            {{
                                liveState.isRunning ? t('leisaiConsole.stateRunning') : t('leisaiConsole.stateStopped')
                            }}
                        </div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('leisaiConsole.fieldFault') }}</div>
                        <div :class="liveState.isFault ? 'text-red-600 font-semibold' : 'text-muted-foreground'">
                            {{ liveState.isFault ? t('common.yes') : t('common.no') }}
                        </div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('leisaiConsole.fieldHomeDone') }}</div>
                        <div>{{ liveState.isHomeDone ? t('common.yes') : t('common.no') }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('leisaiConsole.fieldCmdPos') }}</div>
                        <div class="font-mono">{{ liveState.commandPosition }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('leisaiConsole.fieldActPos') }}</div>
                        <div class="font-mono">{{ liveState.actualPosition }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">
                            {{ t('leisaiConsole.fieldSpeed') }} ({{ liveState.speedSource }})
                        </div>
                        <div class="font-mono">{{ liveState.effectiveSpeed }}</div>
                    </div>
                    <div>
                        <div class="text-muted-foreground">{{ t('leisaiConsole.fieldBusVoltage') }}</div>
                        <div class="font-mono">{{ liveState.busVoltageVolt.toFixed(1) }} V</div>
                    </div>
                </div>
            </div>
        </AppCard>

        <!-- Tab 切换条 -->
        <div class="flex flex-wrap gap-2 border-b">
            <Button unstyled type="button"
                v-for="[key, label] in Object.entries(tabLabels)"
                :key="key"
                :class="[
                    'px-4 py-2 text-sm border-b-2 transition-colors',
                    activeTab === key
                        ? 'border-primary text-primary font-medium'
                        : 'border-transparent text-muted-foreground hover:text-foreground',
                ]"
                @click="activeTab = key as TabKey"
            >
                {{ label }}
            </Button>
        </div>

        <!-- Tab: 图表（v-show 保留 DOM，防止切换 Tab 时图表状态被重置） -->
        <div v-show="activeTab === 'chart'" class="grid grid-cols-2 gap-4">
            <!-- 伺服运行状态综合分析：双Y轴时序图（位置/跟随误差） -->
            <AppCard :beam-size="120" :beam-duration="10">
                <div class="px-4 pt-3 text-base font-semibold">{{ t('leisaiConsole.chartTitle1') }}</div>
                <div class="p-3 pt-0">
                    <div ref="chart1El" style="height: 300px" />
                </div>
            </AppCard>
            <!-- 位置-速度相图：X=位置, Y=速度, 颜色=|跟随误差| -->
            <AppCard :beam-size="120" :beam-duration="10" :beam-delay="2">
                <div class="px-4 pt-3 text-base font-semibold">{{ t('leisaiConsole.chartTitle2') }}</div>
                <div class="p-3 pt-0">
                    <div ref="chart2El" style="height: 300px" />
                </div>
            </AppCard>
        </div>

        <!-- Tab: 状态 -->
        <AppCard v-if="activeTab === 'status'" :beam="false">
            <div class="p-3 space-y-3">
                <div v-if="!liveState" class="text-sm text-muted-foreground">{{ t('leisaiConsole.noStatePush') }}</div>
                <template v-else>
                    <div class="grid grid-cols-2 gap-2 text-xs sm:grid-cols-3">
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.statusWord') }}</div>
                            <div class="font-mono">0x{{ liveState.statusWord.toString(16).padStart(4, '0') }}</div>
                        </div>
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.triggerWord') }}</div>
                            <div class="font-mono">0x{{ liveState.triggerWord.toString(16).padStart(4, '0') }}</div>
                            <div class="text-[10px] text-muted-foreground">{{ liveState.triggerMode }}</div>
                        </div>
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.diBitmap') }}</div>
                            <div class="font-mono">0x{{ liveState.inputIoBitmap.toString(16).padStart(4, '0') }}</div>
                        </div>
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.doBitmap') }}</div>
                            <div class="font-mono">0x{{ liveState.outputIoBitmap.toString(16).padStart(4, '0') }}</div>
                        </div>
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.slaveId') }}</div>
                            <div class="font-mono">{{ liveState.slaveId }}</div>
                        </div>
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.timestamp') }}</div>
                            <div class="font-mono">{{ new Date(liveState.timestampMs).toLocaleString() }}</div>
                        </div>
                    </div>
                    <div class="grid grid-cols-3 gap-2 text-xs sm:grid-cols-6">
                        <div
                            :class="[
                                'rounded border p-2 text-center',
                                liveState.isFault ? 'bg-red-100 dark:bg-red-900/40' : '',
                            ]"
                        >
                            {{ t('leisaiConsole.bitFault') }}
                            <br />
                            <span class="font-mono">{{ liveState.isFault ? '1' : '0' }}</span>
                        </div>
                        <div
                            :class="[
                                'rounded border p-2 text-center',
                                liveState.isEnabled ? 'bg-green-100 dark:bg-green-900/40' : '',
                            ]"
                        >
                            {{ t('leisaiConsole.bitEnabled') }}
                            <br />
                            <span class="font-mono">{{ liveState.isEnabled ? '1' : '0' }}</span>
                        </div>
                        <div
                            :class="[
                                'rounded border p-2 text-center',
                                liveState.isRunning ? 'bg-blue-100 dark:bg-blue-900/40' : '',
                            ]"
                        >
                            {{ t('leisaiConsole.bitRunning') }}
                            <br />
                            <span class="font-mono">{{ liveState.isRunning ? '1' : '0' }}</span>
                        </div>
                        <div class="rounded border p-2 text-center">
                            {{ t('leisaiConsole.bitCmdDone') }}
                            <br />
                            <span class="font-mono">{{ liveState.isCommandDone ? '1' : '0' }}</span>
                        </div>
                        <div class="rounded border p-2 text-center">
                            {{ t('leisaiConsole.bitPathDone') }}
                            <br />
                            <span class="font-mono">{{ liveState.isPathDone ? '1' : '0' }}</span>
                        </div>
                        <div class="rounded border p-2 text-center">
                            {{ t('leisaiConsole.bitHomeDone') }}
                            <br />
                            <span class="font-mono">{{ liveState.isHomeDone ? '1' : '0' }}</span>
                        </div>
                    </div>
                </template>
            </div>
        </AppCard>
        <!-- Tab: IO -->
        <AppCard v-if="activeTab === 'io'" :beam="false">
            <div class="p-3 space-y-4">
                <div class="flex items-center justify-between">
                    <h3 class="text-sm font-semibold">{{ t('leisaiConsole.diTitle') }}</h3>
                    <Button severity="secondary" outlined size="small" @click="loadIoConfig">
                        <RefreshCw class="size-3.5" />
                        {{ t('leisaiConsole.refreshConfig') }}
                    </Button>
                </div>
                <div class="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4 lg:grid-cols-7">
                    <div
                        v-for="i in 7"
                        :key="`di-${i}`"
                        :class="['rounded border p-2', diBit(i - 1) ? 'bg-green-100 dark:bg-green-900/40' : '']"
                    >
                        <div class="font-semibold">DI{{ i }}</div>
                        <div class="text-[11px] text-muted-foreground">{{ diLabel(i - 1) }}</div>
                        <div class="font-mono mt-1">{{ diBit(i - 1) ? 'ON' : 'OFF' }}</div>
                    </div>
                </div>

                <h3 class="text-sm font-semibold mt-2">{{ t('leisaiConsole.doTitle') }}</h3>
                <div class="grid grid-cols-1 gap-2 text-xs sm:grid-cols-3">
                    <div
                        v-for="i in 3"
                        :key="`do-${i}`"
                        :class="['rounded border p-2', doBit(i - 1) ? 'bg-green-100 dark:bg-green-900/40' : '']"
                    >
                        <div class="font-semibold">DO{{ i }}</div>
                        <div class="text-[11px] text-muted-foreground">{{ doLabel(i - 1) }}</div>
                        <div class="mt-1 flex items-center gap-2">
                            <span class="font-mono">{{ doBit(i - 1) ? 'ON' : 'OFF' }}</span>
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="acting"
                                @click="writeDo(i, true)"
                            >
                                {{ t('leisaiConsole.setOn') }}
                            </Button>
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="acting"
                                @click="writeDo(i, false)"
                            >
                                {{ t('leisaiConsole.setOff') }}
                            </Button>
                        </div>
                    </div>
                </div>
            </div>
        </AppCard>

        <!-- Tab: 故障 -->
        <AppCard v-if="activeTab === 'fault'" :beam="false">
            <div class="p-3 space-y-3">
                <div v-if="!liveState" class="text-sm text-muted-foreground">{{ t('leisaiConsole.noStatePush') }}</div>
                <template v-else>
                    <div class="grid grid-cols-2 gap-2 text-xs sm:grid-cols-3">
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.faultCodeCurrent') }}</div>
                            <div class="font-mono">
                                0x{{ liveState.currentFaultCode.toString(16).padStart(4, '0') }}
                            </div>
                        </div>
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.faultCodeWarning') }}</div>
                            <div class="font-mono">0x{{ liveState.prWarningCode.toString(16).padStart(4, '0') }}</div>
                        </div>
                        <div class="rounded border p-2">
                            <div class="text-muted-foreground">{{ t('leisaiConsole.faultStatus') }}</div>
                            <div :class="liveState.isFault ? 'text-red-600 font-semibold' : 'text-green-600'">
                                {{ liveState.isFault ? t('leisaiConsole.faultYes') : t('leisaiConsole.faultNo') }}
                            </div>
                        </div>
                    </div>
                </template>
                <div class="flex gap-2 pt-2">
                    <Button severity="secondary" outlined :disabled="acting" @click="clearFault('current')">
                        {{ t('leisaiConsole.clearCurrentFault') }}
                    </Button>
                    <Button severity="secondary" outlined :disabled="acting" @click="clearFault('all')">
                        {{ t('leisaiConsole.clearAllFaults') }}
                    </Button>
                    <Button :disabled="acting" @click="saveToEeprom">
                        <Save class="size-3.5" />
                        {{ t('leisaiConsole.saveToEeprom') }}
                    </Button>
                </div>
            </div>
        </AppCard>

        <!-- Tab: 手动 Modbus -->
        <AppCard v-if="activeTab === 'raw'" :beam="false">
            <div class="p-3 space-y-3">
                <div
                    class="flex items-start gap-2 rounded border border-yellow-300 bg-yellow-50 p-2 text-xs dark:border-yellow-700 dark:bg-yellow-900/20"
                >
                    <AlertTriangle class="size-4 text-yellow-700 mt-0.5" />
                    <div>
                        <div class="font-semibold text-yellow-800 dark:text-yellow-300">
                            {{ t('leisaiConsole.rawWarningTitle') }}
                        </div>
                        <div class="text-yellow-700 dark:text-yellow-400">
                            {{ t('leisaiConsole.rawWarningDesc') }}
                        </div>
                    </div>
                </div>

                <div class="grid grid-cols-2 gap-3 text-xs sm:grid-cols-4">
                    <label class="flex flex-col gap-1">
                        {{ t('leisaiConsole.rawOp') }}
                        <Select
                            v-model="rawForm.op"
                            :options="[
                                { label: t('leisaiConsole.rawOpRead'), value: 'read' },
                                { label: t('leisaiConsole.rawOpWrite'), value: 'write' },
                            ]"
                            option-label="label"
                            option-value="value"
                            size="small"
                            class="!text-xs"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                    </label>
                    <label class="flex flex-col gap-1" v-if="rawForm.op === 'write'">
                        {{ t('leisaiConsole.rawFc') }}
                        <Select
                            v-model.number="rawForm.fc"
                            :options="[
                                { label: t('leisaiConsole.rawFc06'), value: 6 },
                                { label: t('leisaiConsole.rawFc16'), value: 16 },
                            ]"
                            option-label="label"
                            option-value="value"
                            size="small"
                            class="!text-xs"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                    </label>
                    <label class="flex flex-col gap-1">
                        {{ t('leisaiConsole.rawStartAddr') }}
                        <InputNumber
                            v-model="rawForm.startAddress"
                            size="small"
                            class="!text-xs"
                            input-class="!font-mono !text-xs !h-7 !py-0"
                            :use-grouping="false"
                        />
                    </label>
                    <label class="flex flex-col gap-1" v-if="rawForm.op === 'read'">
                        {{ t('leisaiConsole.rawQuantity') }}
                        <InputNumber
                            v-model="rawForm.quantity"
                            size="small"
                            class="!text-xs"
                            input-class="!font-mono !text-xs !h-7 !py-0"
                            :min="1"
                            :max="125"
                            :use-grouping="false"
                        />
                    </label>
                    <label class="flex flex-col gap-1 sm:col-span-4" v-if="rawForm.op === 'write'">
                        {{ t('leisaiConsole.rawValues') }}
                        <InputText
                            v-model="rawForm.valuesText"
                            size="small"
                            class="!font-mono !text-xs !h-7 !py-0"
                            :placeholder="t('leisaiConsole.rawValuesPlaceholder')"
                        />
                    </label>
                </div>

                <div class="flex gap-2">
                    <Button :disabled="acting" @click="runRawCommand">
                        {{ t('leisaiConsole.rawExecute') }}
                    </Button>
                </div>

                <div v-if="lastRawResult" class="rounded border bg-muted/30 p-2 text-xs space-y-1">
                    <div>
                        {{ t('leisaiConsole.rawResult') }}
                        <span :class="lastRawResult.success ? 'text-green-600' : 'text-red-600'">
                            {{ lastRawResult.success ? t('leisaiConsole.rawSuccess') : t('leisaiConsole.rawFailed') }}
                        </span>
                        <span class="ml-2 text-muted-foreground">
                            {{ t('leisaiConsole.rawElapsed', { ms: lastRawResult.elapsedMs }) }}
                        </span>
                    </div>
                    <div v-if="lastRawResult.errorMessage" class="text-red-600">
                        {{ t('leisaiConsole.rawError') }}{{ lastRawResult.errorMessage }}
                    </div>
                    <div>
                        {{ t('leisaiConsole.rawRequest') }}
                        <span class="font-mono">{{ lastRawResult.rawRequest }}</span>
                    </div>
                    <div>
                        {{ t('leisaiConsole.rawResponse') }}
                        <span class="font-mono">{{ lastRawResult.rawResponse }}</span>
                    </div>
                    <div v-if="lastRawResult.parsedValues?.length">
                        {{ t('leisaiConsole.rawParsed') }}
                        <span class="font-mono">
                            [{{
                                lastRawResult.parsedValues
                                    .map((v) => '0x' + v.toString(16).padStart(4, '0'))
                                    .join(', ')
                            }}]
                        </span>
                    </div>
                </div>
            </div>
        </AppCard>

        <!-- Tab: 参数 (Phase 2) -->
        <AppCard v-if="activeTab === 'params'" :beam="false">
            <div class="p-3">
                <div
                    v-if="paramMetadata.length === 0 && isLoadingParams"
                    class="py-8 text-center text-sm text-muted-foreground"
                >
                    {{ t('leisaiConsole.paramsLoading') }}
                </div>
                <div v-else-if="paramMetadata.length === 0" class="py-8 text-center text-sm text-muted-foreground">
                    {{ t('leisaiConsole.paramsEmpty') }}
                </div>
                <div v-else class="grid gap-3" style="grid-template-columns: 200px 1fr">
                    <!-- 左侧：分组树 -->
                    <div class="rounded border bg-muted/20 p-2">
                        <div class="mb-2 text-xs font-semibold text-muted-foreground">
                            {{ t('leisaiConsole.paramGroups') }}
                        </div>
                        <ul class="space-y-1">
                            <li v-for="g in availableGroups" :key="g">
                                <Button unstyled type="button"
                                    class="flex w-full items-center justify-between rounded px-2 py-1 text-left text-xs hover:bg-muted"
                                    :class="currentGroup === g ? 'bg-primary/10 font-semibold text-primary' : ''"
                                    @click="currentGroup = g"
                                >
                                    <span>
                                        Pr{{ g }} ·
                                        {{ LeisaiParamGroupNames[g] ?? t('leisaiConsole.paramGroupUnnamed') }}
                                    </span>
                                    <span class="text-[10px] text-muted-foreground">
                                        {{ paramMetadata.filter((p) => p.group === g).length }}
                                    </span>
                                </Button>
                            </li>
                        </ul>
                    </div>

                    <!-- 右侧：参数表格 + 工具条 -->
                    <div class="flex min-w-0 flex-col gap-2">
                        <div class="flex flex-wrap items-center gap-2">
                            <div class="text-sm font-semibold">
                                Pr{{ currentGroup }} ·
                                {{ LeisaiParamGroupNames[currentGroup] ?? t('leisaiConsole.paramGroupUnnamed') }}
                            </div>
                            <span class="text-xs text-muted-foreground">
                                {{ t('leisaiConsole.paramCount', { count: currentGroupParams.length }) }}
                                <span :class="dirtyCount > 0 ? 'text-orange-600 font-semibold' : ''">
                                    {{ dirtyCount }}
                                </span>
                            </span>
                            <InputText
                                v-model="paramSearch"
                                size="small"
                                class="ml-auto !text-xs !h-7 !py-0 w-56"
                                :placeholder="t('leisaiConsole.paramSearchPlaceholder')"
                            />
                        </div>

                        <div class="max-h-[60vh] overflow-auto rounded border">
                            <table class="w-full border-collapse text-xs">
                                <thead class="sticky top-0 bg-muted text-left">
                                    <tr>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colAddress') }}</th>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colPr') }}</th>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colName') }}</th>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colUnit') }}</th>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colRange') }}</th>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colDefault') }}</th>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colCurrent') }}</th>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colEdit') }}</th>
                                        <th class="px-2 py-1.5 font-medium">{{ t('leisaiConsole.colActions') }}</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    <template v-for="p in currentGroupParams" :key="p.addressLow">
                                        <tr
                                            class="border-t hover:bg-muted/30"
                                            :class="
                                                isParamDirty(p.addressLow) ? 'bg-orange-100 dark:bg-orange-950/60' : ''
                                            "
                                        >
                                            <td class="px-2 py-1 font-mono">
                                                0x{{ p.addressLow.toString(16).padStart(4, '0') }}
                                            </td>
                                            <td class="px-2 py-1 font-mono">{{ p.pr }}</td>
                                            <td class="px-2 py-1">{{ p.name }}</td>
                                            <td class="px-2 py-1 text-muted-foreground">{{ p.unit || '-' }}</td>
                                            <td class="px-2 py-1 font-mono text-muted-foreground">
                                                <template v-if="p.rangeMin !== null && p.rangeMax !== null">
                                                    {{ p.rangeMin }}~{{ p.rangeMax }}
                                                </template>
                                                <template v-else>-</template>
                                            </td>
                                            <td class="px-2 py-1 font-mono text-muted-foreground">
                                                {{ p.defaultValue ?? '-' }}
                                            </td>
                                            <td class="px-2 py-1 font-mono">
                                                <span v-if="paramValues[p.addressLow] !== undefined">
                                                    {{ paramValues[p.addressLow] }}
                                                </span>
                                                <span v-else class="text-muted-foreground">
                                                    {{ t('leisaiConsole.valueNotRead') }}
                                                </span>
                                                <div v-if="paramErrors[p.addressLow]" class="text-[10px] text-red-600">
                                                    {{ paramErrors[p.addressLow] }}
                                                </div>
                                            </td>
                                            <td class="px-2 py-1">
                                                <InputNumber
                                                    v-model="paramEdits[p.addressLow]"
                                                    :min="p.rangeMin ?? undefined"
                                                    :max="p.rangeMax ?? undefined"
                                                    size="small"
                                                    input-class="!font-mono !text-xs !w-24 !h-6 !py-0"
                                                    :use-grouping="false"
                                                />
                                            </td>
                                            <td class="px-2 py-1">
                                                <Button unstyled type="button"
                                                    class="text-[10px] text-muted-foreground hover:text-foreground disabled:opacity-30"
                                                    :disabled="!isParamDirty(p.addressLow)"
                                                    @click="paramEdits[p.addressLow] = paramValues[p.addressLow]"
                                                    :title="t('leisaiConsole.undoChange')"
                                                >
                                                    {{ t('leisaiConsole.undoChange') }}
                                                </Button>
                                            </td>
                                        </tr>
                                        <tr
                                            v-if="p.description"
                                            :class="
                                                isParamDirty(p.addressLow) ? 'bg-orange-100 dark:bg-orange-950/60' : ''
                                            "
                                        >
                                            <td
                                                colspan="9"
                                                class="px-2 pb-1.5 pt-0 text-[10px] leading-relaxed text-muted-foreground"
                                            >
                                                {{ p.description }}
                                            </td>
                                        </tr>
                                    </template>
                                </tbody>
                            </table>
                        </div>

                        <!-- 底部批量操作 -->
                        <div class="flex flex-wrap gap-2 border-t pt-2">
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="isLoadingParams || isWritingParams"
                                @click="readCurrentGroupV1"
                            >
                                {{ t('leisaiConsole.btnReadGroupV1') }}
                            </Button>
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="isLoadingParams || isWritingParams"
                                @click="readCurrentGroupV2"
                            >
                                {{ t('leisaiConsole.btnReadGroupV2') }}
                            </Button>
                            <Button
                                size="small"
                                :disabled="isWritingParams || dirtyCount === 0"
                                @click="writeDirtyParams"
                            >
                                {{ t('leisaiConsole.btnWriteDirty', { count: dirtyCount }) }}
                            </Button>
                            <Button
                                size="small"
                                severity="secondary"
                                outlined
                                :disabled="isWritingParams"
                                @click="resetCurrentGroupToDefault"
                            >
                                {{ t('leisaiConsole.btnResetDefault') }}
                            </Button>
                            <Button size="small" severity="secondary" outlined :disabled="acting" @click="saveToEeprom">
                                {{ t('leisaiConsole.saveToEeprom') }}
                            </Button>
                            <div class="ml-auto flex gap-2">
                                <Button size="small" text severity="secondary" @click="exportParamConfig">
                                    {{ t('leisaiConsole.btnExportJson') }}
                                </Button>
                                <label
                                    class="inline-flex cursor-pointer items-center gap-1 rounded border px-3 py-1 text-xs hover:bg-muted"
                                >
                                    {{ t('leisaiConsole.btnImportJson') }}
                                    <input
                                        type="file"
                                        accept="application/json"
                                        class="hidden"
                                        @change="importParamConfig"
                                    />
                                </label>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </AppCard>
        <!-- 操作日志面板 -->
        <AppCard :beam="false">
            <div class="p-3">
                <div class="mb-2 flex items-center justify-between">
                    <span class="text-xs font-semibold text-muted-foreground">{{ t('leisaiConsole.logTitle') }}</span>
                    <Button
                        text
                        severity="secondary"
                        size="small"
                        class="h-6 px-2 text-xs"
                        @click="store.logs.splice(0)"
                    >
                        {{ t('leisaiConsole.logClear') }}
                    </Button>
                </div>
                <div v-if="store.logs.length === 0" class="py-3 text-center text-xs text-muted-foreground">
                    {{ t('leisaiConsole.logEmpty') }}
                </div>
                <div v-else class="max-h-40 overflow-y-auto space-y-0.5">
                    <div
                        v-for="(entry, i) in [...store.logs].reverse().slice(0, 100)"
                        :key="i"
                        class="flex items-start gap-2 text-xs"
                    >
                        <span class="shrink-0 font-mono text-[10px] text-muted-foreground">
                            {{ new Date(entry.time).toLocaleTimeString() }}
                        </span>
                        <span
                            class="shrink-0 w-10 text-center rounded px-1 text-[10px] font-semibold"
                            :class="{
                                'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300':
                                    entry.level === 'success',
                                'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300': entry.level === 'error',
                                'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300':
                                    entry.level === 'warn',
                                'bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400': entry.level === 'info',
                            }"
                        >
                            {{ entry.level }}
                        </span>
                        <span class="break-all">{{ entry.text }}</span>
                    </div>
                </div>
            </div>
        </AppCard>
    </div>
</template>
