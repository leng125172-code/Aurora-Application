<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import {
    Card,
    CardHeader,
    CardTitle,
    CardDescription,
    CardContent,
} from '@/components/ui/card'
import { NumberTicker } from '@/components/ui/number-ticker'
import { BorderBeam } from '@/components/ui/border-beam'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'

const { t } = useI18n()
const auth = useAuthStore()
const tenant = useTenantStore()

const roleCount = computed(() => auth.currentUser?.roles?.length ?? 0)
</script>

<template>
    <div class="space-y-6">
        <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.dashboard') }}</h1>

        <div class="grid gap-4 md:grid-cols-3">
            <Card class="relative overflow-hidden">
                <BorderBeam :size="160" :duration="10" />
                <CardHeader>
                    <CardTitle class="text-base">用户</CardTitle>
                    <CardDescription>当前登录用户</CardDescription>
                </CardHeader>
                <CardContent class="text-lg font-medium">
                    {{ auth.currentUser?.userName ?? '-' }}
                </CardContent>
            </Card>

            <Card class="relative overflow-hidden">
                <BorderBeam :size="160" :duration="10" :delay="3" />
                <CardHeader>
                    <CardTitle class="text-base">租户</CardTitle>
                    <CardDescription>当前会话租户</CardDescription>
                </CardHeader>
                <CardContent class="text-lg font-medium">
                    {{ tenant.current?.name ?? 'Host' }}
                </CardContent>
            </Card>

            <Card class="relative overflow-hidden">
                <BorderBeam :size="160" :duration="10" :delay="6" />
                <CardHeader>
                    <CardTitle class="text-base">角色数量</CardTitle>
                    <CardDescription>当前用户拥有的角色总数</CardDescription>
                </CardHeader>
                <CardContent class="text-3xl font-semibold">
                    <NumberTicker :value="roleCount" />
                </CardContent>
            </Card>
        </div>
    </div>
</template>
