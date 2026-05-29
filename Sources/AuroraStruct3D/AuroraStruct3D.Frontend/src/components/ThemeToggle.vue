<script setup lang="ts">
/**
 * 主题切换器：light / dark / system 三态切换
 * 使用 PrimeVue Button + Menu(popup 模式) 实现下拉，
 * 图标仍使用 @lucide/vue，保持 Aurora 视觉一致性。
 */
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Moon, Sun, Monitor } from '@lucide/vue'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import type { MenuItem } from 'primevue/menuitem'
import { useThemeStore, type ThemeMode } from '@/stores/theme'

const theme = useThemeStore()
const { t } = useI18n()

const menuRef = ref<InstanceType<typeof Menu> | null>(null)

/** 构造下拉菜单项；选中态由模板 #item 槽通过 data.mode 高亮 */
const items = computed<MenuItem[]>(() => [
    {
        label: t('layout.themeLight'),
        data: { mode: 'light' as ThemeMode },
        command: () => theme.setMode('light'),
    },
    {
        label: t('layout.themeDark'),
        data: { mode: 'dark' as ThemeMode },
        command: () => theme.setMode('dark'),
    },
    {
        label: t('layout.themeSystem'),
        data: { mode: 'system' as ThemeMode },
        command: () => theme.setMode('system'),
    },
])

function togglePopup(event: Event): void {
    menuRef.value?.toggle(event)
}
</script>

<template>
    <Button
        type="button"
        severity="secondary"
        text
        rounded
        :aria-label="t('layout.switchTheme')"
        v-tooltip.bottom="t('layout.switchTheme')"
        @click="togglePopup"
    >
        <template #icon>
            <Sun v-if="theme.mode === 'light'" class="size-4" />
            <Moon v-else-if="theme.mode === 'dark'" class="size-4" />
            <Monitor v-else class="size-4" />
        </template>
    </Button>
    <Menu ref="menuRef" :model="items" :popup="true">
        <template #item="{ item, props: itemProps }">
            <a
                v-ripple
                v-bind="itemProps.action"
                :class="[
                    'flex items-center gap-2',
                    theme.mode === (item.data as { mode: ThemeMode }).mode && 'font-semibold text-primary',
                ]"
            >
                <Sun v-if="(item.data as { mode: ThemeMode }).mode === 'light'" class="size-4" />
                <Moon v-else-if="(item.data as { mode: ThemeMode }).mode === 'dark'" class="size-4" />
                <Monitor v-else class="size-4" />
                <span>{{ item.label }}</span>
            </a>
        </template>
    </Menu>
</template>
