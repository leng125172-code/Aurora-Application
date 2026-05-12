<script setup lang="ts">
import { Languages } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuRadioGroup,
    DropdownMenuRadioItem,
} from '@/components/ui/dropdown-menu'
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

function onSelect(value: string): void {
    setLocale(value as SupportedLocale)
}
</script>

<template>
    <DropdownMenu>
        <DropdownMenuTrigger as-child>
            <Button variant="ghost" size="icon" :title="locale">
                <Languages />
            </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
            <DropdownMenuRadioGroup :model-value="locale" @update:model-value="onSelect">
                <DropdownMenuRadioItem v-for="lang in langs" :key="lang.code" :value="lang.code">
                    {{ lang.label }}
                </DropdownMenuRadioItem>
            </DropdownMenuRadioGroup>
        </DropdownMenuContent>
    </DropdownMenu>
</template>
