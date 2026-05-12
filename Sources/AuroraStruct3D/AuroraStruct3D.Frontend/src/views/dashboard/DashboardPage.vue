<script setup lang="ts">
/**
 * 系统仪表盘：展示系统负载（CPU/内存/NPU/GPU 圆形进度条）、
 * 实时网络流量折线图（上行/下行）、CAP 和 Hangfire 概览卡片。
 * 所有实时数据通过 SignalR ReceiveSystemMetrics 推送更新。
 */
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import * as signalR from '@microsoft/signalr'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components'
import { AnimatedCircularProgressBar } from '@/components/ui/animated-circular-progressbar'
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card'
import { useAuthStore } from '@/stores/auth'
import { httpClient } from '@/api/client'

use([CanvasRenderer, LineChart, GridComponent, TooltipComponent, LegendComponent])

const { t } = useI18n()
const authStore = useAuthStore()

// ── 系统指标 ─────────────────────────────────────────────────────────────────
interface SystemMetrics {
    cpuPercent: number
    memoryPercent: number
    memoryUsedBytes: number
    memoryTotalBytes: number
    networkSendRate: number
    networkReceiveRate: number
    networkTotalSent: number
    networkTotalReceived: number
    npuPercent: number
    gpuPercent: number
}

const metrics = ref<SystemMetrics>({
    cpuPercent: 0,
    memoryPercent: 0,
    memoryUsedBytes: 0,
    memoryTotalBytes: 0,
    networkSendRate: 0,
    networkReceiveRate: 0,
    networkTotalSent: 0,
    networkTotalReceived: 0,
    npuPercent: -1,
    gpuPercent: -1,
})

// ── CAP / Hangfire 概览 ──────────────────────────────────────────────────────
interface CapStats {
    publishSucceeded?: number
    publishFailed?: number
    consumeSucceeded?: number
    consumeFailed?: number
}
interface HangfireStats {
    enqueued?: number
    scheduled?: number
    processing?: number
    succeeded?: number
    failed?: number
    servers?: number
}
const capStats = ref<CapStats>({})
const hangfireStats = ref<HangfireStats>({})

// ── 网络流量历史（最近 60 个点，5s/点）──────────────────────────────────────
const MAX_POINTS = 60
const uploadHistory = ref<number[]>(Array(MAX_POINTS).fill(0))
const downloadHistory = ref<number[]>(Array(MAX_POINTS).fill(0))
const timeLabels = ref<string[]>(Array.from({ length: MAX_POINTS }, (_, i) => `${(MAX_POINTS - i) * 5}s`))

function pushNetworkPoint(send: number, recv: number): void {
    uploadHistory.value.push(send)
    if (uploadHistory.value.length > MAX_POINTS) uploadHistory.value.shift()
    downloadHistory.value.push(recv)
    if (downloadHistory.value.length > MAX_POINTS) downloadHistory.value.shift()
    refreshChartOption()
}

// 上行色：rgb(249, 204, 131)  下行色：rgb(135, 195, 255)
const trafficChartOption = ref({
    tooltip: {
        trigger: 'axis',
        formatter: (params: unknown[]) => {
            return (params as Array<{ seriesName: string; value: number }>)
                .map((p) => `${p.seriesName}: ${formatBytes(p.value, true)}`)
                .join('<br/>')
        },
    },
    legend: { data: [t('dashboard.upload'), t('dashboard.download')], top: 4 },
    grid: { top: 36, bottom: 24, left: 60, right: 12 },
    xAxis: {
        type: 'category',
        data: timeLabels.value,
        axisLabel: { show: false },
        axisTick: { show: false },
        axisLine: { lineStyle: { color: 'rgba(128,128,128,0.2)' } },
    },
    yAxis: {
        type: 'value',
        axisLabel: {
            formatter: (v: number) => formatBytes(v, true),
            fontSize: 10,
        },
        splitLine: { lineStyle: { color: 'rgba(128,128,128,0.15)' } },
    },
    series: [
        {
            name: t('dashboard.upload'),
            type: 'line',
            smooth: true,
            showSymbol: false,
            data: uploadHistory.value.slice(),
            itemStyle: { color: 'rgb(249,204,131)' },
            areaStyle: { color: 'rgba(249,204,131,0.12)' },
        },
        {
            name: t('dashboard.download'),
            type: 'line',
            smooth: true,
            showSymbol: false,
            data: downloadHistory.value.slice(),
            itemStyle: { color: 'rgb(135,195,255)' },
            areaStyle: { color: 'rgba(135,195,255,0.12)' },
        },
    ],
})

function refreshChartOption(): void {
    trafficChartOption.value = {
        ...trafficChartOption.value,
        series: [
            { ...trafficChartOption.value.series[0], data: uploadHistory.value.slice() },
            { ...trafficChartOption.value.series[1], data: downloadHistory.value.slice() },
        ],
    }
}

// ── 工具函数 ──────────────────────────────────────────────────────────────────
function formatBytes(bytes: number, rate = false): string {
    if (bytes <= 0) return rate ? '0 B/s' : '0 B'
    const units = ['B', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(1024))
    const val = (bytes / Math.pow(1024, i)).toFixed(2)
    return rate ? `${val} ${units[i]}/s` : `${val} ${units[i]}`
}

function gaugeColor(pct: number): string {
    if (pct >= 90) return 'rgb(239,68,68)'
    if (pct >= 70) return 'rgb(249,115,22)'
    return 'rgb(34,197,94)'
}

const cpuDisplay = computed(() => Math.round(Math.max(0, metrics.value.cpuPercent)))
const memDisplay = computed(() => Math.round(Math.max(0, metrics.value.memoryPercent)))
const npuDisplay = computed(() => Math.round(Math.max(0, metrics.value.npuPercent)))
const gpuDisplay = computed(() => Math.round(Math.max(0, metrics.value.gpuPercent)))

// NPU 和 GPU 同时可用时展示 4 列（Windows + Intel NPU 环境）
const showBothNpuGpu = computed(() => metrics.value.npuPercent >= 0 && metrics.value.gpuPercent >= 0)

// ── 初始数据拉取 ──────────────────────────────────────────────────────────────
async function loadInitialData(): Promise<void> {
    try {
        const [capRes, hangfireRes] = await Promise.all([
            httpClient.get<CapStats>('/cap/api/stats'),
            httpClient.get<HangfireStats>('/api/hangfire/stats'),
        ])
        capStats.value = capRes.data ?? {}
        hangfireStats.value = hangfireRes.data ?? {}
    } catch {
        // 错误已在拦截器处理
    }
}

// ── SignalR ───────────────────────────────────────────────────────────────────
let connection: signalR.HubConnection | null = null

async function startSignalR(): Promise<void> {
    connection = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/dashboard', {
            accessTokenFactory: () => authStore.token ?? '',
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build()

    connection.on('ReceiveSystemMetrics', (data: SystemMetrics) => {
        metrics.value = data
        pushNetworkPoint(data.networkSendRate, data.networkReceiveRate)
    })
    connection.on('ReceiveCapStats', (data: CapStats) => {
        capStats.value = data
    })
    connection.on('ReceiveHangfireStats', (data: HangfireStats) => {
        hangfireStats.value = data
    })

    try {
        await connection.start()
    } catch {
        // 连接失败时静默处理，展示初始数据
    }
}

onMounted(async () => {
    await loadInitialData()
    await startSignalR()
})

onUnmounted(async () => {
    if (connection) {
        await connection.stop()
        connection = null
    }
})
</script>

<template>
    <div class="space-y-5">
        <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.dashboard') }}</h1>

        <!-- 系统负载：圆形进度条 -->
        <div :class="['grid gap-4 grid-cols-1', showBothNpuGpu ? 'md:grid-cols-4' : 'md:grid-cols-3']">
            <!-- CPU -->
            <Card class="flex flex-col items-center py-5">
                <CardHeader class="pb-2 text-center">
                    <CardTitle class="text-sm text-muted-foreground">{{ t('dashboard.cpu') }}</CardTitle>
                </CardHeader>
                <CardContent class="flex flex-col items-center gap-2">
                    <AnimatedCircularProgressBar
                        v-if="metrics.cpuPercent >= 0"
                        :value="cpuDisplay"
                        :max="100"
                        :gauge-primary-color="gaugeColor(cpuDisplay)"
                        gauge-secondary-color="rgba(128,128,128,0.12)"
                        :circle-stroke-width="8"
                        :duration="1"
                        class="size-28 text-xl"
                    />
                    <span v-else class="text-sm text-muted-foreground">{{ t('dashboard.na') }}</span>
                    <span class="text-xs text-muted-foreground">{{ cpuDisplay }}%</span>
                </CardContent>
            </Card>

            <!-- 内存 -->
            <Card class="flex flex-col items-center py-5">
                <CardHeader class="pb-2 text-center">
                    <CardTitle class="text-sm text-muted-foreground">{{ t('dashboard.memory') }}</CardTitle>
                </CardHeader>
                <CardContent class="flex flex-col items-center gap-2">
                    <AnimatedCircularProgressBar
                        v-if="metrics.memoryPercent >= 0"
                        :value="memDisplay"
                        :max="100"
                        :gauge-primary-color="gaugeColor(memDisplay)"
                        gauge-secondary-color="rgba(128,128,128,0.12)"
                        :circle-stroke-width="8"
                        :duration="1"
                        class="size-28 text-xl"
                    />
                    <span v-else class="text-sm text-muted-foreground">{{ t('dashboard.na') }}</span>
                    <span class="text-xs text-muted-foreground">
                        {{ formatBytes(metrics.memoryUsedBytes) }} / {{ formatBytes(metrics.memoryTotalBytes) }}
                    </span>
                </CardContent>
            </Card>

            <!-- NPU 卡片（仅当 NPU 和 GPU 同时存在时单独展示 NPU；否则在混合卡中显示）-->
            <Card v-if="showBothNpuGpu" class="flex flex-col items-center py-5">
                <CardHeader class="pb-2 text-center">
                    <CardTitle class="text-sm text-muted-foreground">{{ t('dashboard.npu') }}</CardTitle>
                </CardHeader>
                <CardContent class="flex flex-col items-center gap-2">
                    <AnimatedCircularProgressBar
                        :value="npuDisplay"
                        :max="100"
                        :gauge-primary-color="gaugeColor(npuDisplay)"
                        gauge-secondary-color="rgba(128,128,128,0.12)"
                        :circle-stroke-width="8"
                        :duration="1"
                        class="size-28 text-xl"
                    />
                    <span class="text-xs text-muted-foreground">{{ npuDisplay }}%</span>
                </CardContent>
            </Card>

            <!-- GPU 卡片（仅当 NPU 和 GPU 同时存在时单独展示 GPU）-->
            <Card v-if="showBothNpuGpu" class="flex flex-col items-center py-5">
                <CardHeader class="pb-2 text-center">
                    <CardTitle class="text-sm text-muted-foreground">{{ t('dashboard.gpu') }}</CardTitle>
                </CardHeader>
                <CardContent class="flex flex-col items-center gap-2">
                    <AnimatedCircularProgressBar
                        :value="gpuDisplay"
                        :max="100"
                        :gauge-primary-color="gaugeColor(gpuDisplay)"
                        gauge-secondary-color="rgba(128,128,128,0.12)"
                        :circle-stroke-width="8"
                        :duration="1"
                        class="size-28 text-xl"
                    />
                    <span class="text-xs text-muted-foreground">{{ gpuDisplay }}%</span>
                </CardContent>
            </Card>

            <!-- NPU（Linux/RK3588）/ GPU（Windows）混合卡（仅当两者不同时存在时显示）-->
            <Card v-else class="flex flex-col items-center py-5">
                <CardHeader class="pb-2 text-center">
                    <CardTitle class="text-sm text-muted-foreground">
                        {{ metrics.npuPercent >= 0 ? t('dashboard.npu') : t('dashboard.gpu') }}
                    </CardTitle>
                </CardHeader>
                <CardContent class="flex flex-col items-center gap-2">
                    <AnimatedCircularProgressBar
                        v-if="metrics.npuPercent >= 0"
                        :value="npuDisplay"
                        :max="100"
                        :gauge-primary-color="gaugeColor(npuDisplay)"
                        gauge-secondary-color="rgba(128,128,128,0.12)"
                        :circle-stroke-width="8"
                        :duration="1"
                        class="size-28 text-xl"
                    />
                    <AnimatedCircularProgressBar
                        v-else-if="metrics.gpuPercent >= 0"
                        :value="gpuDisplay"
                        :max="100"
                        :gauge-primary-color="gaugeColor(gpuDisplay)"
                        gauge-secondary-color="rgba(128,128,128,0.12)"
                        :circle-stroke-width="8"
                        :duration="1"
                        class="size-28 text-xl"
                    />
                    <span v-else class="text-sm text-muted-foreground">{{ t('dashboard.na') }}</span>
                    <span class="text-xs text-muted-foreground">
                        {{
                            metrics.npuPercent >= 0
                                ? `${npuDisplay}%`
                                : metrics.gpuPercent >= 0
                                  ? `${gpuDisplay}%`
                                  : t('dashboard.notSupported')
                        }}
                    </span>
                </CardContent>
            </Card>
        </div>

        <!-- 网络流量：速率摘要 + 折线图合并 -->
        <Card>
            <CardHeader>
                <CardTitle class="text-base">{{ t('dashboard.traffic') }}</CardTitle>
                <CardDescription>
                    <span class="text-[rgb(249,204,131)] font-medium mr-4">
                        ↑ {{ formatBytes(metrics.networkSendRate, true) }}
                    </span>
                    <span class="text-[rgb(135,195,255)] font-medium">
                        ↓ {{ formatBytes(metrics.networkReceiveRate, true) }}
                    </span>
                    <span class="ml-4 text-muted-foreground text-xs">
                        {{ t('dashboard.totalSent') }}: {{ formatBytes(metrics.networkTotalSent) }} &nbsp;
                        {{ t('dashboard.totalReceived') }}: {{ formatBytes(metrics.networkTotalReceived) }}
                    </span>
                </CardDescription>
            </CardHeader>
            <CardContent class="pr-4">
                <VChart :option="trafficChartOption" style="height: 220px; width: 100%" autoresize />
            </CardContent>
        </Card>

        <!-- CAP + Hangfire 概览 -->
        <div class="grid gap-4 md:grid-cols-2">
            <!-- CAP 概览 -->
            <Card>
                <CardHeader>
                    <CardTitle class="text-base">{{ t('dashboard.capOverview') }}</CardTitle>
                </CardHeader>
                <CardContent class="grid grid-cols-2 gap-3">
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('cap.publishSucceeded') }}</div>
                        <div class="mt-1 text-2xl font-semibold text-green-500">
                            {{ capStats.publishSucceeded ?? '-' }}
                        </div>
                    </div>
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('cap.publishFailed') }}</div>
                        <div class="mt-1 text-2xl font-semibold text-red-500">{{ capStats.publishFailed ?? '-' }}</div>
                    </div>
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('cap.consumeSucceeded') }}</div>
                        <div class="mt-1 text-2xl font-semibold text-blue-500">
                            {{ capStats.consumeSucceeded ?? '-' }}
                        </div>
                    </div>
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('cap.consumeFailed') }}</div>
                        <div class="mt-1 text-2xl font-semibold text-orange-500">
                            {{ capStats.consumeFailed ?? '-' }}
                        </div>
                    </div>
                </CardContent>
            </Card>

            <!-- Hangfire 概览 -->
            <Card>
                <CardHeader>
                    <CardTitle class="text-base">{{ t('dashboard.hangfireOverview') }}</CardTitle>
                </CardHeader>
                <CardContent class="grid grid-cols-3 gap-2">
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('hangfire.enqueued') }}</div>
                        <div class="mt-1 text-xl font-semibold text-yellow-500">
                            {{ hangfireStats.enqueued ?? '-' }}
                        </div>
                    </div>
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('hangfire.processing') }}</div>
                        <div class="mt-1 text-xl font-semibold text-purple-500">
                            {{ hangfireStats.processing ?? '-' }}
                        </div>
                    </div>
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('hangfire.succeeded') }}</div>
                        <div class="mt-1 text-xl font-semibold text-green-500">
                            {{ hangfireStats.succeeded ?? '-' }}
                        </div>
                    </div>
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('hangfire.failed') }}</div>
                        <div class="mt-1 text-xl font-semibold text-red-500">{{ hangfireStats.failed ?? '-' }}</div>
                    </div>
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('hangfire.recurring') }}</div>
                        <div class="mt-1 text-xl font-semibold text-cyan-500">{{ hangfireStats.servers ?? '-' }}</div>
                    </div>
                    <div class="rounded-lg bg-muted/40 p-3">
                        <div class="text-xs text-muted-foreground">{{ t('hangfire.servers') }}</div>
                        <div class="mt-1 text-xl font-semibold text-indigo-500">{{ hangfireStats.servers ?? '-' }}</div>
                    </div>
                </CardContent>
            </Card>
        </div>
    </div>
</template>
