/**
 * Aurora 项目 PrimeVue 主题预设
 * - 基于 Aura，主色替换为「暖调香槟金」色阶（500 = #D4B96A）
 * - 浅色模式：暖香槟金 + 白/浅米
 * - 深色模式：复古暗金 + 深黑/暗灰
 * - 全局字号/间距收紧至紧凑密度，强化金色特征
 */
import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

// 暖调香槟金色阶（手工调色：50 最浅 → 950 最深，过渡更自然）
const champagneGold = {
    50: '#F9F5E8',   // 极浅金（背景）
    100: '#F2E9C9',  // 浅金（hover/辅助）
    200: '#E8D99B',  // 浅中金（border/分隔）
    300: '#DCC478',  // 中金（highlight）
    400: '#D4B96A',  // 暖香槟金（浅色模式主色）
    500: '#C8A858',  // 深香槟金（hover）
    600: '#B59745',  // 深金（active）
    700: '#9F8438',  // 暗金（深色模式辅助）
    800: '#947D58',  // 复古暗金（深色模式主色）
    900: '#7A6745',  // 深暗金（hover）
    950: '#4A3F2B',  // 极暗金（border/分隔）
}

export const AuroraPreset = definePreset(Aura, {
    semantic: {
        primary: champagneGold,
        colorScheme: {
            light: {
                primary: {
                    color: '{primary.400}',      // 暖香槟金（核心主色）
                    contrastColor: '#ffffff',    // 白色文字
                    hoverColor: '{primary.500}', // 深香槟金（hover）
                    activeColor: '{primary.600}',// 深金（active）
                },
                highlight: {
                    background: '{primary.100}', // 浅金（高亮背景）
                    focusBackground: '{primary.200}', // 浅中金（焦点背景）
                    color: '{primary.700}',      // 暗金（高亮文字）
                    focusColor: '{primary.800}', // 复古暗金（焦点文字）
                },
                // 浅色：白底 + 浅金系 surface（与主色协调）
                surface: {
                    0: '#ffffff',
                    50: '#F9F5E8',
                    100: '#F2E9C9',
                    200: '#E8D99B',
                    300: '#E0E0E0',
                    400: '#A9A9A9',
                    500: '#777777',
                    600: '#555555',
                    700: '#444444',
                    800: '#292929',
                    900: '#1A1A1A',
                    950: '#0F0F0F',
                },
            },
            dark: {
                primary: {
                    color: '{primary.800}',      // 复古暗金（核心主色）
                    contrastColor: '#121212',    // 近黑文字
                    hoverColor: '{primary.700}', // 暗金（hover）
                    activeColor: '{primary.600}',// 深金（active）
                },
                highlight: {
                    background: 'color-mix(in srgb, {primary.800}, transparent 82%)', // 暗金透明（高亮背景）
                    focusBackground: 'color-mix(in srgb, {primary.800}, transparent 74%)', // 暗金半透（焦点背景）
                    color: '{primary.300}',      // 中金（高亮文字）
                    focusColor: '{primary.200}', // 浅中金（焦点文字）
                },
                // 深色：深黑系 + 暗金系 surface（与主色协调）
                // surface.0 最亮（白色文字），数字越大越暗（背景色）
                surface: {
                    0: '#ffffff',
                    50: '#F0F0F0',
                    100: '#E0E0E0',
                    200: '#CCCCCC',  // 主要文字、激活态图标（高亮）
                    300: '#BEBEBE',  // 次级按钮图标/文字（提亮↑，增强暗色可读性）
                    400: '#A3A3A3',  // 次级文字（提亮↑，原 #737373 对比度不足）
                    500: '#737373',  // 弱化文字（提示、placeholder）
                    600: '#525252',  // 分隔线、禁用态（补充缺失项）
                    700: '#303030',  // 深色控件背景
                    800: '#1C1C1C',  // Card/Panel 背景
                    900: '#0F0F0F',  // 页面背景
                    950: '#000000',  // 最深（modal overlay）
                },
            },
        },
    },
    // 全局紧凑密度配置（与 CSS 变量对齐）
    tokens: {
        fontSize: {
            xs: '0.75rem',
            sm: '0.8125rem',
            md: '0.875rem',
            lg: '1rem',
            xl: '1.125rem',
        },
        spacing: {
            xs: '0.25rem',
            sm: '0.5rem',
            md: '0.75rem',
            lg: '1rem',
            xl: '1.25rem',
        },
        // 按钮/输入框高度收紧
        formField: {
            sm: {
                paddingX: '0.5rem',
                paddingY: '0.25rem',
                fontSize: '0.8125rem',
            },
            md: {
                paddingX: '0.625rem',
                paddingY: '0.375rem',
                fontSize: '0.875rem',
            },
        },
    },
})
