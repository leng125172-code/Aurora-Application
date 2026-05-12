<script setup lang="ts">
/**
 * Hangfire 任务仪表盘，含5个子页：仪表盘/作业/重试/周期性作业/服务器。
 * 通过 route.query.tab 切换子页，SignalR 实时推送仪表盘统计数字。
 * API 数据来源：/api/hangfire/* (见 HangfireRouteActionProvider)
 */
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import * as signalR from '@microsoft/signalr'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components'
import { RefreshCw, PlayCircle, Trash2, RotateCcw } from 'lucide-vue-next'
import { Button } from '@/components/ui/button'
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Badge } from '@/components/ui/badge'
import { httpClient } from '@/api/client'
import { useAuthStore } from '@/stores/auth'

use([CanvasRenderer, LineChart, GridComponent, TooltipComponent, LegendComponent])

const { t } = useI18n()
const route = useRoute()
const authStore = useAuthStore()

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

const trendChartOption = ref({
    tooltip: { trigger: 'axis' as const },
    legend: { data: [t('hangfire.succeeded'), t('hangfire.failed')], top: 4 },
    grid: { top: 36, bottom: 24, left: 48, right: 12 },
    xAxis: {
        type: 'category' as const,
        data: [] as string[],
        axisLabel: { fontSize: 10 },
        axisTick: { show: false },
        axisLine: { lineStyle: { color: 'rgba(128,128,128,0.2)' } },
    },
    yAxis: {
        type: 'value' as const,
        minInterval: 1,
        axisLabel: { fontSize: 10 },
        splitLine: { lineStyle: { color: 'rgba(128,128,128,0.15)' } },
    },
    series: [
        {
            name: t('hangfire.succeeded'),
            type: 'line' as const,
            smooth: true,
            showSymbol: false,
            data: [] as number[],
            itemStyle: { color: 'rgb(34,197,94)' },
            areaStyle: { color: 'rgba(34,197,94,0.12)' },
        },
        {
            name: t('hangfire.failed'),
            type: 'line' as const,
            smooth: true,
            showSymbol: false,
            data: [] as number[],
            itemStyle: { color: 'rgb(239,68,68)' },
            areaStyle: { color: 'rgba(239,68,68,0.10)' },
        },
    ],
})

function updateChart(): void {
    const s = dailyHistory.value.succeeded ?? {}
    const f = dailyHistory.value.failed ?? {}
    const allDates = Array.from(new Set([...Object.keys(s), ...Object.keys(f)])).sort()
    trendChartOption.value = {
        ...trendChartOption.value,
        xAxis: { ...trendChartOption.value.xAxis, data: allDates.map((d) => d.substring(0, 10)) },
        series: [
            { ...trendChartOption.value.series[0], data: allDates.map((d) => s[d] ?? 0) },
            { ...trendChartOption.value.series[1], data: allDates.map((d) => f[d] ?? 0) },
        ],
    }
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

watch(activeTab, () => loadCurrentTab())

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
})
onUnmounted(async () => {
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
            <Button variant="outline" size="icon" :disabled="loading" @click="loadCurrentTab">
                <RefreshCw :class="['size-4', loading && 'animate-spin']" />
            </Button>
        </div>

        <!-- ── 仪表盘 ── -->
        <template v-if="activeTab === 'dashboard'">
            <div class="grid gap-3 md:grid-cols-4 lg:grid-cols-8">
                <Card v-for="c in STAT_CARDS" :key="c.key">
                    <CardHeader class="pb-2">
                        <CardTitle class="text-xs">{{ t(`hangfire.${c.key}`) }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div :class="['text-2xl font-semibold', c.color]">{{ stats[c.key] ?? '-' }}</div>
                    </CardContent>
                </Card>
            </div>
            <Card>
                <CardHeader>
                    <CardTitle class="text-base">{{ t('hangfire.dailyTrend') }}</CardTitle>
                    <CardDescription>{{ t('hangfire.dailyTrendDesc') }}</CardDescription>
                </CardHeader>
                <CardContent>
                    <VChart :option="trendChartOption" style="height: 280px; width: 100%" autoresize />
                </CardContent>
            </Card>
        </template>

        <!-- ── 作业 ── -->
        <template v-else-if="activeTab === 'jobs'">
            <div class="flex flex-wrap gap-2">
                <Button
                    v-for="s in JOB_STATES"
                    :key="s"
                    :variant="jobState === s ? 'default' : 'outline'"
                    size="sm"
                    @click="loadJobs(s)"
                >
                    {{ stateLabel(s) }}
                </Button>
            </div>
            <Card>
                <CardContent class="p-0">
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>{{ t('hangfire.jobId') }}</TableHead>
                                <TableHead>{{ t('hangfire.jobName') }}</TableHead>
                                <TableHead>{{ t('hangfire.jobCreated') }}</TableHead>
                                <TableHead class="text-right">{{ t('common.action') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!jobList.length">
                                <TableCell colspan="4" class="py-8 text-center text-muted-foreground">
                                    {{ t('hangfire.noJobs') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="job in jobList" :key="jobId(job)">
                                <TableCell class="font-mono text-xs">{{ jobId(job) }}</TableCell>
                                <TableCell class="font-mono text-sm">{{ jobLabel(job) }}</TableCell>
                                <TableCell class="text-xs text-muted-foreground">{{ jobTime(job) }}</TableCell>
                                <TableCell class="text-right">
                                    <div class="flex justify-end gap-1">
                                        <Button
                                            v-if="jobId(job)"
                                            variant="ghost"
                                            size="icon"
                                            :title="t('hangfire.retryJob')"
                                            @click="requeueJob(jobId(job))"
                                        >
                                            <RotateCcw class="size-4 text-blue-500" />
                                        </Button>
                                        <Button
                                            v-if="jobId(job)"
                                            variant="ghost"
                                            size="icon"
                                            :title="t('common.delete')"
                                            class="text-destructive hover:text-destructive"
                                            @click="deleteJob(jobId(job))"
                                        >
                                            <Trash2 class="size-4" />
                                        </Button>
                                    </div>
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </CardContent>
            </Card>
        </template>

        <!-- ── 重试（失败作业）── -->
        <template v-else-if="activeTab === 'retries'">
            <Card>
                <CardHeader>
                    <CardTitle class="text-base">{{ t('hangfire.tabRetries') }}</CardTitle>
                </CardHeader>
                <CardContent class="p-0">
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>{{ t('hangfire.jobId') }}</TableHead>
                                <TableHead>{{ t('hangfire.jobName') }}</TableHead>
                                <TableHead>错误信息</TableHead>
                                <TableHead class="text-right">{{ t('common.action') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!jobList.length">
                                <TableCell colspan="4" class="py-8 text-center text-muted-foreground">
                                    {{ t('hangfire.noJobs') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="job in jobList" :key="jobId(job)">
                                <TableCell class="font-mono text-xs">{{ jobId(job) }}</TableCell>
                                <TableCell class="font-mono text-sm">{{ jobLabel(job) }}</TableCell>
                                <TableCell class="max-w-xs truncate text-xs text-destructive">
                                    {{ (jobVal(job)['Reason'] ?? jobVal(job)['reason'] ?? '-') as string }}
                                </TableCell>
                                <TableCell class="text-right">
                                    <Button
                                        v-if="jobId(job)"
                                        variant="ghost"
                                        size="icon"
                                        :title="t('hangfire.retryJob')"
                                        @click="requeueJob(jobId(job))"
                                    >
                                        <RotateCcw class="size-4 text-blue-500" />
                                    </Button>
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </CardContent>
            </Card>
        </template>

        <!-- ── 周期性作业 ── -->
        <template v-else-if="activeTab === 'recurring'">
            <Card>
                <CardHeader>
                    <CardTitle class="text-base">{{ t('hangfire.tabRecurring') }}</CardTitle>
                </CardHeader>
                <CardContent class="p-0">
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
                                <TableCell class="font-mono text-sm">{{ rId(job) }}</TableCell>
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
                                            variant="ghost"
                                            size="icon"
                                            :title="t('hangfire.triggerNow')"
                                            @click="triggerJob(rId(job))"
                                        >
                                            <PlayCircle class="size-4 text-green-500" />
                                        </Button>
                                        <Button
                                            v-if="rId(job)"
                                            variant="ghost"
                                            size="icon"
                                            :title="t('common.delete')"
                                            class="text-destructive hover:text-destructive"
                                            @click="deleteRecurringJob(rId(job))"
                                        >
                                            <Trash2 class="size-4" />
                                        </Button>
                                    </div>
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </CardContent>
            </Card>
        </template>

        <!-- ── 服务器 ── -->
        <template v-else-if="activeTab === 'servers'">
            <Card>
                <CardHeader>
                    <CardTitle class="text-base">{{ t('hangfire.tabServers') }}</CardTitle>
                </CardHeader>
                <CardContent class="p-0">
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
                                <TableCell class="font-mono text-sm">{{ sName(srv) }}</TableCell>
                                <TableCell>{{ sWorkers(srv) }}</TableCell>
                                <TableCell class="text-xs text-muted-foreground">{{ sQueues(srv) }}</TableCell>
                                <TableCell class="text-xs text-muted-foreground">{{ sStarted(srv) }}</TableCell>
                                <TableCell class="text-xs text-muted-foreground">{{ sHeartbeat(srv) }}</TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </CardContent>
            </Card>
        </template>
    </div>
</template>
