/**
 * Vue Router：路由表 + 鉴权守卫
 * - /login：登录页（未登录默认跳转目标）
 * - /：主布局（含侧边栏 + 顶栏），其内部为业务路由
 * - 动态路由由后续期次根据 ABP 权限系统注入
 */
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
    {
        path: '/login',
        name: 'Login',
        // 路由级懒加载
        component: () => import('@/views/login/LoginPage.vue'),
        meta: { requiresAuth: false, title: 'login.title' },
    },
    {
        path: '/',
        component: () => import('@/layouts/MainLayout.vue'),
        meta: { requiresAuth: true },
        children: [
            {
                path: '',
                redirect: '/dashboard',
            },
            {
                path: 'dashboard',
                name: 'Dashboard',
                component: () => import('@/views/dashboard/DashboardPage.vue'),
                meta: { requiresAuth: true, title: 'menu.dashboard' },
            },
            {
                path: 'embed/swagger',
                name: 'EmbedSwagger',
                // 使用自研 Swagger UI（解析 OpenAPI JSON + 在线调试）
                component: () => import('@/views/swagger/SwaggerPage.vue'),
                meta: { requiresAuth: true, title: 'menu.swagger' },
            },
            {
                path: 'embed/cap',
                name: 'EmbedCap',
                component: () => import('@/views/embed/CapPage.vue'),
                meta: { requiresAuth: true, title: 'menu.cap' },
            },
            {
                path: 'embed/hangfire',
                name: 'EmbedHangfire',
                component: () => import('@/views/embed/HangfirePage.vue'),
                meta: { requiresAuth: true, title: 'menu.hangfire' },
            },
            {
                path: 'embed/profiler',
                name: 'EmbedProfiler',
                component: () => import('@/views/embed/ProfilerPage.vue'),
                meta: { requiresAuth: true, title: 'menu.profiler' },
            },
            {
                path: 'system-info',
                name: 'SystemInfo',
                component: () => import('@/views/system/SystemInfoPage.vue'),
                meta: { requiresAuth: true, title: 'menu.systemInfo' },
            },
            {
                path: 'device-state/faults',
                name: 'FaultHistory',
                component: () => import('@/views/device-state/FaultHistoryPage.vue'),
                meta: { requiresAuth: true, title: 'menu.faultHistory' },
            },
            {
                path: 'device-state/logs',
                name: 'StateLog',
                component: () => import('@/views/device-state/StateLogPage.vue'),
                meta: { requiresAuth: true, title: 'menu.stateLog' },
            },
            {
                path: 'projectors',
                name: 'ProjectorManage',
                component: () => import('@/views/projectors/ProjectorManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.projectorManage' },
            },
            {
                path: 'projectors/logs',
                name: 'ProjectorLogs',
                component: () => import('@/views/projectors/ProjectorLogsPage.vue'),
                meta: { requiresAuth: true, title: 'menu.projectorLogs' },
            },
            {
                path: 'projectors/:id/control',
                name: 'ProjectorControl',
                component: () => import('@/views/projectors/ProjectorControlPage.vue'),
                meta: { requiresAuth: true, title: 'menu.projectorControl' },
            },
            {
                path: 'cameras',
                name: 'CameraManage',
                component: () => import('@/views/cameras/CameraManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.cameraManage' },
            },
            {
                path: 'cameras/logs',
                name: 'CameraLogs',
                component: () => import('@/views/cameras/CameraLogsPage.vue'),
                meta: { requiresAuth: true, title: 'menu.cameraLogs' },
            },
            {
                path: 'cameras/:id/control',
                name: 'CameraControl',
                component: () => import('@/views/cameras/CameraControlPage.vue'),
                meta: { requiresAuth: true, title: 'menu.cameraControl' },
            },
            {
                path: 'serial-ports',
                name: 'SerialPortManage',
                component: () => import('@/views/serial-ports/SerialPortManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.serialPortManage' },
            },
            {
                path: 'serial-ports/logs',
                name: 'SerialPortLogs',
                component: () => import('@/views/serial-ports/SerialPortLogsPage.vue'),
                meta: { requiresAuth: true, title: 'menu.serialPortLogs' },
            },
            {
                path: 'motors',
                name: 'MotorDeviceManage',
                component: () => import('@/views/motors/MotorDeviceManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.motorDeviceManage' },
            },
            {
                path: 'motors/logs',
                name: 'MotorLogs',
                component: () => import('@/views/motors/MotorLogsPage.vue'),
                meta: { requiresAuth: true, title: 'menu.motorLogs' },
            },
            {
                path: 'motors/ktech-console/:axisId',
                name: 'KtechMotorConsole',
                component: () => import('@/views/motors/KtechMotorConsolePage.vue'),
                meta: { requiresAuth: true, title: 'menu.ktechMotorConsole' },
            },
            {
                path: 'motors/leisai-console/:axisId',
                name: 'LeisaiMotorConsole',
                component: () => import('@/views/motors/LeisaiMotorConsolePage.vue'),
                meta: { requiresAuth: true, title: 'menu.leisaiMotorConsole' },
            },
            {
                path: 'product-models',
                name: 'ProductModelManage',
                component: () => import('@/views/product-models/ProductModelManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.productModelManage' },
            },
            {
                path: 'calibration/devices',
                name: 'CalibrationDevices',
                component: () => import('@/views/calibration/CalibrationDevicePage.vue'),
                meta: { requiresAuth: true, title: 'menu.calibrationDevices' },
            },
            {
                path: 'calibration/projects',
                name: 'CalibrationProjects',
                component: () => import('@/views/calibration/CalibrationProjectPage.vue'),
                meta: { requiresAuth: true, title: 'menu.calibrationProjects' },
            },
            {
                path: 'calibration/projects/:id/wizard',
                name: 'CalibrationWizard',
                component: () => import('@/views/calibration/CalibrationWizardPage.vue'),
                meta: { requiresAuth: true, title: 'menu.calibrationWizard' },
                path: 'product-models/logs',
                name: 'ProductModelLogs',
                component: () => import('@/views/product-models/ProductModelLogsPage.vue'),
                meta: { requiresAuth: true, title: 'menu.productModelLogs' },
            },
            {
                path: 'ai-models',
                name: 'AiModelManage',
                component: () => import('@/views/ai-models/AiModelManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.aiModelManage' },
            },
            {
                path: 'ai-models/logs',
                name: 'AiModelLogs',
                component: () => import('@/views/ai-models/AiModelLogsPage.vue'),
                meta: { requiresAuth: true, title: 'menu.aiModelLogs' },
            },
            {
                path: 'calibration/projects',
                name: 'CalibProjectManage',
                component: () => import('@/views/calibration/CalibProjectManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.calibProjectManage' },
            },
            {
                path: 'calibration/gimbal-groups',
                name: 'CalibGimbalGroupManage',
                component: () => import('@/views/calibration/CalibGimbalGroupManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.calibGimbalGroupManage' },
            },
            {
                path: 'calibration/projects/:id/wizard',
                name: 'CalibWizard',
                component: () => import('@/views/calibration/CalibWizardPage.vue'),
                meta: { requiresAuth: true, title: 'menu.calibWizard' },
            },
            // 系统管理路由已移除（仅供后台调试使用）
        ],
    },
    {
        path: '/:pathMatch(.*)*',
        name: 'NotFound',
        component: () => import('@/views/error/NotFoundPage.vue'),
        meta: { requiresAuth: false },
    },
]

export const router = createRouter({
    history: createWebHistory(),
    routes,
})

// 鉴权守卫：未登录访问受保护路由统一跳转 /login
router.beforeEach(async (to) => {
    // 动态导入避免循环依赖
    const { useAuthStore } = await import('@/stores/auth')
    const auth = useAuthStore()

    if (to.meta.requiresAuth && !auth.isAuthenticated) {
        return { name: 'Login', query: { redirect: to.fullPath } }
    }
    if (to.name === 'Login' && auth.isAuthenticated) {
        return { path: '/' }
    }
    return true
})

export default router
