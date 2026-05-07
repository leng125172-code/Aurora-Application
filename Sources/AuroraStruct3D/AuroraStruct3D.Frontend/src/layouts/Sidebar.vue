<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import {
    LayoutDashboard,
    FileText,
    MessageSquare,
    Activity,
    Gauge,
} from 'lucide-vue-next'
import { cn } from '@/lib/utils'

interface MenuItem {
    readonly key: string
    readonly to: string
    readonly icon: unknown
}

const items: readonly MenuItem[] = [
    { key: 'menu.dashboard', to: '/dashboard', icon: LayoutDashboard },
    { key: 'menu.swagger', to: '/embed/swagger', icon: FileText },
    { key: 'menu.cap', to: '/embed/cap', icon: MessageSquare },
    { key: 'menu.hangfire', to: '/embed/hangfire', icon: Activity },
    { key: 'menu.profiler', to: '/embed/profiler', icon: Gauge },
]

const route = useRoute()
const { t } = useI18n()

const activePath = computed(() => route.path)
</script>

<template>
    <aside class="flex h-full w-60 flex-col border-r bg-card/40 backdrop-blur">
        <div class="flex h-14 items-center border-b px-4 text-base font-semibold">
            AuroraStruct3D
        </div>
        <nav class="flex-1 space-y-1 overflow-y-auto p-2">
            <RouterLink
                v-for="item in items"
                :key="item.key"
                :to="item.to"
                :class="
                    cn(
                        'flex items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors',
                        activePath === item.to
                            ? 'bg-accent text-accent-foreground'
                            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground',
                    )
                "
            >
                <component :is="item.icon" class="size-4" />
                {{ t(item.key) }}
            </RouterLink>
        </nav>
    </aside>
</template>
