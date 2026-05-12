<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import {
    LayoutDashboard,
    FileText,
    MessageSquare,
    Activity,
    Gauge,
    Info,
    ChevronDown,
    ChevronRight,
} from 'lucide-vue-next'
import { cn } from '@/lib/utils'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()

// 展开状态：CAP 和 Hangfire 默认展开（若当前路由匹配）
const capExpanded = ref(route.path.startsWith('/embed/cap'))
const hangfireExpanded = ref(route.path.startsWith('/embed/hangfire'))

function isCapActive(tab: string): boolean {
    if (!route.path.startsWith('/embed/cap')) return false
    const current = (route.query.tab as string) || 'dashboard'
    return current === tab
}

function isHangfireActive(tab: string): boolean {
    if (!route.path.startsWith('/embed/hangfire')) return false
    const current = (route.query.tab as string) || 'dashboard'
    return current === tab
}

function isExactActive(path: string): boolean {
    return route.path === path
}

function navigate(path: string, tab?: string): void {
    if (tab) {
        void router.push({ path, query: { tab } })
    } else {
        void router.push(path)
    }
}
</script>

<template>
    <aside class="flex h-full w-56 flex-col border-r bg-card/40 backdrop-blur">
        <div class="flex h-14 items-center border-b px-4 text-base font-semibold">AuroraStruct3D</div>
        <nav class="flex-1 space-y-1 overflow-y-auto p-2">
            <!-- 工具监控 -->
            <div class="px-3 py-1 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                {{ t('menu.tools') }}
            </div>

            <!-- 仪表盘 -->
            <button
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        isExactActive('/dashboard')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/dashboard')"
            >
                <LayoutDashboard class="size-4 shrink-0" />
                {{ t('menu.dashboard') }}
            </button>

            <!-- Swagger -->
            <button
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        isExactActive('/embed/swagger')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/embed/swagger')"
            >
                <FileText class="size-4 shrink-0" />
                {{ t('menu.swagger') }}
            </button>

            <!-- CAP 展开组 -->
            <div>
                <button
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/embed/cap')
                                ? 'text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="capExpanded = !capExpanded"
                >
                    <MessageSquare class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.cap') }}</span>
                    <ChevronDown v-if="capExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </button>
                <div v-if="capExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <button
                        v-for="(label, tab) in {
                            dashboard: t('cap.tabDashboard'),
                            published: t('cap.tabPublished'),
                            received: t('cap.tabReceived'),
                            subscribers: t('cap.tabSubscribers'),
                            nodes: t('cap.tabNodes'),
                        }"
                        :key="tab"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isCapActive(tab)
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/embed/cap', tab)"
                    >
                        {{ label }}
                    </button>
                </div>
            </div>

            <!-- Hangfire 展开组 -->
            <div>
                <button
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/embed/hangfire')
                                ? 'text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="hangfireExpanded = !hangfireExpanded"
                >
                    <Activity class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.hangfire') }}</span>
                    <ChevronDown v-if="hangfireExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </button>
                <div v-if="hangfireExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <button
                        v-for="(label, tab) in {
                            dashboard: t('hangfire.tabDashboard'),
                            jobs: t('hangfire.tabJobs'),
                            retries: t('hangfire.tabRetries'),
                            recurring: t('hangfire.tabRecurring'),
                            servers: t('hangfire.tabServers'),
                        }"
                        :key="tab"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isHangfireActive(tab)
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/embed/hangfire', tab)"
                    >
                        {{ label }}
                    </button>
                </div>
            </div>

            <!-- MiniProfiler -->
            <button
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        isExactActive('/embed/profiler')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/embed/profiler')"
            >
                <Gauge class="size-4 shrink-0" />
                {{ t('menu.profiler') }}
            </button>

            <!-- 系统信息 -->
            <button
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        isExactActive('/system-info')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/system-info')"
            >
                <Info class="size-4 shrink-0" />
                {{ t('menu.systemInfo') }}
            </button>
        </nav>
    </aside>
</template>
