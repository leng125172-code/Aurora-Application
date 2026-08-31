<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import {
    LayoutDashboard,
    FileText,
    MessageSquare,
    Activity,
    Gauge,
    Info,
    AlertTriangle,
    ScrollText,
    Monitor,
    Camera,
    Cable,
    Cpu,
    Box,
    ScanLine,
    GitBranch,
    FolderKanban,
    ChevronDown,
    ChevronRight,
    Users,
    ShieldCheck,
} from '@lucide/vue'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const auth = useAuthStore()
const workflowDebugEnabled = __WORKFLOW_DEBUG__

// 展开状态：CAP 和 Hangfire 默认展开（若当前路由匹配）
const capExpanded = ref(route.path.startsWith('/embed/cap'))
const hangfireExpanded = ref(route.path.startsWith('/embed/hangfire'))
const calibExpanded = ref(route.path.startsWith('/calibration'))
const projectorExpanded = ref(route.path.startsWith('/projectors'))
const cameraExpanded = ref(route.path.startsWith('/cameras'))
const serialPortExpanded = ref(route.path.startsWith('/serial-ports'))
const motorExpanded = ref(route.path.startsWith('/motors'))
const productModelExpanded = ref(route.path.startsWith('/product-models'))
const aiModelExpanded = ref(route.path.startsWith('/ai-models'))

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
    <aside data-testid="app-sidebar" class="flex h-full w-64 flex-col border-r bg-card/95 backdrop-blur-xl lg:w-60">
        <div class="flex h-14 shrink-0 items-center gap-2 border-b px-4 text-sm font-bold tracking-wide">
            <span class="size-2 rounded-sm bg-primary shadow-[0_0_10px_hsl(var(--primary)/0.55)]" />
            AuroraStruct3D
        </div>
        <nav class="sidebar-nav flex-1 space-y-1 overflow-y-auto p-2">
            <div v-if="auth.canOperate" class="px-3 py-1 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                {{ t('menu.visualApplications') }}
            </div>
            <Button
                v-if="auth.canOperate"
                size="small"
                text
                severity="secondary"
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        isExactActive('/projects')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/projects')"
            >
                <FolderKanban class="size-4 shrink-0" />
                {{ t('menu.visualSolutions') }}
            </Button>

            <!-- 工具监控 -->
            <div v-if="auth.canManage" class="px-3 py-1 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                {{ t('menu.tools') }}
            </div>

            <!-- 仪表盘 -->
            <Button
                v-if="auth.canOperate"
                size="small"
                text
                severity="secondary"
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
            </Button>

            <Button
                size="small"
                text
                severity="secondary"
                v-if="workflowDebugEnabled && auth.canManage"
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        isExactActive('/workflow-ide')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/workflow-ide')"
            >
                <GitBranch class="size-4 shrink-0" />
                工作流调试
            </Button>

            <!-- Swagger -->
            <Button
                v-if="auth.canManage"
                size="small"
                text
                severity="secondary"
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
            </Button>

            <!-- CAP 展开组 -->
            <div v-if="auth.canManage">
                <Button
                    size="small"
                    text
                    severity="secondary"
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
                </Button>
                <div v-if="capExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
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
                    </Button>
                </div>
            </div>

            <!-- Hangfire 展开组 -->
            <div v-if="auth.canManage">
                <Button
                    size="small"
                    text
                    severity="secondary"
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
                </Button>
                <div v-if="hangfireExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
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
                    </Button>
                </div>
            </div>

            <!-- MiniProfiler -->
            <Button
                v-if="auth.canManage"
                size="small"
                text
                severity="secondary"
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
            </Button>

            <!-- 系统信息 -->
            <Button
                v-if="auth.canManage"
                size="small"
                text
                severity="secondary"
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
            </Button>

            <!-- 设备状态 -->
            <div v-if="auth.canOperate" class="px-3 py-1 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                {{ t('menu.deviceState') }}
            </div>

            <!-- 故障历史 -->
            <Button
                v-if="auth.canOperate"
                size="small"
                text
                severity="secondary"
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        isExactActive('/device-state/faults')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/device-state/faults')"
            >
                <AlertTriangle class="size-4 shrink-0" />
                {{ t('menu.faultHistory') }}
            </Button>

            <!-- 状态日志 -->
            <Button
                v-if="auth.canOperate"
                size="small"
                text
                severity="secondary"
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        isExactActive('/device-state/logs')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/device-state/logs')"
            >
                <ScrollText class="size-4 shrink-0" />
                {{ t('menu.stateLog') }}
            </Button>

            <!-- 投影仪管理展开组 -->
            <div v-if="auth.canOperate">
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/projectors')
                                ? 'text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="projectorExpanded = !projectorExpanded"
                >
                    <Monitor class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.projectorManage') }}</span>
                    <ChevronDown v-if="projectorExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </Button>
                <div v-if="projectorExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/projectors')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/projectors')"
                    >
                        {{ t('menu.management') }}
                    </Button>
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/projectors/logs')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/projectors/logs')"
                    >
                        {{ t('menu.operationLogs') }}
                    </Button>
                </div>
            </div>

            <!-- 相机管理展开组 -->
            <div v-if="auth.canOperate">
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/cameras')
                                ? 'text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="cameraExpanded = !cameraExpanded"
                >
                    <Camera class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.cameraManage') }}</span>
                    <ChevronDown v-if="cameraExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </Button>
                <div v-if="cameraExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/cameras')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/cameras')"
                    >
                        {{ t('menu.management') }}
                    </Button>
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/cameras/logs')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/cameras/logs')"
                    >
                        {{ t('menu.operationLogs') }}
                    </Button>
                </div>
            </div>

            <!-- 485 串口管理 -->
            <div v-if="auth.canOperate">
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/serial-ports')
                                ? 'text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="serialPortExpanded = !serialPortExpanded"
                >
                    <Cable class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.serialPortManage') }}</span>
                    <ChevronDown v-if="serialPortExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </Button>
                <div v-if="serialPortExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/serial-ports')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/serial-ports')"
                    >
                        {{ t('menu.management') }}
                    </Button>
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/serial-ports/logs')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/serial-ports/logs')"
                    >
                        {{ t('menu.operationLogs') }}
                    </Button>
                </div>
            </div>

            <!-- 485 电机设备管理 -->
            <div v-if="auth.canOperate">
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/motors')
                                ? 'text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="motorExpanded = !motorExpanded"
                >
                    <Cpu class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.motorDeviceManage') }}</span>
                    <ChevronDown v-if="motorExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </Button>
                <div v-if="motorExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/motors')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/motors')"
                    >
                        {{ t('menu.management') }}
                    </Button>
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/motors/logs')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/motors/logs')"
                    >
                        {{ t('menu.operationLogs') }}
                    </Button>
                </div>
            </div>

            <!-- 三维数模管理 -->
            <Button
                v-if="auth.canOperate"
                size="small"
                text
                severity="secondary"
                :class="
                    cn(
                        'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                        route.path.startsWith('/plcs')
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                    )
                "
                @click="navigate('/plcs')"
            >
                <Cable class="size-4 shrink-0" />
                {{ t('menu.plcManage') }}
            </Button>

            <!-- 三维数模管理 -->
            <div v-if="auth.canOperate">
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/product-models')
                                ? 'text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="productModelExpanded = !productModelExpanded"
                >
                    <Box class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.productModelManage') }}</span>
                    <ChevronDown v-if="productModelExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </Button>
                <div v-if="productModelExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/product-models')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/product-models')"
                    >
                        {{ t('menu.management') }}
                    </Button>
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/product-models/logs')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/product-models/logs')"
                    >
                        {{ t('menu.operationLogs') }}
                    </Button>
                </div>
            </div>

            <!-- AI 模型管理 -->
            <div v-if="auth.canOperate">
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/ai-models')
                                ? 'text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="aiModelExpanded = !aiModelExpanded"
                >
                    <Cpu class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.aiModelManage') }}</span>
                    <ChevronDown v-if="aiModelExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </Button>
                <div v-if="aiModelExpanded" class="ml-6 mt-0.5 space-y-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/ai-models')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/ai-models')"
                    >
                        {{ t('menu.management') }}
                    </Button>
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/ai-models/logs')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/ai-models/logs')"
                    >
                        {{ t('menu.operationLogs') }}
                    </Button>
                </div>
            </div>

            <!-- 标定管理 可展开菜单 -->
            <div v-if="auth.canOperate">
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            route.path.startsWith('/calibration')
                                ? 'bg-accent text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="calibExpanded = !calibExpanded"
                >
                    <ScanLine class="size-4 shrink-0" />
                    <span class="flex-1">{{ t('menu.calibration') }}</span>
                    <ChevronDown v-if="calibExpanded" class="size-3.5" />
                    <ChevronRight v-else class="size-3.5" />
                </Button>
                <div v-if="calibExpanded" class="ml-6 mt-0.5 flex flex-col gap-0.5">
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        :class="
                            cn(
                                'flex w-full items-center gap-2 rounded-md px-3 py-1.5 text-sm transition-colors text-left',
                                isExactActive('/calibration/projects')
                                    ? 'bg-accent text-accent-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                            )
                        "
                        @click="navigate('/calibration/projects')"
                    >
                        <ScanLine class="size-3.5 shrink-0" />
                        {{ t('menu.calibProjectManage') }}
                    </Button>
                </div>
            </div>

            <template v-if="auth.canManage">
                <div class="px-3 py-1 text-xs font-medium text-muted-foreground uppercase tracking-wider">
                    {{ t('menu.system') }}
                </div>
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            isExactActive('/system/users')
                                ? 'bg-accent text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="navigate('/system/users')"
                >
                    <Users class="size-4 shrink-0" />
                    {{ t('menu.users') }}
                </Button>
                <Button
                    size="small"
                    text
                    severity="secondary"
                    :class="
                        cn(
                            'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors text-left',
                            isExactActive('/system/roles')
                                ? 'bg-accent text-accent-foreground'
                                : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                        )
                    "
                    @click="navigate('/system/roles')"
                >
                    <ShieldCheck class="size-4 shrink-0" />
                    {{ t('menu.roles') }}
                </Button>
            </template>
        </nav>
    </aside>
</template>

<style scoped>
.sidebar-nav :deep(.p-button) {
    justify-content: flex-start;
    text-align: left;
}
</style>
