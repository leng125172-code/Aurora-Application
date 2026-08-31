<script setup lang="ts">
import { computed } from 'vue'
import WorkflowJsonTree from '@/components/workflow/WorkflowJsonTree.vue'

type InspectionStatus = 'OK' | 'NG' | 'UNKNOWN' | 'ERROR'

interface InspectionResultValue {
    isValid?: boolean
    isOk?: boolean
    resultCode?: string
    message?: string
    reasons?: unknown
    details?: unknown
}

interface MeasurementDefinition {
    label: string
    unit?: string
}

interface MeasurementRow extends MeasurementDefinition {
    key: string
    value: number
}

const props = defineProps<{
    value: unknown
}>()

const FIELD_DEFINITIONS: Record<string, MeasurementDefinition> = {
    tiltX: { label: 'X 方向倾角', unit: '°' },
    tiltY: { label: 'Y 方向倾角', unit: '°' },
    totalTilt: { label: '合成倾角', unit: '°' },
    nominalTiltX: { label: 'X 标称角度', unit: '°' },
    nominalTiltY: { label: 'Y 标称角度', unit: '°' },
    deviationX: { label: 'X 方向偏差', unit: '°' },
    deviationY: { label: 'Y 方向偏差', unit: '°' },
    totalDeviation: { label: '合成偏差', unit: '°' },
    axisToNormalAngle: { label: '轴线与法向夹角', unit: '°' },
    axisToPlaneAngle: { label: '轴线与平面夹角', unit: '°' },
    axisAngle: { label: '两条轴线夹角', unit: '°' },
    angleDeviation: { label: '轴线夹角偏差', unit: '°' },
    twistZ: { label: '平面内旋转角', unit: '°' },
    twistDeviation: { label: '旋转角偏差', unit: '°' },
    referenceRmse: { label: '基准特征拟合 RMSE' },
    measuredRmse: { label: '安装特征拟合 RMSE' },
    planeRmse: { label: '平面拟合 RMSE' },
    axisRmse: { label: '轴线拟合 RMSE' },
    referencePointCount: { label: '基准面有效点数', unit: ' 点' },
    measuredPointCount: { label: '安装面有效点数', unit: ' 点' },
}

const result = computed<InspectionResultValue>(() =>
    isRecord(props.value) ? props.value : {},
)

const details = computed<Record<string, unknown>>(() =>
    isRecord(result.value.details) ? result.value.details : {},
)

const status = computed<InspectionStatus>(() => {
    const code = String(result.value.resultCode ?? details.value.status ?? '').toUpperCase()
    if (code === 'OK' || code === 'NG' || code === 'UNKNOWN' || code === 'ERROR') return code
    if (result.value.isValid === false) return 'UNKNOWN'
    return result.value.isOk === true ? 'OK' : 'NG'
})

const statusLabel = computed(() => ({
    OK: '合格',
    NG: '不合格',
    UNKNOWN: '无法判定',
    ERROR: '检测异常',
})[status.value])

const statusDescription = computed(() => ({
    OK: '所有启用的判定条件均在允许范围内',
    NG: '至少一项检测值超出允许范围',
    UNKNOWN: '数据质量不足，当前结果不能用于合格判定',
    ERROR: '检测过程发生异常，请检查工作流日志',
})[status.value])

const measurements = computed<MeasurementRow[]>(() =>
    Object.entries(FIELD_DEFINITIONS).flatMap(([key, definition]) => {
        const value = details.value[key]
        return typeof value === 'number' && Number.isFinite(value)
            ? [{ key, value, ...definition }]
            : []
    }),
)

const reasons = computed<string[]>(() => {
    const candidates = [result.value.reasons, details.value.qualityReasons, details.value.reasons]
    return Array.from(new Set(candidates.flatMap((value) =>
        Array.isArray(value)
            ? value.filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
            : [],
    ))).filter((reason) => !['OK', 'NG', 'UNKNOWN', 'ERROR'].includes(reason.toUpperCase()))
})

const toleranceSummary = computed<string[]>(() => {
    const tolerance = isRecord(details.value.tolerance) ? details.value.tolerance : undefined
    if (!tolerance) return []

    const lines: string[] = []
    appendRange(lines, 'X 偏差', tolerance.minDeviationX, tolerance.maxDeviationX, '°')
    appendRange(lines, 'Y 偏差', tolerance.minDeviationY, tolerance.maxDeviationY, '°')
    appendMaximum(lines, '合成偏差', tolerance.maxTotalDeviation, '°')
    appendMaximum(lines, '拟合 RMSE', tolerance.maxFitRmse)
    appendMinimum(lines, '有效点数', tolerance.minPointCount, ' 点')
    return lines
})

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(4).replace(/0+$/, '').replace(/\.$/, '')
}

function appendRange(
    target: string[],
    label: string,
    minimum: unknown,
    maximum: unknown,
    unit = '',
) {
    if (typeof minimum === 'number' && typeof maximum === 'number') {
        target.push(`${label} ${formatNumber(minimum)}～${formatNumber(maximum)}${unit}`)
    }
}

function appendMaximum(target: string[], label: string, maximum: unknown, unit = '') {
    if (typeof maximum === 'number') target.push(`${label} ≤ ${formatNumber(maximum)}${unit}`)
}

function appendMinimum(target: string[], label: string, minimum: unknown, unit = '') {
    if (typeof minimum === 'number') target.push(`${label} ≥ ${formatNumber(minimum)}${unit}`)
}
</script>

<template>
    <div class="min-w-[34rem] space-y-2 py-1 text-sm">
        <div
            class="flex items-center gap-3 rounded-md border px-3 py-2"
            :class="{
                'border-emerald-500/40 bg-emerald-500/10': status === 'OK',
                'border-red-500/40 bg-red-500/10': status === 'NG' || status === 'ERROR',
                'border-amber-500/40 bg-amber-500/10': status === 'UNKNOWN',
            }"
        >
            <span
                class="rounded px-2 py-0.5 font-semibold"
                :class="{
                    'bg-emerald-600 text-white': status === 'OK',
                    'bg-red-600 text-white': status === 'NG' || status === 'ERROR',
                    'bg-amber-500 text-white': status === 'UNKNOWN',
                }"
            >{{ status }} · {{ statusLabel }}</span>
            <span class="text-muted-foreground">{{ statusDescription }}</span>
        </div>

        <div v-if="measurements.length" class="grid grid-cols-2 gap-x-5 gap-y-1 rounded-md bg-muted/40 px-3 py-2">
            <div v-for="item in measurements" :key="item.key" class="flex justify-between gap-4">
                <span class="text-muted-foreground">{{ item.label }}</span>
                <span class="font-mono font-medium">{{ formatNumber(item.value) }}{{ item.unit }}</span>
            </div>
        </div>

        <div v-if="reasons.length" class="rounded-md border border-red-500/30 bg-red-500/5 px-3 py-2">
            <div class="mb-1 font-medium text-red-700 dark:text-red-400">判定依据</div>
            <ul class="list-disc space-y-0.5 pl-5">
                <li v-for="reason in reasons" :key="reason">{{ reason }}</li>
            </ul>
        </div>

        <div v-if="toleranceSummary.length" class="text-xs text-muted-foreground">
            允许范围：{{ toleranceSummary.join('；') }}
        </div>

        <details class="text-xs text-muted-foreground">
            <summary class="cursor-pointer select-none">查看原始结果</summary>
            <div class="mt-1 max-h-56 overflow-auto rounded border bg-background p-2 text-foreground">
                <WorkflowJsonTree :value="value" />
            </div>
        </details>
    </div>
</template>
