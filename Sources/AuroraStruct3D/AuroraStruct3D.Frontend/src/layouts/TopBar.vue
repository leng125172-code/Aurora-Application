<script setup lang="ts">
import { LogOut, User } from 'lucide-vue-next'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuItem,
} from '@/components/ui/dropdown-menu'
import ThemeToggle from '@/components/ThemeToggle.vue'
import LangSwitcher from '@/components/LangSwitcher.vue'
import DeviceStatusBadge from '@/components/device-state/DeviceStatusBadge.vue'
import DeviceModeSwitcher from '@/components/device-state/DeviceModeSwitcher.vue'
import { useAuthStore } from '@/stores/auth'
import { useDeviceStateStore } from '@/stores/deviceState'
import { logoutAsync } from '@/api/auth'

const auth = useAuthStore()
const deviceStateStore = useDeviceStateStore()
const router = useRouter()
const { t } = useI18n()

async function handleLogout(): Promise<void> {
    await logoutAsync()
    auth.reset()
    await router.push({ name: 'Login' })
}
</script>

<template>
    <header class="flex h-14 items-center justify-between border-b bg-card/40 px-4 backdrop-blur">
        <div class="text-sm text-muted-foreground">
            {{ auth.currentUser?.userName ?? '' }}
        </div>
        <div class="flex items-center gap-2">
            <!-- 设备状态徽章 + 模式切换 -->
            <DeviceStatusBadge :status="deviceStateStore.state?.status" />
            <DeviceModeSwitcher :device-state="deviceStateStore.state" />
            <LangSwitcher />
            <ThemeToggle />
            <DropdownMenu>
                <DropdownMenuTrigger as-child>
                    <Button variant="ghost" size="icon" :title="t('layout.profile')">
                        <User />
                    </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-48">
                    <DropdownMenuLabel>
                        {{ auth.currentUser?.userName ?? '-' }}
                    </DropdownMenuLabel>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem @select="handleLogout">
                        <LogOut class="mr-2 size-4" />
                        {{ t('layout.logout') }}
                    </DropdownMenuItem>
                </DropdownMenuContent>
            </DropdownMenu>
        </div>
    </header>
</template>
