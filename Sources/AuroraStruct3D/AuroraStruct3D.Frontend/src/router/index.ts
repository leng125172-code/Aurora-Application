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
                path: 'motors',
                name: 'MotorDeviceManage',
                component: () => import('@/views/motors/MotorDeviceManagePage.vue'),
                meta: { requiresAuth: true, title: 'menu.motorDeviceManage' },
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
