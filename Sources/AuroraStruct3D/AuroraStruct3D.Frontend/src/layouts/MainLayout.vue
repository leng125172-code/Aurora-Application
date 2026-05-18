<script setup lang="ts">
import { onMounted } from 'vue'
import { RouterView } from 'vue-router'
import Sidebar from '@/layouts/Sidebar.vue'
import TopBar from '@/layouts/TopBar.vue'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { getApplicationConfigurationAsync } from '@/api/abp-application'

const auth = useAuthStore()
const tenant = useTenantStore()

onMounted(async () => {
    try {
        const cfg = await getApplicationConfigurationAsync()
        if (cfg.currentUser.isAuthenticated) {
            auth.setCurrentUser({
                id: cfg.currentUser.id ?? '',
                userName: cfg.currentUser.userName ?? '',
                email: cfg.currentUser.email,
                tenantId: cfg.currentUser.tenantId,
                roles: cfg.currentUser.roles ?? [],
            })
        }
        tenant.setCurrent({
            id: cfg.currentTenant.id ?? null,
            name: cfg.currentTenant.name ?? null,
            isAvailable: cfg.currentTenant.isAvailable,
        })
    } catch {
        // 错误已在 axios 拦截器中提示
    }
})
</script>

<template>
    <div class="relative z-[1] flex h-full w-full overflow-hidden">
        <Sidebar />
        <div class="flex flex-1 flex-col overflow-hidden">
            <TopBar />
            <main class="flex-1 overflow-auto p-6">
                <RouterView />
            </main>
        </div>
    </div>
</template>
