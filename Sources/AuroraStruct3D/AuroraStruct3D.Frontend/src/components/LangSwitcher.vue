<script setup lang="ts">
/**
 * 语言切换器：支持 5 种 locale
 * 使用 PrimeVue Button + Menu(popup 模式)
 */
import { ref, computed } from 'vue'
import { Languages } from '@lucide/vue'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import type { MenuItem } from 'primevue/menuitem'
import { setLocale, type SupportedLocale } from '@/i18n'

const { locale } = useI18n()

interface LangItem {
    readonly code: SupportedLocale
    readonly label: string
}

const langs: readonly LangItem[] = [
    { code: 'zh-CN', label: '简体中文' },
    { code: 'en-US', label: 'English' },
    { code: 'ko-KR', label: '한국어' },
    { code: 'ja-JP', label: '日本語' },
    { code: 'de-DE', label: 'Deutsch' },
]

const menuRef = ref<InstanceType<typeof Menu> | null>(null)

const items = computed<MenuItem[]>(() =>
    langs.map((lang) => ({
        label: lang.label,
        data: { code: lang.code },
        command: () => setLocale(lang.code),
    }))
)

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
        :aria-label="locale"
        v-tooltip.bottom="locale"
        @click="togglePopup"
    >
        <template #icon>
            <Languages class="size-4" />
        </template>
    </Button>
    <Menu ref="menuRef" :model="items" :popup="true">
        <template #item="{ item, props: itemProps }">
            <a
                v-ripple
                v-bind="itemProps.action"
                :class="[
                    'flex items-center',
                    locale === (item.data as { code: SupportedLocale }).code && 'font-semibold text-primary',
                ]"
            >
                <span>{{ item.label }}</span>
            </a>
        </template>
    </Menu>
</template>
