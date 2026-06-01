<script setup lang="ts">
import { ref, computed } from 'vue'
import { LogOut, User } from '@lucide/vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import type { MenuItem } from 'primevue/menuitem'
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

const profileMenuRef = ref<InstanceType<typeof Menu> | null>(null)

async function handleLogout(): Promise<void> {
    await logoutAsync()
    auth.reset()
    await router.push({ name: 'Login' })
}

/** 用户头像下拉项 */
const profileItems = computed<MenuItem[]>(() => [
    {
        label: auth.currentUser?.userName ?? '-',
        disabled: true,
        data: { kind: 'label' as const },
    },
    { separator: true },
    {
        label: t('layout.logout'),
        data: { kind: 'logout' as const },
        command: () => void handleLogout(),
    },
])

function toggleProfile(event: Event): void {
    profileMenuRef.value?.toggle(event)
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
            <Button
                type="button"
                severity="secondary"
                text
                :aria-label="t('layout.profile')"
                v-tooltip.bottom="t('layout.profile')"
                @click="toggleProfile"
            >
                <template #icon>
                    <User class="size-4" />
                </template>
            </Button>
            <Menu ref="profileMenuRef" :model="profileItems" :popup="true">
                <template #item="{ item, props: itemProps }">
                    <a
                        v-ripple
                        v-bind="itemProps.action"
                        :class="[
                            'flex items-center gap-2',
                            (item.data as { kind: string } | undefined)?.kind === 'label' && 'font-medium',
                        ]"
                    >
                        <LogOut v-if="(item.data as { kind: string } | undefined)?.kind === 'logout'" class="size-4" />
                        <span>{{ item.label }}</span>
                    </a>
                </template>
            </Menu>
        </div>
    </header>
</template>
