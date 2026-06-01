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
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import { AppCard } from '@/components/primevue'
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
let capChartResizeObserver: ResizeObserver | null = null

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
    // 监听容器尺寸变化，自动调用 resize 使图表宽度自适应
    capChartResizeObserver?.disconnect()
    capChartResizeObserver = new ResizeObserver(() => capChart?.resize())
    capChartResizeObserver.observe(capChartEl.value)
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
function statusVariant(s?: string): 'success' | 'secondary' | 'danger' {
    const v = (s ?? '').toLowerCase()
    if (v === 'succeeded') return 'success'
    if (v === 'failed') return 'danger'
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
    capChartResizeObserver?.disconnect()
    capChartResizeObserver = null
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
            <Button severity="secondary" outlined :disabled="loading" @click="loadCurrentTab">
                <RefreshCw :class="['size-4', loading && 'animate-spin']" />
            </Button>
        </div>

        <!-- ── 仪表盘 ── -->
        <template v-if="activeTab === 'dashboard'">
            <div class="grid gap-4 md:grid-cols-4">
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="0">
                    <div class="p-4 space-y-2">
                        <div class="text-sm font-medium whitespace-nowrap">{{ t('cap.publishSucceeded') }}</div>
                        <div class="text-3xl font-semibold text-green-500">{{ stats.publishSucceeded ?? '-' }}</div>
                    </div>
                </AppCard>
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="2">
                    <div class="p-4 space-y-2">
                        <div class="text-sm font-medium whitespace-nowrap">{{ t('cap.publishFailed') }}</div>
                        <div class="text-3xl font-semibold text-red-500">{{ stats.publishFailed ?? '-' }}</div>
                    </div>
                </AppCard>
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="4">
                    <div class="p-4 space-y-2">
                        <div class="text-sm font-medium whitespace-nowrap">{{ t('cap.consumeSucceeded') }}</div>
                        <div class="text-3xl font-semibold text-blue-500">{{ stats.consumeSucceeded ?? '-' }}</div>
                    </div>
                </AppCard>
                <AppCard :beam-size="80" :beam-duration="8" :beam-delay="6">
                    <div class="p-4 space-y-2">
                        <div class="text-sm font-medium whitespace-nowrap">{{ t('cap.consumeFailed') }}</div>
                        <div class="text-3xl font-semibold text-orange-500">{{ stats.consumeFailed ?? '-' }}</div>
                    </div>
                </AppCard>
            </div>
            <AppCard :beam-size="120" :beam-duration="10">
                <div class="p-4 space-y-3">
                    <div>
                        <div class="text-base font-semibold">{{ t('cap.statistics') }}</div>
                        <div class="text-sm text-muted-foreground mt-1">
                            {{ t('cap.publishDesc') }} / {{ t('cap.consumeDesc') }}
                        </div>
                    </div>
                    <div ref="capChartEl" style="height: 280px; width: 100%" />
                </div>
            </AppCard>
        </template>

        <!-- ── 发布 / 接收 ── -->
        <template v-else-if="activeTab === 'published' || activeTab === 'received'">
            <!-- 状态筛选 -->
            <div class="flex flex-wrap gap-2">
                <Button
                    v-for="s in STATUS_OPTIONS"
                    :key="s"
                    :severity="messageStatus === s ? 'primary' : 'secondary'"
                    :outlined="messageStatus !== s"
                    size="small"
                    @click="selectStatus(s)"
                >
                    {{ t('cap.' + s) }}
                </Button>
            </div>

            <AppCard :beam="true">
                <div class="p-4 pb-2">
                    <div class="text-base font-semibold">
                        {{ activeTab === 'published' ? t('cap.tabPublished') : t('cap.tabReceived') }}
                    </div>
                    <div class="text-sm text-muted-foreground mt-1">
                        {{ t('management.totalRecords', { total: pageCount * 20 }) }}
                    </div>
                </div>
                <div class="overflow-x-auto">
                    <table class="w-full text-sm">
                        <thead>
                            <tr>
                                <th>{{ t('cap.messageName') }}</th>
                                <th>{{ t('cap.messageGroup') }}</th>
                                <th>{{ t('cap.messageStatus') }}</th>
                                <th>{{ t('cap.messageTime') }}</th>
                                <th class="text-right">{{ t('common.action') }}</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-if="!messages.length">
                                <td colspan="5" class="py-8 text-center text-muted-foreground">
                                    {{ t('cap.noMessages') }}
                                </td>
                            </tr>
                            <tr v-for="msg in messages" :key="msg.id">
                                <td class="font-mono text-xs max-w-48 truncate">{{ msg.name }}</td>
                                <td class="text-xs text-muted-foreground">{{ msg.group ?? '-' }}</td>
                                <td>
                                    <Tag :severity="statusVariant(msg.statusName)" :value="msg.statusName" />
                                </td>
                                <td class="text-xs text-muted-foreground">{{ formatTime(msg.added) }}</td>
                                <td class="text-right">
                                    <Button
                                        v-if="msg.id && msg.statusName?.toLowerCase() === 'failed'"
                                        text
                                        severity="secondary"
                                        :title="t('hangfire.retryJob')"
                                        @click="requeue(msg.id!, messageType)"
                                    >
                                        <RotateCcw class="size-4 text-blue-500" />
                                    </Button>
                                </td>
                            </tr>
                        </tbody>
                    </table>
                    <div class="flex items-center justify-between border-t px-4 py-2">
                        <span class="text-xs text-muted-foreground">
                            {{ t('cap.pageIndex') }}: {{ currentPage }} / {{ Math.max(1, pageCount) }}
                        </span>
                        <div class="flex gap-2">
                            <Button
                                severity="secondary"
                                outlined
                                size="small"
                                :disabled="currentPage <= 1"
                                @click="loadMessages(currentPage - 1)"
                            >
                                {{ t('management.prevPage') }}
                            </Button>
                            <Button
                                severity="secondary"
                                outlined
                                size="small"
                                :disabled="currentPage >= pageCount"
                                @click="loadMessages(currentPage + 1)"
                            >
                                {{ t('management.nextPage') }}
                            </Button>
                        </div>
                    </div>
                </div>
            </AppCard>
        </template>

        <!-- ── 订阅者 ── -->
        <template v-else-if="activeTab === 'subscribers'">
            <AppCard :beam="true">
                <div class="p-4 pb-2">
                    <div class="text-base font-semibold">{{ t('cap.tabSubscribers') }}</div>
                </div>
                <div class="overflow-x-auto">
                    <table class="w-full text-sm">
                        <thead>
                            <tr>
                                <th>{{ t('cap.subscriberGroup') }}</th>
                                <th>{{ t('cap.subscriberName') }}</th>
                                <th>{{ t('cap.subscriberImpl') }}</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-if="!subscribers.length">
                                <td colspan="3" class="py-8 text-center text-muted-foreground">
                                    {{ t('cap.noSubscribers') }}
                                </td>
                            </tr>
                            <tr v-for="(sub, i) in subscribers" :key="i">
                                <td>
                                    <Tag severity="secondary" :value="sub.group" />
                                </td>
                                <td class="font-mono text-xs">{{ sub.name }}</td>
                                <td class="text-xs text-muted-foreground">
                                    {{ sub.implName ?? sub.methodInfo }}
                                </td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </AppCard>
        </template>

        <!-- ── 节点 ── -->
        <template v-else-if="activeTab === 'nodes'">
            <AppCard :beam="true">
                <div class="p-4 pb-2">
                    <div class="text-base font-semibold">{{ t('cap.tabNodes') }}</div>
                </div>
                <div class="overflow-x-auto">
                    <table class="w-full text-sm">
                        <thead>
                            <tr>
                                <th>{{ t('cap.nodeName') }}</th>
                                <th>{{ t('cap.nodeAddress') }}</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-if="!nodes.length">
                                <td colspan="2" class="py-8 text-center text-muted-foreground">
                                    {{ t('cap.noNodes') }}
                                </td>
                            </tr>
                            <tr v-for="(node, i) in nodes" :key="i">
                                <td class="font-medium">{{ node.name ?? '-' }}</td>
                                <td class="font-mono text-xs text-muted-foreground">
                                    {{ node.address ?? '-' }}
                                </td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </AppCard>
        </template>
    </div>
</template>
