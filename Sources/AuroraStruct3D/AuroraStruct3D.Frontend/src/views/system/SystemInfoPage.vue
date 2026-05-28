<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { RefreshCw, Server, Package } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
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
    assemblies: AssemblyInfo[]
}

// ── 状态 ──────────────────────────────────────────────────────────────────────

const info = ref<SystemInfoResponse | null>(null)
const loading = ref(false)
const assemblySearch = ref('')

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
}

onMounted(loadData)

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

function cpuColor(pct: number): 'destructive' | 'secondary' | 'default' {
    if (pct >= 90) return 'destructive'
    if (pct >= 70) return 'secondary'
    return 'default'
}

// ── 计算属性 ──────────────────────────────────────────────────────────────────

const filteredAssemblies = computed<AssemblyInfo[]>(() => {
    if (!info.value) return []
    const q = assemblySearch.value.trim().toLowerCase()
    if (!q) return info.value.assemblies
    return info.value.assemblies.filter(
        (a) =>
            a.name.toLowerCase().includes(q) ||
            a.title.toLowerCase().includes(q) ||
            a.description.toLowerCase().includes(q)
    )
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
</script>

<template>
    <div class="space-y-5">
        <!-- 页头 -->
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('sysinfo.title') }}</h1>
            <Button variant="outline" size="sm" :disabled="loading" @click="loadData">
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
        <Card>
            <CardHeader class="pb-3">
                <CardTitle class="flex items-center gap-2 text-base">
                    <Server class="size-4" />
                    {{ t('sysinfo.server') }}
                </CardTitle>
            </CardHeader>
            <CardContent v-if="info">
                <div class="grid grid-cols-1 md:grid-cols-2 gap-x-8 gap-y-0 text-sm">
                    <!-- 左列 -->
                    <div class="divide-y divide-border/60">
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.appName') }}</span>
                            <span class="font-medium break-all">{{ info.server.applicationName }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.os') }}</span>
                            <span class="break-all">{{ info.server.osDescription }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.processor') }}</span>
                            <span class="break-all">
                                {{ info.server.processorModel }}，{{ info.server.processorCount }} 核
                                <Badge
                                    v-if="info.server.cpuPercent >= 0"
                                    :variant="cpuColor(info.server.cpuPercent)"
                                    class="ml-1.5 text-xs"
                                >
                                    {{ info.server.cpuPercent.toFixed(1) }}%
                                </Badge>
                            </span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.memory') }}</span>
                            <span>
                                {{ formatBytes(info.server.memoryUsedBytes) }} /
                                {{ formatBytes(info.server.memoryTotalBytes) }}
                                <Badge
                                    :variant="
                                        memPercent >= 90 ? 'destructive' : memPercent >= 70 ? 'secondary' : 'default'
                                    "
                                    class="ml-1.5 text-xs"
                                >
                                    {{ memPercent }}%
                                </Badge>
                            </span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.directory') }}</span>
                            <span class="break-all font-mono text-xs leading-5">{{ info.server.contentRootPath }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.processName') }}</span>
                            <span class="font-mono">{{ info.server.processName }}</span>
                        </div>
                    </div>

                    <!-- 右列 -->
                    <div class="divide-y divide-border/60">
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.dotnetVersion') }}</span>
                            <span>{{ info.server.dotNetVersion }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.machine') }}</span>
                            <span>{{ info.server.machineName }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.user') }}</span>
                            <span>{{ info.server.userName }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.serverTime') }}</span>
                            <span>{{ formatDateTime(info.server.serverTime) }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.processStart') }}</span>
                            <span>{{ formatDateTime(info.server.processStartTime) }}</span>
                        </div>
                        <div class="flex items-start py-2.5 gap-3">
                            <span class="w-28 shrink-0 text-muted-foreground">{{ t('sysinfo.uptime') }}</span>
                            <span class="font-medium">{{ info.server.uptimeText }}</span>
                        </div>
                    </div>
                </div>
            </CardContent>
            <CardContent v-else class="py-8 text-center text-sm text-muted-foreground">
                {{ loading ? t('common.loading') : '-' }}
            </CardContent>
        </Card>

        <!-- 已加载程序集卡 -->
        <Card>
            <CardHeader class="pb-3">
                <div class="flex items-center justify-between gap-4">
                    <CardTitle class="flex items-center gap-2 text-base">
                        <Package class="size-4" />
                        {{ t('sysinfo.assemblies') }}
                        <Badge variant="secondary" class="ml-1">
                            {{ filteredAssemblies.length }}
                        </Badge>
                    </CardTitle>
                    <Input
                        v-model="assemblySearch"
                        :placeholder="t('sysinfo.searchAssembly')"
                        class="h-8 w-56 text-sm"
                    />
                </div>
            </CardHeader>
            <CardContent class="p-0">
                <div class="overflow-x-auto">
                    <table class="w-full text-sm">
                        <thead>
                            <tr class="border-b bg-muted/30 text-muted-foreground text-xs">
                                <th class="px-4 py-2.5 text-left font-medium w-52">{{ t('sysinfo.colName') }}</th>
                                <th class="px-4 py-2.5 text-left font-medium w-40">{{ t('sysinfo.colTitle') }}</th>
                                <th class="px-4 py-2.5 text-left font-medium w-28">
                                    {{ t('sysinfo.colFileVersion') }}
                                </th>
                                <th class="px-4 py-2.5 text-left font-medium w-36">
                                    {{ t('sysinfo.colInfoVersion') }}
                                </th>
                                <th class="px-4 py-2.5 text-left font-medium w-40">{{ t('sysinfo.colBuildTime') }}</th>
                                <th class="px-4 py-2.5 text-left font-medium">{{ t('sysinfo.colDescription') }}</th>
                            </tr>
                        </thead>
                        <tbody class="divide-y divide-border/50">
                            <tr
                                v-for="asm in filteredAssemblies"
                                :key="asm.name"
                                class="hover:bg-muted/20 transition-colors"
                            >
                                <td class="px-4 py-2 font-mono text-xs text-blue-500 break-all">
                                    {{ asm.name }}
                                </td>
                                <td class="px-4 py-2 text-xs">{{ asm.title || '-' }}</td>
                                <td class="px-4 py-2 font-mono text-xs">{{ asm.fileVersion || '-' }}</td>
                                <td class="px-4 py-2 font-mono text-xs">{{ asm.informationalVersion || '-' }}</td>
                                <td class="px-4 py-2 text-xs text-muted-foreground">
                                    {{ asm.buildTime ? formatDateTime(asm.buildTime) : '-' }}
                                </td>
                                <td class="px-4 py-2 text-xs text-muted-foreground max-w-xs truncate">
                                    {{ asm.description || '-' }}
                                </td>
                            </tr>
                            <tr v-if="filteredAssemblies.length === 0">
                                <td colspan="6" class="px-4 py-8 text-center text-muted-foreground text-sm">
                                    {{ t('management.noData') }}
                                </td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </CardContent>
        </Card>
    </div>
</template>
