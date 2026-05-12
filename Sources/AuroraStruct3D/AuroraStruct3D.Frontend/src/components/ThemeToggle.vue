<script setup lang="ts">
import { Moon, Sun, Monitor } from 'lucide-vue-next'
import { Button } from '@/components/ui/button'
import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuRadioGroup,
    DropdownMenuRadioItem,
} from '@/components/ui/dropdown-menu'
import { useThemeStore, type ThemeMode } from '@/stores/theme'
import { useI18n } from 'vue-i18n'

const theme = useThemeStore()
const { t } = useI18n()

function onSelect(value: string): void {
    theme.setMode(value as ThemeMode)
}
</script>

<template>
    <DropdownMenu>
        <DropdownMenuTrigger as-child>
            <Button variant="ghost" size="icon" :title="t('layout.switchTheme')">
                <Sun v-if="theme.mode === 'light'" />
                <Moon v-else-if="theme.mode === 'dark'" />
                <Monitor v-else />
            </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
            <DropdownMenuRadioGroup :model-value="theme.mode" @update:model-value="onSelect">
                <DropdownMenuRadioItem value="light">
                    <Sun class="mr-2 size-4" />
                    {{ t('layout.themeLight') }}
                </DropdownMenuRadioItem>
                <DropdownMenuRadioItem value="dark">
                    <Moon class="mr-2 size-4" />
                    {{ t('layout.themeDark') }}
                </DropdownMenuRadioItem>
                <DropdownMenuRadioItem value="system">
                    <Monitor class="mr-2 size-4" />
                    {{ t('layout.themeSystem') }}
                </DropdownMenuRadioItem>
            </DropdownMenuRadioGroup>
        </DropdownMenuContent>
    </DropdownMenu>
</template>
