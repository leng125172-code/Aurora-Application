<script setup lang="ts">
/**
 * MiniProfiler 仪表盘：调用 /api/profiler/sessions 获取分页会话列表，
 * 支持名称关键字筛选；点击行展开完整计时详情。
 */
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RefreshCw, ChevronDown, ChevronRight } from '@lucide/vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import { AppCard } from '@/components/primevue'
import { httpClient } from '@/api/client'

const { t } = useI18n()

// ——— 类型定义（ASP.NET Core Minimal API 默认 camelCase）———
interface ProfilerSession {
    id: string
    name: string
    started: string
    durationMilliseconds: number
    machineName?: string
    sqlCount: number
    sqlDurationMs: number
}

interface PagedProfilerList {
    totalCount: number
    page: number
    pageSize: number
    items: ProfilerSession[]
}

interface TimingDetail {
    id: string
    name: string
    durationMilliseconds: number | null
    startMilliseconds: number
    children?: TimingDetail[]
    customTimings?: Record<string, CustomTiming[]>
}

interface CustomTiming {
    commandString?: string
    durationMilliseconds?: number | null
    [key: string]: unknown
}

interface ProfilerResult {
    id: string
    name: string
    durationMilliseconds: number
    root?: TimingDetail
    clientTimings?: unknown
    [key: string]: unknown
}

// ——— 状态 ———
const pagedData = ref<PagedProfilerList | null>(null)
const loading = ref(false)
const sessionSearch = ref('')
const sessionPage = ref(1)
const sessionPageSize = 20
const selectedId = ref<string | null>(null)
const detail = ref<ProfilerResult | null>(null)
const detailLoading = ref(false)

// ——— 列表加载 ———
async function loadList(): Promise<void> {
    loading.value = true
    selectedId.value = null
    detail.value = null
    try {
        const res = await httpClient.get<PagedProfilerList>('/api/profiler/sessions', {
            params: {
                page: sessionPage.value,
                pageSize: sessionPageSize,
                q: sessionSearch.value.trim() || undefined,
            },
        })
        pagedData.value = res.data ?? null
    } catch {
        // 错误已在拦截器处理
    } finally {
        loading.value = false
    }
}

// 搜索变化时重置到第 1 页
watch(sessionSearch, () => {
    sessionPage.value = 1
    loadList()
})

// 翻页时重新加载
watch(sessionPage, loadList)

// ——— 会话详情 ———
async function selectSession(id: string): Promise<void> {
    if (selectedId.value === id) {
        selectedId.value = null
        detail.value = null
        return
    }
    selectedId.value = id
    detail.value = null
    detailLoading.value = true
    try {
        const res = await httpClient.get<ProfilerResult>(`/api/profiler/sessions/${id}`)
        detail.value = res.data ?? null
    } catch {
        // 错误已在拦截器处理
    } finally {
        detailLoading.value = false
    }
}

// ——— 计算属性 ———
const sessions = computed<ProfilerSession[]>(() => pagedData.value?.items ?? [])

const totalPages = computed<number>(() => {
    if (!pagedData.value) return 1
    return Math.max(1, Math.ceil(pagedData.value.totalCount / sessionPageSize))
})

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

// 扁平化计时树以便表格展示
function flattenTimings(node: TimingDetail, depth = 0): Array<TimingDetail & { _depth: number }> {
    const result: Array<TimingDetail & { _depth: number }> = [{ ...node, _depth: depth }]
    for (const child of node.children ?? []) {
        result.push(...flattenTimings(child, depth + 1))
    }
    return result
}

// 统计 SQL 调用次数和总耗时
function getSqlStats(timing: TimingDetail): { count: number; totalMs: number } {
    const sqlTimings: CustomTiming[] = []
    for (const timings of Object.values(timing.customTimings ?? {})) {
        sqlTimings.push(...timings)
    }
    const total = sqlTimings.reduce((s, t) => s + (t.durationMilliseconds ?? 0), 0)
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
            <div class="px-4 pt-4 pb-2 flex items-center justify-between gap-4">
                <div class="flex items-center gap-2">
                    <h3 class="text-base font-semibold">{{ t('profiler.results') }}</h3>
                    <Tag v-if="pagedData" severity="secondary" :value="String(pagedData.totalCount)" class="ml-1" />
                </div>
                <InputText
                    v-model="sessionSearch"
                    :placeholder="t('profiler.searchPlaceholder')"
                    class="h-8 w-56 text-sm"
                />
            </div>
            <div class="overflow-x-auto">
                <table class="w-full text-sm">
                    <thead>
                        <tr>
                            <th class="w-8"></th>
                            <th>{{ t('profiler.name') }}</th>
                            <th>{{ t('profiler.started') }}</th>
                            <th class="text-right">{{ t('profiler.duration') }}</th>
                            <th class="text-right">{{ t('profiler.sqlCount') }}</th>
                            <th class="text-right">{{ t('profiler.sqlDuration') }}</th>
                            <th>{{ t('profiler.sessionId') }}</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="loading">
                            <td colspan="7" class="py-8 text-center text-muted-foreground text-sm">
                                {{ t('common.loading') }}
                            </td>
                        </tr>
                        <tr v-else-if="sessions.length === 0">
                            <td colspan="7" class="py-8 text-center text-muted-foreground">
                                {{ t('profiler.noData') }}
                            </td>
                        </tr>
                        <template v-for="session in sessions" :key="session.id">
                            <!-- 会话行 -->
                            <tr class="cursor-pointer hover:bg-muted/50" @click="selectSession(session.id)">
                                <td class="w-8 pl-4">
                                    <ChevronDown
                                        v-if="selectedId === session.id"
                                        class="size-4 text-muted-foreground"
                                    />
                                    <ChevronRight v-else class="size-4 text-muted-foreground" />
                                </td>
                                <td class="font-medium">{{ session.name }}</td>
                                <td class="text-sm text-muted-foreground">
                                    {{ formatStarted(session.started) }}
                                </td>
                                <td class="text-right">
                                    <span
                                        :class="[
                                            'font-mono text-sm font-semibold',
                                            durationClass(session.durationMilliseconds),
                                        ]"
                                    >
                                        {{ formatDuration(session.durationMilliseconds) }} ms
                                    </span>
                                </td>
                                <td class="text-right">
                                    <Tag
                                        v-if="session.sqlCount > 0"
                                        severity="secondary"
                                        :value="String(session.sqlCount)"
                                        class="font-mono"
                                    />
                                    <span v-else class="text-muted-foreground">-</span>
                                </td>
                                <td class="text-right font-mono text-sm text-muted-foreground">
                                    {{ session.sqlCount > 0 ? formatDuration(session.sqlDurationMs) : '-' }}
                                </td>
                                <td class="font-mono text-xs text-muted-foreground">
                                    {{ session.id }}
                                </td>
                            </tr>

                            <!-- 展开详情行 -->
                            <tr v-if="selectedId === session.id" class="bg-muted/30 hover:bg-muted/30">
                                <td colspan="7" class="p-4">
                                    <div
                                        v-if="detailLoading"
                                        class="flex items-center gap-2 text-sm text-muted-foreground"
                                    >
                                        <RefreshCw class="size-3 animate-spin" />
                                        {{ t('common.loading') }}
                                    </div>
                                    <div v-else-if="detail">
                                        <p class="mb-2 text-sm font-semibold">{{ t('profiler.detail') }}</p>
                                        <div class="overflow-x-auto">
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
                                                        v-for="timing in detail.root ? flattenTimings(detail.root) : []"
                                                        :key="timing.id"
                                                    >
                                                        <td>
                                                            <span
                                                                class="font-mono text-sm"
                                                                :style="{ paddingLeft: `${timing._depth * 16}px` }"
                                                            >
                                                                {{ timing.name }}
                                                            </span>
                                                        </td>
                                                        <td class="text-right font-mono text-sm">
                                                            <span
                                                                :class="durationClass(timing.durationMilliseconds ?? 0)"
                                                            >
                                                                {{ formatDuration(timing.durationMilliseconds) }}
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
                                                        <td class="text-right font-mono text-sm text-muted-foreground">
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
                                    </div>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </table>

                <!-- 分页控件 -->
                <div
                    v-if="pagedData && pagedData.totalCount > sessionPageSize"
                    class="flex items-center justify-between px-4 py-3 border-t border-border/50 text-sm"
                >
                    <span class="text-muted-foreground text-xs">
                        第 {{ (sessionPage - 1) * sessionPageSize + 1 }}–{{
                            Math.min(sessionPage * sessionPageSize, pagedData.totalCount)
                        }}
                        条，共 {{ pagedData.totalCount }} 条
                    </span>
                    <div class="flex items-center gap-1">
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :disabled="sessionPage <= 1 || loading"
                            @click="sessionPage = 1"
                        >
                            «
                        </Button>
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :disabled="sessionPage <= 1 || loading"
                            @click="sessionPage--"
                        >
                            ‹
                        </Button>
                        <span class="px-3 text-muted-foreground">{{ sessionPage }} / {{ totalPages }}</span>
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :disabled="sessionPage >= totalPages || loading"
                            @click="sessionPage++"
                        >
                            ›
                        </Button>
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :disabled="sessionPage >= totalPages || loading"
                            @click="sessionPage = totalPages"
                        >
                            »
                        </Button>
                    </div>
                </div>
            </div>
        </AppCard>
    </div>
</template>
