<script setup lang="ts">
/**
 * CAP 消息仪表盘，含5个子页：仪表盘/发布/接收/订阅者/节点。
 * 通过 route.query.tab 切换子页，使用 REST API 加载数据，SignalR 实时推送仪表盘统计。
 */
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import * as signalR from '@microsoft/signalr'
import * as echarts from 'echarts'
import { RefreshCw, RotateCcw } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card'
import { BorderBeam } from '@/components/ui/border-beam'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Badge } from '@/components/ui/badge'
import { httpClient } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import { useThemeStore } from '@/stores/theme'

const { t } = useI18n()
const route = useRoute()
const authStore = useAuthStore()
const themeStore = useThemeStore()

const activeTab = computed(() => (route.query.tab as string) || 'dashboard')

// ── 类型 ──────────────────────────────────────────────────────────────────────
interface CapStats {
    publishSucceeded?: number
    publishFailed?: number
    consumeSucceeded?: number
    consumeFailed?: number
}
interface CapMessage {
    id?: number | string
    name?: string
    group?: string
    content?: string
    added?: string
    statusName?: string
    expiresAt?: string
}
interface CapPageResult {
    pageCount?: number
    data?: CapMessage[]
}
interface CapSubscriber {
    group?: string
    name?: string
    methodInfo?: string
    implName?: string
    serviceAddress?: string
}
interface CapNode {
    name?: string
    address?: string
}

// ── 状态 ──────────────────────────────────────────────────────────────────────
const stats = ref<CapStats>({})
const loading = ref(false)
const currentPage = ref(1)
const pageCount = ref(0)
const messages = ref<CapMessage[]>([])
const subscribers = ref<CapSubscriber[]>([])
const nodes = ref<CapNode[]>([])

// ── 仪表盘 ────────────────────────────────────────────────────────────────────
async function loadStats(): Promise<void> {
    loading.value = true
    try {
        const res = await httpClient.get<CapStats>('/cap/api/stats')
        stats.value = res.data ?? {}
        updateChart()
    } catch {
        // 错误已在拦截器处理
    } finally {
        loading.value = false
    }
}

const capChartEl = ref<HTMLDivElement | null>(null)
let capChart: echarts.ECharts | null = null

/** 构建 CAP 统计柱状图 ECharts option。 */
function buildCapOption() {
    const cats = [t('cap.succeeded'), t('cap.failed')]
    const isDark = themeStore.isDark
    return {
        backgroundColor: 'transparent',
        textStyle: { color: isDark ? '#e5e7eb' : '#374151', fontSize: 11 },
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend: {
            top: 0,
            right: 0,
            textStyle: { color: isDark ? '#e5e7eb' : '#374151', fontSize: 11 },
        },
        grid: { top: 36, bottom: 36, left: 50, right: 12, containLabel: false },
        xAxis: {
            type: 'category',
            data: cats,
            axisLabel: { fontSize: 11 },
        },
        yAxis: {
            type: 'value',
            minInterval: 1,
            splitLine: { lineStyle: { color: isDark ? '#374151' : '#e5e7eb' } },
        },
        series: [
            {
                name: t('cap.publishSucceeded'),
                type: 'bar',
                data: [stats.value.publishSucceeded ?? 0, stats.value.publishFailed ?? 0],
                itemStyle: { color: '#22c55e' },
            },
            {
                name: t('cap.consumeSucceeded'),
                type: 'bar',
                data: [stats.value.consumeSucceeded ?? 0, stats.value.consumeFailed ?? 0],
                itemStyle: { color: '#3b82f6' },
            },
        ],
    }
}

/** 初始化 CAP 统计柱状图 ECharts 实例。 */
function initCapChart(): void {
    if (!capChartEl.value) return
    capChart?.dispose()
    capChart = echarts.init(capChartEl.value, themeStore.isDark ? 'dark' : undefined, { renderer: 'canvas' })
    capChart.setOption(buildCapOption())
}

/** 仅更新柱状图数据，不重建实例。 */
function updateChart(): void {
    capChart?.setOption(buildCapOption())
}

// ── 消息列表（发布/接收）─────────────────────────────────────────────────────
type MessageType = 'published' | 'received'

const messageStatus = ref<string>('succeeded')
const messageType = ref<MessageType>('published')

const STATUS_OPTIONS = ['succeeded', 'failed', 'delayed', 'processing'] as const

async function loadMessages(page = 1): Promise<void> {
    currentPage.value = page
    loading.value = true
    try {
        const res = await httpClient.get<CapPageResult>(
            `/cap/api/${messageType.value}/${messageStatus.value}?currentPage=${page}&pageSize=20`
        )
        const d = res.data ?? {}
        pageCount.value = d.pageCount ?? 0
        messages.value = d.data ?? []
    } catch {
        // 错误已在拦截器处理
    } finally {
        loading.value = false
    }
}

function selectStatus(s: string): void {
    messageStatus.value = s
    loadMessages(1)
}

async function requeue(id: number | string, type: MessageType): Promise<void> {
    try {
        await httpClient.post(`/cap/api/${type}/requeue`, JSON.stringify([id]), {
            headers: { 'Content-Type': 'application/json' },
        })
        await loadMessages(currentPage.value)
    } catch {
        // 错误已在拦截器处理
    }
}

// ── 订阅者 ────────────────────────────────────────────────────────────────────
async function loadSubscribers(): Promise<void> {
    loading.value = true
    try {
        const res = await httpClient.get<CapSubscriber[]>('/cap/api/subscriber')
        subscribers.value = Array.isArray(res.data) ? res.data : []
    } catch {
        // 错误已在拦截器处理
    } finally {
        loading.value = false
    }
}

// ── 节点 ──────────────────────────────────────────────────────────────────────
async function loadNodes(): Promise<void> {
    loading.value = true
    try {
        const res = await httpClient.get<CapNode[]>('/cap/api/nodes')
        nodes.value = Array.isArray(res.data) ? res.data : []
    } catch {
        // 错误已在拦截器处理
    } finally {
        loading.value = false
    }
}

// ── 根据 tab 加载对应数据 ──────────────────────────────────────────────────────
function loadCurrentTab(): void {
    switch (activeTab.value) {
        case 'dashboard':
            void loadStats()
            break
        case 'published':
            messageType.value = 'published'
            void loadMessages(1)
            break
        case 'received':
            messageType.value = 'received'
            void loadMessages(1)
            break
        case 'subscribers':
            void loadSubscribers()
            break
        case 'nodes':
            void loadNodes()
            break
    }
}

watch(activeTab, async (tab) => {
    loadCurrentTab()
    if (tab === 'dashboard') {
        // v-if 切回仪表盘时 div 已重新创建，需重新初始化图表
        await nextTick()
        initCapChart()
    }
})

watch(
    () => themeStore.isDark,
    () => {
        // 主题变化时重新初始化图表以应用新颜色方案
        initCapChart()
    }
)

// ── SignalR ────────────────────────────────────────────────────────────────────
let connection: signalR.HubConnection | null = null

async function startSignalR(): Promise<void> {
    connection = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/dashboard', {
            accessTokenFactory: () => authStore.token ?? '',
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build()

    connection.on('ReceiveCapStats', (data: CapStats) => {
        stats.value = data
        updateChart()
    })
    // 注册占位处理器，避免 Hub 广播其他事件时产生 Warning
    connection.on('ReceiveHangfireStats', () => {
        /* CapPage 不处理 */
    })
    connection.on('ReceiveSystemMetrics', () => {
        /* CapPage 不处理 */
    })

    try {
        await connection.start()
    } catch (err) {
        console.warn('[DashboardHub] 连接失败，将依赖初始加载数据', err)
    }
}

// ── 工具 ──────────────────────────────────────────────────────────────────────
function statusVariant(s?: string): 'default' | 'secondary' | 'destructive' {
    const v = (s ?? '').toLowerCase()
    if (v === 'succeeded') return 'default'
    if (v === 'failed') return 'destructive'
    return 'secondary'
}

function formatTime(iso?: string): string {
    if (!iso) return '-'
    try {
        return new Date(iso).toLocaleString()
    } catch {
        return iso
    }
}

onMounted(async () => {
    loadCurrentTab()
    await startSignalR()
    // 初始 tab 是仪表盘时初始化柱状图
    if (activeTab.value === 'dashboard') {
        await nextTick()
        initCapChart()
    }
})

onUnmounted(async () => {
    capChart?.dispose()
    if (connection) {
        connection.off('ReceiveCapStats')
        await connection.stop()
        connection = null
    }
})
</script>

<template>
    <div class="space-y-4">
        <!-- 标题 + 刷新 -->
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.cap') }}</h1>
            <Button variant="outline" size="icon" :disabled="loading" @click="loadCurrentTab">
                <RefreshCw :class="['size-4', loading && 'animate-spin']" />
            </Button>
        </div>

        <!-- ── 仪表盘 ── -->
        <template v-if="activeTab === 'dashboard'">
            <div class="grid gap-4 md:grid-cols-4">
                <Card>
                    <CardHeader>
                        <CardTitle class="text-sm">{{ t('cap.publishSucceeded') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="text-3xl font-semibold text-green-500">{{ stats.publishSucceeded ?? '-' }}</div>
                    </CardContent>
                </Card>
                <Card>
                    <CardHeader>
                        <CardTitle class="text-sm">{{ t('cap.publishFailed') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="text-3xl font-semibold text-red-500">{{ stats.publishFailed ?? '-' }}</div>
                    </CardContent>
                </Card>
                <Card>
                    <CardHeader>
                        <CardTitle class="text-sm">{{ t('cap.consumeSucceeded') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="text-3xl font-semibold text-blue-500">{{ stats.consumeSucceeded ?? '-' }}</div>
                    </CardContent>
                </Card>
                <Card>
                    <CardHeader>
                        <CardTitle class="text-sm">{{ t('cap.consumeFailed') }}</CardTitle>
                    </CardHeader>
                    <CardContent>
                        <div class="text-3xl font-semibold text-orange-500">{{ stats.consumeFailed ?? '-' }}</div>
                    </CardContent>
                </Card>
            </div>
            <Card class="relative">
                <BorderBeam :size="120" :duration="10" />
                <CardHeader>
                    <CardTitle class="text-base">{{ t('cap.statistics') }}</CardTitle>
                    <CardDescription>{{ t('cap.publishDesc') }} / {{ t('cap.consumeDesc') }}</CardDescription>
                </CardHeader>
                <CardContent>
                    <div ref="capChartEl" style="height: 280px; width: 100%" />
                </CardContent>
            </Card>
        </template>

        <!-- ── 发布 / 接收 ── -->
        <template v-else-if="activeTab === 'published' || activeTab === 'received'">
            <!-- 状态筛选 -->
            <div class="flex flex-wrap gap-2">
                <Button
                    v-for="s in STATUS_OPTIONS"
                    :key="s"
                    :variant="messageStatus === s ? 'default' : 'outline'"
                    size="sm"
                    @click="selectStatus(s)"
                >
                    {{ t('cap.' + s) }}
                </Button>
            </div>

            <Card>
                <CardHeader>
                    <CardTitle class="text-base">
                        {{ activeTab === 'published' ? t('cap.tabPublished') : t('cap.tabReceived') }}
                    </CardTitle>
                    <CardDescription>{{ t('management.totalRecords', { total: pageCount * 20 }) }}</CardDescription>
                </CardHeader>
                <CardContent class="p-0">
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>{{ t('cap.messageName') }}</TableHead>
                                <TableHead>{{ t('cap.messageGroup') }}</TableHead>
                                <TableHead>{{ t('cap.messageStatus') }}</TableHead>
                                <TableHead>{{ t('cap.messageTime') }}</TableHead>
                                <TableHead class="text-right">{{ t('common.action') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!messages.length">
                                <TableCell colspan="5" class="py-8 text-center text-muted-foreground">
                                    {{ t('cap.noMessages') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="msg in messages" :key="msg.id">
                                <TableCell class="font-mono text-xs max-w-48 truncate">{{ msg.name }}</TableCell>
                                <TableCell class="text-xs text-muted-foreground">{{ msg.group ?? '-' }}</TableCell>
                                <TableCell>
                                    <Badge :variant="statusVariant(msg.statusName)">{{ msg.statusName }}</Badge>
                                </TableCell>
                                <TableCell class="text-xs text-muted-foreground">{{ formatTime(msg.added) }}</TableCell>
                                <TableCell class="text-right">
                                    <Button
                                        v-if="msg.id && msg.statusName?.toLowerCase() === 'failed'"
                                        variant="ghost"
                                        size="icon"
                                        :title="t('hangfire.retryJob')"
                                        @click="requeue(msg.id!, messageType)"
                                    >
                                        <RotateCcw class="size-4 text-blue-500" />
                                    </Button>
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                    <div class="flex items-center justify-between border-t px-4 py-2">
                        <span class="text-xs text-muted-foreground">
                            {{ t('cap.pageIndex') }}: {{ currentPage }} / {{ Math.max(1, pageCount) }}
                        </span>
                        <div class="flex gap-2">
                            <Button
                                variant="outline"
                                size="sm"
                                :disabled="currentPage <= 1"
                                @click="loadMessages(currentPage - 1)"
                            >
                                {{ t('management.prevPage') }}
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                :disabled="currentPage >= pageCount"
                                @click="loadMessages(currentPage + 1)"
                            >
                                {{ t('management.nextPage') }}
                            </Button>
                        </div>
                    </div>
                </CardContent>
            </Card>
        </template>

        <!-- ── 订阅者 ── -->
        <template v-else-if="activeTab === 'subscribers'">
            <Card>
                <CardHeader>
                    <CardTitle class="text-base">{{ t('cap.tabSubscribers') }}</CardTitle>
                </CardHeader>
                <CardContent class="p-0">
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>{{ t('cap.subscriberGroup') }}</TableHead>
                                <TableHead>{{ t('cap.subscriberName') }}</TableHead>
                                <TableHead>{{ t('cap.subscriberImpl') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!subscribers.length">
                                <TableCell colspan="3" class="py-8 text-center text-muted-foreground">
                                    {{ t('cap.noSubscribers') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="(sub, i) in subscribers" :key="i">
                                <TableCell>
                                    <Badge variant="secondary">{{ sub.group }}</Badge>
                                </TableCell>
                                <TableCell class="font-mono text-xs">{{ sub.name }}</TableCell>
                                <TableCell class="text-xs text-muted-foreground">
                                    {{ sub.implName ?? sub.methodInfo }}
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </CardContent>
            </Card>
        </template>

        <!-- ── 节点 ── -->
        <template v-else-if="activeTab === 'nodes'">
            <Card>
                <CardHeader>
                    <CardTitle class="text-base">{{ t('cap.tabNodes') }}</CardTitle>
                </CardHeader>
                <CardContent class="p-0">
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>{{ t('cap.nodeName') }}</TableHead>
                                <TableHead>{{ t('cap.nodeAddress') }}</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            <TableRow v-if="!nodes.length">
                                <TableCell colspan="2" class="py-8 text-center text-muted-foreground">
                                    {{ t('cap.noNodes') }}
                                </TableCell>
                            </TableRow>
                            <TableRow v-for="(node, i) in nodes" :key="i">
                                <TableCell class="font-medium">{{ node.name ?? '-' }}</TableCell>
                                <TableCell class="font-mono text-xs text-muted-foreground">
                                    {{ node.address ?? '-' }}
                                </TableCell>
                            </TableRow>
                        </TableBody>
                    </Table>
                </CardContent>
            </Card>
        </template>
    </div>
</template>
