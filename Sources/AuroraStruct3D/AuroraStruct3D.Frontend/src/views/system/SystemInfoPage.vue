<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RefreshCw, Server, Package, ChevronDown, ChevronRight } from '@lucide/vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import { AppCard } from '@/components/primevue'
import { httpClient } from '@/api/client'

const { t } = useI18n()

// ── 类型定义 ──────────────────────────────────────────────────────────────────

interface ServerInfo {
    applicationName: string
    osDescription: string
    osArchitecture: string
    processorCount: number
    processorModel: string
    cpuPercent: number
    memoryUsedBytes: number
    memoryTotalBytes: number
    machineName: string
    userName: string
    dotNetVersion: string
    contentRootPath: string
    processName: string
    processStartTime: string
    serverTime: string
    uptimeText: string
}

interface AssemblyInfo {
    name: string
    title: string
    fileVersion: string
    informationalVersion: string
    buildTime: string | null
    description: string
}

interface SystemInfoResponse {
    server: ServerInfo
}

interface PagedAssemblyList {
    totalCount: number
    page: number
    pageSize: number
    items: AssemblyInfo[]
}

// ── 状态 ──────────────────────────────────────────────────────────────────────

const info = ref<SystemInfoResponse | null>(null)
const loading = ref(false)
const assemblySearch = ref('')
const assemblyPage = ref(1)
const assemblyPageSize = 20
const assemblyData = ref<PagedAssemblyList | null>(null)
const assemblyLoading = ref(false)
/** 当前展开的程序集 name，null 表示全部折叠 */
const expandedAssembly = ref<string | null>(null)

// ── 数据加载 ──────────────────────────────────────────────────────────────────

async function loadData(): Promise<void> {
    loading.value = true
    try {
        const res = await httpClient.get<SystemInfoResponse>('/api/system-info')
        info.value = res.data
    } catch {
        // 错误已在拦截器处理
    } finally {
        loading.value = false
    }
    await loadAssemblies()
}

async function loadAssemblies(): Promise<void> {
    assemblyLoading.value = true
    try {
        const res = await httpClient.get<PagedAssemblyList>('/api/system-info/assemblies', {
            params: {
                page: assemblyPage.value,
                pageSize: assemblyPageSize,
                q: assemblySearch.value.trim() || undefined,
            },
        })
        assemblyData.value = res.data
        expandedAssembly.value = null
    } catch {
        // 错误已在拦截器处理
    } finally {
        assemblyLoading.value = false
    }
}

onMounted(loadData)

// 搜索关键字变化时重置到第一页并重新加载
watch(assemblySearch, () => {
    assemblyPage.value = 1
    loadAssemblies()
})

// 翻页时重新加载
watch(assemblyPage, loadAssemblies)

// ── 工具函数 ──────────────────────────────────────────────────────────────────

function formatBytes(bytes: number): string {
    if (bytes <= 0) return '0 B'
    const units = ['B', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(1024))
    return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${units[i]}`
}

function formatDateTime(iso: string | null | undefined): string {
    if (!iso) return '-'
    return new Date(iso).toLocaleString('zh-CN', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
    })
}

function cpuColor(pct: number): 'danger' | 'warn' | 'info' {
    if (pct >= 90) return 'danger'
    if (pct >= 70) return 'warn'
    return 'info'
}

// ── 计算属性 ──────────────────────────────────────────────────────────────────

const filteredAssemblies = computed<AssemblyInfo[]>(() => assemblyData.value?.items ?? [])

const totalPages = computed<number>(() => {
    if (!assemblyData.value) return 1
    return Math.max(1, Math.ceil(assemblyData.value.totalCount / assemblyPageSize))
})

const memPercent = computed<number>(() => {
    if (!info.value) return 0
    const { memoryUsedBytes, memoryTotalBytes } = info.value.server
    if (memoryTotalBytes <= 0) return 0
    return Math.round((memoryUsedBytes / memoryTotalBytes) * 100)
})

// 已运行天数（从 serverTime 与 processStartTime 计算，无需依赖 uptimeText）
const uptimeDays = computed<number>(() => {
    if (!info.value) return 0
    const start = new Date(info.value.server.processStartTime).getTime()
    const now = new Date(info.value.server.serverTime).getTime()
    return Math.floor((now - start) / 86_400_000)
})

// 超过 30 天时展示稳定运行横幅
const showStableBanner = computed<boolean>(() => uptimeDays.value >= 30)

/** 切换展开/折叠某个程序集 */
function toggleAssembly(name: string): void {
    expandedAssembly.value = expandedAssembly.value === name ? null : name
}

/**
 * 截断 informationalVersion 中的 git hash 部分。
 * 例如 "8.1.0+22a775b8eae3..." → "8.1.0  +22a775b8"
 * 只保留语义版本号 + 8 位 hash 前缀，其余省略。
 */
function shortInfoVersion(ver: string): string {
    if (!ver) return '-'
    const plus = ver.indexOf('+')
    if (plus === -1) return ver
    const semver = ver.slice(0, plus)
    const hash = ver.slice(plus + 1, plus + 9)
    return `${semver}+${hash}`
}
</script>

<template>
    <div class="space-y-5">
        <!-- 页头 -->
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('sysinfo.title') }}</h1>
            <Button severity="secondary" outlined size="small" :disabled="loading" @click="loadData">
                <RefreshCw :class="['size-4 mr-1.5', loading && 'animate-spin']" />
                {{ t('sysinfo.refresh') }}
            </Button>
        </div>

        <!-- 稳定运行横幅（运行超过 30 天时显示） -->
        <div
            v-if="showStableBanner"
            class="flex items-center gap-3 rounded-xl border border-emerald-500/40 bg-emerald-500/10 px-5 py-3.5 text-emerald-700 dark:text-emerald-400"
        >
            <span class="text-2xl">🎉</span>
            <div>
                <p class="font-semibold text-base">{{ t('sysinfo.stableBannerTitle') }}</p>
                <p class="text-sm opacity-80">{{ t('sysinfo.stableBannerDesc', { days: uptimeDays }) }}</p>
            </div>
        </div>

        <!-- 服务器信息卡 -->
        <AppCard :beam-size="120" :beam-duration="10">
            <template #header>
                <div class="px-4 pt-3 text-base font-semibold flex items-center gap-2">
                    <Server class="size-4" />
                    {{ t('sysinfo.server') }}
                </div>
            </template>
            <div v-if="info">
                <div class="grid grid-cols-1 md:grid-cols-2 gap-x-8 gap-y-0 text-sm">
                    <!-- 左列 -->
                    <div class="divide-y divide-border/60">
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.appName') }}
                            </span>
                            <span class="font-medium break-all">{{ info.server.applicationName }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">{{ t('sysinfo.os') }}</span>
                            <span class="break-all">{{ info.server.osDescription }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.processor') }}
                            </span>
                            <span class="break-all">
                                {{ info.server.processorModel }}，{{ info.server.processorCount }} 核
                                <Tag
                                    v-if="info.server.cpuPercent >= 0"
                                    :severity="cpuColor(info.server.cpuPercent)"
                                    :value="`${info.server.cpuPercent.toFixed(1)}%`"
                                    class="ml-1.5"
                                />
                            </span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.memory') }}
                            </span>
                            <span>
                                {{ formatBytes(info.server.memoryUsedBytes) }} /
                                {{ formatBytes(info.server.memoryTotalBytes) }}
                                <Tag
                                    :severity="memPercent >= 90 ? 'danger' : memPercent >= 70 ? 'warn' : 'info'"
                                    :value="`${memPercent}%`"
                                    class="ml-1.5"
                                />
                            </span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.directory') }}
                            </span>
                            <span class="break-all font-mono text-xs leading-5">{{ info.server.contentRootPath }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.processName') }}
                            </span>
                            <span class="font-mono">{{ info.server.processName }}</span>
                        </div>
                    </div>

                    <!-- 右列 -->
                    <div class="divide-y divide-border/60">
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.dotnetVersion') }}
                            </span>
                            <span>{{ info.server.dotNetVersion }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.machine') }}
                            </span>
                            <span>{{ info.server.machineName }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.user') }}
                            </span>
                            <span>{{ info.server.userName }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.serverTime') }}
                            </span>
                            <span>{{ formatDateTime(info.server.serverTime) }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.processStart') }}
                            </span>
                            <span>{{ formatDateTime(info.server.processStartTime) }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="shrink-0 whitespace-nowrap text-muted-foreground">
                                {{ t('sysinfo.uptime') }}
                            </span>
                            <span class="font-medium">{{ info.server.uptimeText }}</span>
                        </div>
                    </div>
                </div>
            </div>
            <div v-else class="py-8 text-center text-sm text-muted-foreground">
                {{ loading ? t('common.loading') : '-' }}
            </div>
        </AppCard>

        <!-- 已加载程序集卡 -->
        <AppCard :beam-size="120" :beam-duration="10" :beam-delay="3">
            <template #header>
                <div class="px-4 pt-3 flex items-center justify-between gap-4">
                    <div class="text-base font-semibold flex items-center gap-2">
                        <Package class="size-4" />
                        {{ t('sysinfo.assemblies') }}
                        <Tag
                            severity="secondary"
                            :value="assemblyData ? String(assemblyData.totalCount) : '…'"
                            class="ml-1"
                        />
                    </div>
                    <InputText
                        v-model="assemblySearch"
                        :placeholder="t('sysinfo.searchAssembly')"
                        class="h-8 w-56 text-sm"
                    />
                </div>
            </template>
            <div class="overflow-x-auto">
                <table class="w-full text-sm">
                    <thead>
                        <tr>
                            <th class="w-8"></th>
                            <th>{{ t('sysinfo.colTitle') }}</th>
                            <th class="text-right w-36">{{ t('sysinfo.colFileVersion') }}</th>
                            <th class="text-right w-44">{{ t('sysinfo.colInfoVersion') }}</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="assemblyLoading">
                            <td colspan="4" class="py-8 text-center text-muted-foreground text-sm">
                                {{ t('common.loading') }}
                            </td>
                        </tr>
                        <tr v-else-if="filteredAssemblies.length === 0">
                            <td colspan="4" class="py-8 text-center text-muted-foreground">
                                {{ t('management.noData') }}
                            </td>
                        </tr>
                        <template v-for="asm in filteredAssemblies" :key="asm.name">
                            <!-- 主行：标题 + 版本号 -->
                            <tr class="cursor-pointer hover:bg-muted/50" @click="toggleAssembly(asm.name)">
                                <td class="w-8 pl-3">
                                    <ChevronDown
                                        v-if="expandedAssembly === asm.name"
                                        class="size-4 text-muted-foreground"
                                    />
                                    <ChevronRight v-else class="size-4 text-muted-foreground" />
                                </td>
                                <td class="font-medium">
                                    {{ asm.title || asm.name }}
                                </td>
                                <td class="text-right font-mono text-xs text-muted-foreground">
                                    {{ asm.fileVersion || '-' }}
                                </td>
                                <td class="text-right font-mono text-xs text-muted-foreground">
                                    {{ shortInfoVersion(asm.informationalVersion) }}
                                </td>
                            </tr>

                            <!-- 展开详情行 -->
                            <tr v-if="expandedAssembly === asm.name" class="bg-muted/30 hover:bg-muted/30">
                                <td colspan="4" class="px-8 py-3">
                                    <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1.5 text-xs">
                                        <dt class="text-muted-foreground whitespace-nowrap">
                                            {{ t('sysinfo.colName') }}
                                        </dt>
                                        <dd class="font-mono text-blue-500 break-all">{{ asm.name }}</dd>

                                        <dt class="text-muted-foreground whitespace-nowrap">
                                            {{ t('sysinfo.colFileVersion') }}
                                        </dt>
                                        <dd class="font-mono">{{ asm.fileVersion || '-' }}</dd>

                                        <dt class="text-muted-foreground whitespace-nowrap">
                                            {{ t('sysinfo.colInfoVersion') }}
                                        </dt>
                                        <dd class="font-mono">
                                            {{ shortInfoVersion(asm.informationalVersion) }}
                                            <details
                                                v-if="asm.informationalVersion?.includes('+')"
                                                class="inline-block ml-2"
                                            >
                                                <summary
                                                    class="cursor-pointer text-muted-foreground text-xs select-none"
                                                >
                                                    full hash
                                                </summary>
                                                <span class="break-all text-muted-foreground">
                                                    {{ asm.informationalVersion }}
                                                </span>
                                            </details>
                                        </dd>

                                        <dt class="text-muted-foreground whitespace-nowrap">
                                            {{ t('sysinfo.colBuildTime') }}
                                        </dt>
                                        <dd>{{ asm.buildTime ? formatDateTime(asm.buildTime) : '-' }}</dd>

                                        <template v-if="asm.description">
                                            <dt class="text-muted-foreground whitespace-nowrap">
                                                {{ t('sysinfo.colDescription') }}
                                            </dt>
                                            <dd class="text-muted-foreground">{{ asm.description }}</dd>
                                        </template>
                                    </dl>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </table>

                <!-- 分页控件 -->
                <div
                    v-if="assemblyData && assemblyData.totalCount > assemblyPageSize"
                    class="flex items-center justify-between px-4 py-3 border-t border-border/50 text-sm"
                >
                    <span class="text-muted-foreground text-xs">
                        第 {{ (assemblyPage - 1) * assemblyPageSize + 1 }}–{{
                            Math.min(assemblyPage * assemblyPageSize, assemblyData.totalCount)
                        }}
                        条，共 {{ assemblyData.totalCount }} 条
                    </span>
                    <div class="flex items-center gap-1">
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :disabled="assemblyPage <= 1 || assemblyLoading"
                            @click="assemblyPage = 1"
                        >
                            «
                        </Button>
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :disabled="assemblyPage <= 1 || assemblyLoading"
                            @click="assemblyPage--"
                        >
                            ‹
                        </Button>
                        <span class="px-3 text-muted-foreground">{{ assemblyPage }} / {{ totalPages }}</span>
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :disabled="assemblyPage >= totalPages || assemblyLoading"
                            @click="assemblyPage++"
                        >
                            ›
                        </Button>
                        <Button
                            severity="secondary"
                            outlined
                            size="small"
                            :disabled="assemblyPage >= totalPages || assemblyLoading"
                            @click="assemblyPage = totalPages"
                        >
                            »
                        </Button>
                    </div>
                </div>
            </div>
        </AppCard>
    </div>
</template>
