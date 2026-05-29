<script setup lang="ts">
/**
 * MiniProfiler 仪表盘：调用 /profiler/results-list 获取会话列表，
 * 点击行后调用 /profiler/results 拉取详细计时数据。
 */
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { RefreshCw, ChevronDown, ChevronRight } from '@lucide/vue'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import { AppCard } from '@/components/primevue'
import { httpClient } from '@/api/client'

const { t } = useI18n()

// ——— 类型定义 ———
interface ProfilerSession {
    Id: string
    Name: string
    Started: string
    DurationMilliseconds: number
    MachineName?: string
    [key: string]: unknown
}

interface TimingDetail {
    Id: string
    Name: string
    DurationMilliseconds: number | null
    StartMilliseconds: number
    Children?: TimingDetail[]
    CustomTimings?: Record<string, CustomTiming[]>
}

interface CustomTiming {
    CommandString?: string
    DurationMilliseconds?: number | null
    [key: string]: unknown
}

interface ProfilerResult {
    Id: string
    Name: string
    DurationMilliseconds: number
    Root?: TimingDetail
    ClientTimings?: unknown
    [key: string]: unknown
}

// ——— 状态 ———
const sessions = ref<ProfilerSession[]>([])
const loading = ref(false)
const selectedId = ref<string | null>(null)
const detail = ref<ProfilerResult | null>(null)
const detailLoading = ref(false)

// ——— 列表加载 ———
async function loadList(): Promise<void> {
    loading.value = true
    selectedId.value = null
    detail.value = null
    try {
        const res = await httpClient.get<ProfilerSession[]>('/profiler/results-list')
        sessions.value = Array.isArray(res.data) ? res.data : []
    } catch {
        // 错误已在拦截器处理
    } finally {
        loading.value = false
    }
}

// ——— 会话详情 ———
async function selectSession(id: string): Promise<void> {
    if (selectedId.value === id) {
        // 再次点击折叠
        selectedId.value = null
        detail.value = null
        return
    }
    selectedId.value = id
    detail.value = null
    detailLoading.value = true
    try {
        const res = await httpClient.post<ProfilerResult>('/profiler/results', { id })
        detail.value = res.data ?? null
    } catch {
        // 错误已在拦截器处理
    } finally {
        detailLoading.value = false
    }
}

// ——— 工具函数 ———
function formatDuration(ms: number | null | undefined): string {
    if (ms == null) return '-'
    return ms.toFixed(2)
}

function formatStarted(iso: string): string {
    try {
        return new Date(iso).toLocaleString()
    } catch {
        return iso
    }
}

function durationClass(ms: number): string {
    if (ms > 1000) return 'text-red-500'
    if (ms > 200) return 'text-orange-500'
    return 'text-green-500'
}

// 扁平化 Root 计时树以便表格展示
function flattenTimings(node: TimingDetail, depth = 0): Array<TimingDetail & { _depth: number }> {
    const result: Array<TimingDetail & { _depth: number }> = [{ ...node, _depth: depth }]
    for (const child of node.Children ?? []) {
        result.push(...flattenTimings(child, depth + 1))
    }
    return result
}

// 统计 SQL 调用次数和总耗时
function getSqlStats(timing: TimingDetail): { count: number; totalMs: number } {
    const sqlTimings: CustomTiming[] = []
    for (const timings of Object.values(timing.CustomTimings ?? {})) {
        sqlTimings.push(...timings)
    }
    const total = sqlTimings.reduce((s, t) => s + (t.DurationMilliseconds ?? 0), 0)
    return { count: sqlTimings.length, totalMs: total }
}

onMounted(loadList)
</script>

<template>
    <div class="space-y-4">
        <!-- 标题栏 -->
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.profiler') }}</h1>
            <Button severity="secondary" outlined :disabled="loading" @click="loadList">
                <RefreshCw :class="['size-4', loading && 'animate-spin']" />
            </Button>
        </div>

        <!-- 会话列表 -->
        <AppCard>
            <div class="p-4 pb-2">
                <h3 class="text-base font-semibold">{{ t('profiler.results') }}</h3>
            </div>
            <div>
                <table class="w-full text-sm">
                    <thead>
                        <tr>
                            <th class="w-8"></th>
                            <th>{{ t('profiler.name') }}</th>
                            <th>{{ t('profiler.started') }}</th>
                            <th class="text-right">{{ t('profiler.duration') }}</th>
                            <th>{{ t('profiler.sessionId') }}</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="sessions.length === 0 && !loading">
                            <td colspan="5" class="py-8 text-center text-muted-foreground">
                                {{ t('profiler.noData') }}
                            </td>
                        </tr>
                        <template v-for="session in sessions" :key="session.Id">
                            <!-- 会话行 -->
                            <tr class="cursor-pointer hover:bg-muted/50" @click="selectSession(session.Id)">
                                <td class="w-8 pl-4">
                                    <ChevronDown
                                        v-if="selectedId === session.Id"
                                        class="size-4 text-muted-foreground"
                                    />
                                    <ChevronRight v-else class="size-4 text-muted-foreground" />
                                </td>
                                <td class="font-medium">{{ session.Name }}</td>
                                <td class="text-sm text-muted-foreground">
                                    {{ formatStarted(session.Started) }}
                                </td>
                                <td class="text-right">
                                    <span
                                        :class="[
                                            'font-mono text-sm font-semibold',
                                            durationClass(session.DurationMilliseconds),
                                        ]"
                                    >
                                        {{ formatDuration(session.DurationMilliseconds) }} ms
                                    </span>
                                </td>
                                <td class="font-mono text-xs text-muted-foreground">
                                    {{ session.Id }}
                                </td>
                            </tr>

                            <!-- 展开详情行 -->
                            <tr v-if="selectedId === session.Id" class="bg-muted/30 hover:bg-muted/30">
                                <td colspan="5" class="p-4">
                                    <div
                                        v-if="detailLoading"
                                        class="flex items-center gap-2 text-sm text-muted-foreground"
                                    >
                                        <RefreshCw class="size-3 animate-spin" />
                                        {{ t('common.loading') }}
                                    </div>
                                    <div v-else-if="detail">
                                        <p class="mb-2 text-sm font-semibold">{{ t('profiler.detail') }}</p>
                                        <table class="w-full text-sm">
                                            <thead>
                                                <tr>
                                                    <th>{{ t('profiler.timingName') }}</th>
                                                    <th class="text-right">
                                                        {{ t('profiler.timingMs') }}
                                                    </th>
                                                    <th class="text-right">
                                                        {{ t('profiler.sqlCount') }}
                                                    </th>
                                                    <th class="text-right">
                                                        {{ t('profiler.sqlDuration') }}
                                                    </th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                <tr
                                                    v-for="timing in detail.Root ? flattenTimings(detail.Root) : []"
                                                    :key="timing.Id"
                                                >
                                                    <td>
                                                        <span
                                                            class="font-mono text-sm"
                                                            :style="{ paddingLeft: `${timing._depth * 16}px` }"
                                                        >
                                                            {{ timing.Name }}
                                                        </span>
                                                    </td>
                                                    <td class="text-right font-mono text-sm">
                                                        <span :class="durationClass(timing.DurationMilliseconds ?? 0)">
                                                            {{ formatDuration(timing.DurationMilliseconds) }}
                                                        </span>
                                                    </td>
                                                    <td class="text-right">
                                                        <Tag
                                                            v-if="getSqlStats(timing).count > 0"
                                                            severity="secondary"
                                                            :value="String(getSqlStats(timing).count)"
                                                            class="font-mono"
                                                        />
                                                        <span v-else class="text-muted-foreground">-</span>
                                                    </td>
                                                    <td
                                                        class="text-right font-mono text-sm text-muted-foreground"
                                                    >
                                                        {{
                                                            getSqlStats(timing).count > 0
                                                                ? formatDuration(getSqlStats(timing).totalMs)
                                                                : '-'
                                                        }}
                                                    </td>
                                                </tr>
                                            </tbody>
                                        </table>
                                    </div>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </table>
            </div>
        </AppCard>
    </div>
</template>
