/**
 * 主题状态管理：light / dark / system
 * - 通过给 <html> 添加/移除 'dark' 类切换主题（与 tailwind.config.ts 中 darkMode:'class' 配套）
 */
import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export type ThemeMode = 'light' | 'dark' | 'system'

const STORAGE_KEY = 'aurora.theme'

function applyTheme(mode: ThemeMode): void {
    const isDark = mode === 'dark' || (mode === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)
    document.documentElement.classList.toggle('dark', isDark)
}

export const useThemeStore = defineStore('theme', () => {
    const mode = ref<ThemeMode>((localStorage.getItem(STORAGE_KEY) as ThemeMode) || 'system')

    // 监听变更，自动持久化并应用
    watch(
        mode,
        (next) => {
            localStorage.setItem(STORAGE_KEY, next)
            applyTheme(next)
        },
        { immediate: true }
    )

    // 跟随系统时也要响应系统级变化
    if (window.matchMedia) {
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
            if (mode.value === 'system') applyTheme('system')
        })
    }

    function setMode(next: ThemeMode): void {
        mode.value = next
    }

    return { mode, setMode }
})
