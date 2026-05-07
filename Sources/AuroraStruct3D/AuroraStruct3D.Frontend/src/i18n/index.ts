/**
 * vue-i18n 初始化：5 种语言
 * 默认 zh-CN，回退 en-US；从 localStorage 读取用户偏好
 */
import { createI18n } from 'vue-i18n'

import deDE from '@/i18n/locales/de-DE'
import enUS from '@/i18n/locales/en-US'
import jaJP from '@/i18n/locales/ja-JP'
import koKR from '@/i18n/locales/ko-KR'
import zhCN from '@/i18n/locales/zh-CN'

export type SupportedLocale = 'zh-CN' | 'en-US' | 'ko-KR' | 'ja-JP' | 'de-DE'

const STORAGE_KEY = 'aurora.culture'

function detectLocale(): SupportedLocale {
    const saved = localStorage.getItem(STORAGE_KEY) as SupportedLocale | null
    if (saved) return saved
    const nav = navigator.language
    if (nav.startsWith('zh')) return 'zh-CN'
    if (nav.startsWith('ko')) return 'ko-KR'
    if (nav.startsWith('ja')) return 'ja-JP'
    if (nav.startsWith('de')) return 'de-DE'
    return 'en-US'
}

export const i18n = createI18n({
    legacy: false,
    locale: detectLocale(),
    fallbackLocale: 'en-US',
    messages: {
        'zh-CN': zhCN,
        'en-US': enUS,
        'ko-KR': koKR,
        'ja-JP': jaJP,
        'de-DE': deDE,
    },
})

/** 切换语言并持久化 */
export function setLocale(locale: SupportedLocale): void {
    i18n.global.locale.value = locale
    localStorage.setItem(STORAGE_KEY, locale)
    document.documentElement.lang = locale
}
