<script setup lang="ts">
/**
 * Hangfire 任务仪表盘，含5个子页：仪表盘/作业/重试/周期性作业/服务器。
 * 通过 route.query.tab 切换子页，SignalR 实时推送仪表盘统计数字。
 * API 数据来源：/api/hangfire/* (见 HangfireRouteActionProvider)
 */
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import * as signalR from '@microsoft/signalr'
import * as echarts from 'echarts'
import { RefreshCw, PlayCircle, Trash2, RotateCcw } from '@lucide/vue'
import Button from 'primevue/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Badge } from '@/components/ui/badge'
import { AppCard } from '@/components/primevue'
import { useAppToast } from '@/composables/useAppToast'
import { httpClient } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import { useThemeStore } from '@/stores/theme'

const { t } = useI18n()
const route = useRoute()
const authStore = useAuthStore()
const themeStore = useThemeStore()
const toast = useAppToast()

const activeTab = computed(() => (route.query.tab as string) || 'dashboard')

// ── 类型 ────────────────────────────────────────────────────────────────────
interface HangfireStats {
    enqueued?: number
    scheduled?: number
    processing?: number
    succeeded?: number
    failed?: number
    deleted?: number
    recurring?: number
    servers?: number
    queues?: number
    fetched?: number
}
// daily history: { succeeded: {"2026-05-12": 5}, failed: {...} }
interface DailyHistory {
    succeeded?: Record<string, number>
    failed?: Record<string, number>
}
// Hangfire job item: Key=jobId, Value=dto (may be Key/Value or key/value)
interface JobItem {
    Key?: string
    Value?: Record<string, unknown>
    key?: string
    value?: Record<string, unknown>
}
interface RecurringJob {
    Id?: string
    id?: string
    Cron?: string
    cron?: string
    NextExecution?: string
    nextExecution?: string
    LastExecution?: string
    lastExecution?: string
    JobType?: string
    Queue?: string
}
interface ServerItem {
    Name?: string
    name?: string
    WorkersCount?: number
    workersCount?: number
    Queues?: string[]
    queues?: string[]
    StartedAt?: string
    startedAt?: string
    Heartbeat?: string
    heartbeat?: string
}

// ── 状态 ────────────────────────────────────────────────────────────────────
const stats = ref<HangfireStats>({})
const loading = ref(false)
const dailyHistory = ref<DailyHistory>({})
const jobList = ref<JobItem[]>([])
const recurringJobs = ref<RecurringJob[]>([])
const servers = ref<ServerItem[]>([])
const jobState = ref<string>('enqueued')

// ── 仪表盘 ───────────────────────────────────────────────────────────────────
async function loadStats(): Promise<void> {
    loading.value = true
    try {
        const [statsRes, dailyRes] = await Promise.all([
            httpClient.get<HangfireStats>('/api/hangfire/stats'),
            httpClient.get<DailyHistory>('/api/hangfire/stats/history/daily'),
        ])
        stats.value = statsRes.data ?? {}
        dailyHistory.value = dailyRes.data ?? {}
        updateChart()
    } catch {
        /* 错误已在拦截器处理 */
    } finally {
        loading.value = false
    }
}

const trendChartEl = ref<HTMLDivElement | null>(null)
let trendChart: echarts.ECharts | null = null

/** 从 dailyHistory 提取排序后的日期、成功数、失败数 */
function buildTrendData(): { dates: string[]; succeeded: number[]; failed: number[] } {
    const s = dailyHistory.value.succeeded ?? {}
    const f = dailyHistory.value.failed ?? {}
    const allDates = Array.from(new Set([...Object.keys(s), ...Object.keys(f)])).sort()
    return {
        dates: allDates.map((d) => d.substring(0, 10)),
        succeeded: allDates.map((d) => s[d] ?? 0),
        failed: allDates.map((d) => f[d] ?? 0),
    }
}

/** 构建趋势折线图 ECharts option。 */
function buildTrendOption() {
    const { dates, succeeded, failed } = buildTrendData()
    const isDark = themeStore.isDark
    return {
        backgroundColor: 'transparent',
        textStyle: { color: isDark ? '#e5e7eb' : '#374151', fontSize: 11 },
        tooltip: { trigger: 'axis' },
        legend: {
            top: 0,
            right: 0,
            textStyle: { color: isDark ? '#e5e7eb' : '#374151', fontSize: 11 },
        },
        grid: { top: 36, bottom: 36, left: 50, right: 12, containLabel: false },
        xAxis: {
            type: 'category',
            data: dates,
            axisLabel: { fontSize: 10 },
            splitLine: { show: false },
        },
        yAxis: {
            type: 'value',
            minInterval: 1,
            axisLabel: { fontSize: 10 },
            splitLine: { lineStyle: { color: isDark ? '#374151' : '#e5e7eb' } },
        },
        series: [
            {
                name: t('hangfire.succeeded'),
                type: 'line',
                smooth: true,
                showSymbol: false,
                data: succeeded,
                lineStyle: { color: 'rgb(34,197,94)', width: 1.5 },
                itemStyle: { color: 'rgb(34,197,94)' },
                areaStyle: { color: 'rgba(34,197,94,0.12)' },
            },
            {
                name: t('hangfire.failed'),
                type: 'line',
                smooth: true,
                showSymbol: false,
                data: failed,
                lineStyle: { color: 'rgb(239,68,68)', width: 1.5 },
                itemStyle: { color: 'rgb(239,68,68)' },
                areaStyle: { color: 'rgba(239,68,68,0.10)' },
            },
        ],
    }
}

/** 初始化趋势折线图 ECharts 实例。 */
function initTrendChart(): void {
    if (!trendChartEl.value) return
    trendChart?.dispose()
    trendChart = echarts.init(trendChartEl.value, themeStore.isDark ? 'dark' : undefined, { renderer: 'canvas' })
    trendChart.setOption(buildTrendOption())
}

/** 仅更新图表数据，不重建实例。 */
function updateChart(): void {
    trendChart?.setOption(buildTrendOption())
}

// ── 作业列表 ─────────────────────────────────────────────────────────────────
const JOB_STATES = ['enqueued', 'scheduled', 'processing', 'succeeded', 'failed', 'deleted'] as const

async function loadJobs(state = jobState.value): Promise<void> {
    jobState.value = state
    loading.value = true
    try {
        const res = await httpClient.get<unknown>(`/api/hangfire/jobs/${state}?from=0&perPage=50`)
        const raw = res.data
        // Hangfire 返回 List<KeyValuePair<string,T>>，JSON 序列化为 [{Key,Value}]
        if (Array.isArray(raw)) {
            jobList.value = raw as JobItem[]
        } else {
            jobList.value = []
        }
    } catch {
        /* 错误已在拦截器处理 */
    } finally {
        loading.value = false
    }
}

// ── 重试 ─────────────────────────────────────────────────────────────────────
async function loadRetries(): Promise<void> {
    await loadJobs('failed')
}

async function requeueJob(jobId: string): Promise<void> {
    try {
        await httpClient.post(`/api/hangfire/jobs/${encodeURIComponent(jobId)}/requeue`, {})
        toast.success(t('hangfire.triggered'))
        await loadJobs(jobState.value)
    } catch {
        /* 错误已在拦截器处理 */
    }
}

async function deleteJob(jobId: string): Promise<void> {
    if (!confirm(t('management.confirmDelete', { name: jobId }))) return
    try {
        await httpClient.delete(`/api/hangfire/jobs/${encodeURIComponent(jobId)}`)
        toast.success(t('common.success'))
        await loadJobs(jobState.value)
    } catch {
        /* 错误已在拦截器处理 */
    }
}

// ── 周期性作业 ───────────────────────────────────────────────────────────────
async function loadRecurring(): Promise<void> {
    loading.value = true
    try {
        const res = await httpClient.get<RecurringJob[]>('/api/hangfire/recurring-jobs')
        recurringJobs.value = Array.isArray(res.data) ? res.data : []
    } catch {
        /* 错误已在拦截器处理 */
    } finally {
        loading.value = false
    }
}

async function triggerJob(id: string): Promise<void> {
    try {
        await httpClient.post(`/api/hangfire/recurring-jobs/${encodeURIComponent(id)}/trigger`, {})
        toast.success(t('hangfire.triggered'))
    } catch {
        /* 错误已在拦截器处理 */
    }
}

async function deleteRecurringJob(id: string): Promise<void> {
    if (!confirm(t('management.confirmDelete', { name: id }))) return
    try {
        await httpClient.delete(`/api/hangfire/recurring-jobs/${encodeURIComponent(id)}`)
        toast.success(t('common.success'))
        await loadRecurring()
    } catch {
        /* 错误已在拦截器处理 */
    }
}

// ── 服务器 ───────────────────────────────────────────────────────────────────
async function loadServers(): Promise<void> {
    loading.value = true
    try {
        const res = await httpClient.get<ServerItem[]>('/api/hangfire/servers')
        servers.value = Array.isArray(res.data) ? res.data : []
    } catch {
        /* 错误已在拦截器处理 */
    } finally {
        loading.value = false
    }
}

// ── 根据 tab 加载 ─────────────────────────────────────────────────────────────
function loadCurrentTab(): void {
    switch (activeTab.value) {
        case 'dashboard':
            void loadStats()
            break
        case 'jobs':
            void loadJobs(jobState.value)
            break
        case 'retries':
            void loadRetries()
            break
        case 'recurring':
            void loadRecurring()
            break
        case 'servers':
            void loadServers()
            break
    }
}

watch(activeTab, async (tab) => {
    loadCurrentTab()
    if (tab === 'dashboard') {
        // v-if 切回仪表盘时 div 已重新创建，需重新初始化图表
        await nextTick()
        initTrendChart()
    }
})

watch(
    () => themeStore.isDark,
    () => {
        // 主题变化时重新初始化图表以应用新颜色方案
        initTrendChart()
    }
)

// ── SignalR ──────────────────────────────────────────────────────────────────
let connection: signalR.HubConnection | null = null

async function startSignalR(): Promise<void> {
    connection = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/dashboard', { accessTokenFactory: () => authStore.token ?? '' })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build()
    connection.on('ReceiveHangfireStats', (data: HangfireStats) => {
        stats.value = data
    })
    // 注册占位处理器，避免 Hub 广播其他事件时产生 Warning
    connection.on('ReceiveCapStats', () => {
        /* HangfirePage 不处理 */
    })
    connection.on('ReceiveSystemMetrics', () => {
        /* HangfirePage 不处理 */
    })
    try {
        await connection.start()
    } catch (err) {
        console.warn('[DashboardHub] 连接失败，将依赖初始加载数据', err)
    }
}

// ── 工具 ─────────────────────────────────────────────────────────────────────
function formatTime(iso?: string): string {
    if (!iso) return '-'
    try {
        return new Date(iso).toLocaleString()
    } catch {
        return iso
    }
}

// Hangfire .NET JSON 可能是大写 Key/Value 或小写 key/value
function jobId(item: JobItem): string {
    return item.Key ?? item.key ?? ''
}
function jobVal(item: JobItem): Record<string, unknown> {
    return item.Value ?? item.value ?? {}
}
function jobLabel(item: JobItem): string {
    const v = jobVal(item)
    // Job 属性：{ Job: { Type, Method, Args } } 或 { job: { type, method } }
    const j = (v['Job'] ?? v['job']) as Record<string, unknown> | undefined
    if (j) {
        const method = (j['Method'] ?? j['method']) as string | undefined
        if (method) return method
    }
    return jobId(item) || '-'
}
function jobTime(item: JobItem): string {
    const v = jobVal(item)
    const t = (v['EnqueuedAt'] ??
        v['enqueuedAt'] ??
        v['ScheduledAt'] ??
        v['scheduledAt'] ??
        v['StartedAt'] ??
        v['startedAt'] ??
        v['FailedAt'] ??
        v['failedAt'] ??
        v['SucceededAt'] ??
        v['succeededAt']) as string | undefined
    return formatTime(t)
}

function stateLabel(s: string): string {
    const map: Record<string, string> = {
        enqueued: t('hangfire.stateEnqueued'),
        scheduled: t('hangfire.stateScheduled'),
        processing: t('hangfire.stateProcessing'),
        succeeded: t('hangfire.stateSucceeded'),
        failed: t('hangfire.stateFailed'),
        deleted: t('hangfire.deleted'),
    }
    return map[s] ?? s
}

// RecurringJob 大小写字段兼容
function rId(j: RecurringJob): string {
    return j.Id ?? j.id ?? ''
}
function rCron(j: RecurringJob): string {
    return j.Cron ?? j.cron ?? ''
}
function rNext(j: RecurringJob): string {
    return j.NextExecution ?? j.nextExecution ?? ''
}
function rLast(j: RecurringJob): string {
    return j.LastExecution ?? j.lastExecution ?? ''
}

// ServerItem 大小写字段兼容
function sName(s: ServerItem): string {
    return s.Name ?? s.name ?? ''
}
function sWorkers(s: ServerItem): number {
    return s.WorkersCount ?? s.workersCount ?? 0
}
function sQueues(s: ServerItem): string {
    return (s.Queues ?? s.queues ?? []).join(', ') || '-'
}
function sStarted(s: ServerItem): string {
    return formatTime(s.StartedAt ?? s.startedAt)
}
function sHeartbeat(s: ServerItem): string {
    return formatTime(s.Heartbeat ?? s.heartbeat)
}

const STAT_CARDS = [
    { key: 'enqueued', color: 'text-yellow-500' },
    { key: 'scheduled', color: 'text-blue-400' },
    { key: 'processing', color: 'text-purple-500' },
    { key: 'succeeded', color: 'text-green-500' },
    { key: 'failed', color: 'text-red-500' },
    { key: 'deleted', color: 'text-muted-foreground' },
    { key: 'recurring', color: 'text-cyan-500' },
    { key: 'servers', color: 'text-indigo-500' },
] as const

onMounted(async () => {
    loadCurrentTab()
    await startSignalR()
    // 初始 tab 是仪表盘时初始化趋势图
    if (activeTab.value === 'dashboard') {
        await nextTick()
        initTrendChart()
    }
})
onUnmounted(async () => {
    trendChart?.dispose()
    if (connection) {
        connection.off('ReceiveHangfireStats')
        await connection.stop()
        connection = null
    }
})
</script>

<template>
    <div class="space-y-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.hangfire') }}</h1>
            <Button severity="secondary" outlined :disabled="loading" @click="loadCurrentTab">
                <RefreshCw :class="['size-4', loading && 'animate-spin']" />
            </Button>
        </div>

        <!-- ── 仪表盘 ── -->
        <template v-if="activeTab === 'dashboard'">
            <div class="grid gap-3 md:grid-cols-4 lg:grid-cols-8">
                <AppCard v-for="c in STAT_CARDS" :key="c.key" :beam="false">
                    <div class="p-4 space-y-1">
                        <div class="text-xs">{{ t(`hangfire.${c.key}`) }}</div>
                        <div :class="['text-2xl font-semibold', c.color]">{{ stats[c.key] ?? '-' }}</div>
                    </div>
                </AppCard>
            </div>
            <AppCard :beam-size="120" :beam-duration="10">
                <div class="p-4 space-y-3">
                    <div>
                        <div class="text-base font-semibold">{{ t('hangfire.dailyTrend') }}</div>
                        <div class="text-sm text-muted-foreground mt-1">{{ t('hangfire.dailyTrendDesc') }}</div>
                    </div>
                    <div ref="trendChartEl" style="height: 280px; width: 100%" />
                </div>
            </AppCard>
        </template>

        <!-- ── 作业 ── -->
        <template v-else-if="activeTab === 'jobs'">
            <div class="flex flex-wrap gap-2">
                <Button
                    v-for="s in JOB_STATES"
                    :key="s"
                    :severity="jobState === s ? 'primary' : 'secondary'"
                    :outlined="jobState !== s"
                    size="small"
                    @click="loadJobs(s)"
                >
                    {{ stateLabel(s) }}
                </Button>
            </div>
            <AppCard :beam="false">
                <div>
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>{{ t('hangfire.jobId') }}</TableHead>
                                <TableHead>{{ t('hangfire.jobName') }}</TableHead>
                                <TableHead class="whitespace-nowrap">{{ t('hangfire.jobCreated') }}</TableHead>
                                <TableHead class="text-right whitespace-nowrap">{{ t('common.action') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!jobList.length">
                                <TableCell colspan="4" class="py-8 text-center text-muted-foreground">
                                    {{ t('hangfire.noJobs') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="job in jobList" :key="jobId(job)">
                                <TableCell class="font-mono text-xs whitespace-nowrap">{{ jobId(job) }}</TableCell>
                                <TableCell>
                                    <div class="max-w-[320px] truncate font-mono text-sm" :title="jobLabel(job)">
                                        {{ jobLabel(job) }}
                                    </div>
                                </TableCell>
                                <TableCell class="text-xs text-muted-foreground whitespace-nowrap">
                                    {{ jobTime(job) }}
                                </TableCell>
                                <TableCell class="text-right">
                                    <div class="flex justify-end gap-1">
                                        <Button
                                            v-if="jobId(job)"
                                            text
                                            severity="secondary"
                                            :title="t('hangfire.retryJob')"
                                            @click="requeueJob(jobId(job))"
                                        >
                                            <RotateCcw class="size-4 text-blue-500" />
                                        </Button>
                                        <Button
                                            v-if="jobId(job)"
                                            text
                                            severity="danger"
                                            :title="t('common.delete')"
                                            @click="deleteJob(jobId(job))"
                                        >
                                            <Trash2 class="size-4" />
                                        </Button>
                                    </div>
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </div>
            </AppCard>
        </template>

        <!-- ── 重试（失败作业）── -->
        <template v-else-if="activeTab === 'retries'">
            <AppCard :beam="false">
                <div class="p-4 pb-2">
                    <div class="text-base font-semibold">{{ t('hangfire.tabRetries') }}</div>
                </div>
                <div>
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>{{ t('hangfire.jobId') }}</TableHead>
                                <TableHead>{{ t('hangfire.jobName') }}</TableHead>
                                <TableHead>{{ t('hangfire.errorMessage') }}</TableHead>
                                <TableHead class="text-right whitespace-nowrap">{{ t('common.action') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!jobList.length">
                                <TableCell colspan="4" class="py-8 text-center text-muted-foreground">
                                    {{ t('hangfire.noJobs') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="job in jobList" :key="jobId(job)">
                                <TableCell class="font-mono text-xs whitespace-nowrap">{{ jobId(job) }}</TableCell>
                                <TableCell>
                                    <div class="max-w-[320px] truncate font-mono text-sm" :title="jobLabel(job)">
                                        {{ jobLabel(job) }}
                                    </div>
                                </TableCell>
                                <TableCell>
                                    <div
                                        class="max-w-xs truncate text-xs text-destructive"
                                        :title="(jobVal(job)['Reason'] ?? jobVal(job)['reason'] ?? '-') as string"
                                    >
                                        {{ (jobVal(job)['Reason'] ?? jobVal(job)['reason'] ?? '-') as string }}
                                    </div>
                                </TableCell>
                                <TableCell class="text-right">
                                    <Button
                                        v-if="jobId(job)"
                                        text
                                        severity="secondary"
                                        :title="t('hangfire.retryJob')"
                                        @click="requeueJob(jobId(job))"
                                    >
                                        <RotateCcw class="size-4 text-blue-500" />
                                    </Button>
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </div>
            </AppCard>
        </template>

        <!-- ── 周期性作业 ── -->
        <template v-else-if="activeTab === 'recurring'">
            <AppCard :beam="false">
                <div class="p-4 pb-2">
                    <div class="text-base font-semibold">{{ t('hangfire.tabRecurring') }}</div>
                </div>
                <div>
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>ID</TableHead>
                                <TableHead>{{ t('hangfire.cron') }}</TableHead>
                                <TableHead>{{ t('hangfire.nextExecution') }}</TableHead>
                                <TableHead>{{ t('hangfire.lastExecution') }}</TableHead>
                                <TableHead class="text-right">{{ t('common.action') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!recurringJobs.length">
                                <TableCell colspan="5" class="py-6 text-center text-muted-foreground">
                                    {{ t('management.noData') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="job in recurringJobs" :key="rId(job)">
                                <TableCell>
                                    <div class="max-w-[200px] truncate font-mono text-sm" :title="rId(job)">
                                        {{ rId(job) }}
                                    </div>
                                </TableCell>
                                <TableCell>
                                    <Badge variant="secondary" class="font-mono text-xs">{{ rCron(job) }}</Badge>
                                </TableCell>
                                <TableCell class="text-xs text-muted-foreground">
                                    {{ formatTime(rNext(job)) }}
                                </TableCell>
                                <TableCell class="text-xs text-muted-foreground">
                                    {{ formatTime(rLast(job)) }}
                                </TableCell>
                                <TableCell class="text-right">
                                    <div class="flex justify-end gap-1">
                                        <Button
                                            v-if="rId(job)"
                                            text
                                            severity="secondary"
                                            :title="t('hangfire.triggerNow')"
                                            @click="triggerJob(rId(job))"
                                        >
                                            <PlayCircle class="size-4 text-green-500" />
                                        </Button>
                                        <Button
                                            v-if="rId(job)"
                                            text
                                            severity="danger"
                                            :title="t('common.delete')"
                                            @click="deleteRecurringJob(rId(job))"
                                        >
                                            <Trash2 class="size-4" />
                                        </Button>
                                    </div>
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </div>
            </AppCard>
        </template>

        <!-- ── 服务器 ── -->
        <template v-else-if="activeTab === 'servers'">
            <AppCard :beam="false">
                <div class="p-4 pb-2">
                    <div class="text-base font-semibold">{{ t('hangfire.tabServers') }}</div>
                </div>
                <div>
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>{{ t('hangfire.serverName') }}</TableHead>
                                <TableHead>{{ t('hangfire.workerCount') }}</TableHead>
                                <TableHead>{{ t('hangfire.queues') }}</TableHead>
                                <TableHead>{{ t('hangfire.startedAt') }}</TableHead>
                                <TableHead>{{ t('hangfire.heartbeat') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!servers.length">
                                <TableCell colspan="5" class="py-8 text-center text-muted-foreground">
                                    {{ t('hangfire.noServers') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="srv in servers" :key="sName(srv)">
                                <TableCell>
                                    <div class="max-w-[200px] truncate font-mono text-sm" :title="sName(srv)">
                                        {{ sName(srv) }}
                                    </div>
                                </TableCell>
                                <TableCell>{{ sWorkers(srv) }}</TableCell>
                                <TableCell class="text-xs text-muted-foreground">
                                    <div class="max-w-[150px] truncate" :title="sQueues(srv)">{{ sQueues(srv) }}</div>
                                </TableCell>
                                <TableCell class="text-xs text-muted-foreground whitespace-nowrap">
                                    {{ sStarted(srv) }}
                                </TableCell>
                                <TableCell class="text-xs text-muted-foreground whitespace-nowrap">
                                    {{ sHeartbeat(srv) }}
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </div>
            </AppCard>
        </template>
    </div>
</template>
