<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import Sidebar from '@/layouts/Sidebar.vue'
import TopBar from '@/layouts/TopBar.vue'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { getApplicationConfigurationAsync } from '@/api/abp-application'
import Button from 'primevue/button'

const auth = useAuthStore()
const tenant = useTenantStore()
const route = useRoute()
const mobileNavOpen = ref(false)

watch(
    () => route.fullPath,
    () => {
        mobileNavOpen.value = false
    }
)

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
        <Button
            v-if="mobileNavOpen"
            unstyled
            type="button"
            class="fixed inset-0 z-30 bg-black/45 backdrop-blur-[1px] lg:hidden"
            aria-label="关闭导航"
            @click="mobileNavOpen = false"
        />
        <div
            class="fixed inset-y-0 left-0 z-40 transition-transform duration-200 lg:static lg:z-auto lg:translate-x-0"
            :class="mobileNavOpen ? 'translate-x-0' : '-translate-x-full'"
        >
            <Sidebar />
        </div>
        <div class="flex flex-1 flex-col overflow-hidden">
            <TopBar @toggle-navigation="mobileNavOpen = !mobileNavOpen" />
            <main class="flex-1 overflow-auto p-3 sm:p-4 lg:p-6">
                <RouterView />
            </main>
        </div>
    </div>
</template>
