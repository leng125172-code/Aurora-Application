/**
 * 应用入口：装配 Vue + Pinia + Router + I18n + PrimeVue + 全局样式
 */
import { createApp } from 'vue'
import { createPinia } from 'pinia'

// PrimeVue 核心、服务与指令（Aura 暗色预设，配合 Tailwind v4 通过 CSS Layer 共存）
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import DialogService from 'primevue/dialogservice'
import Tooltip from 'primevue/tooltip'
import Ripple from 'primevue/ripple'
import StyleClass from 'primevue/styleclass'
import Aura from '@primeuix/themes/aura'

import App from '@/App.vue'
import { router } from '@/router'
import { i18n } from '@/i18n'

// 全局样式（Tailwind v4 入口 + 残留的 shadcn-vue 变量；迁移完成后再清理）
import '@/assets/styles/index.css'

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.use(i18n)

// 注册 PrimeVue：暗色模式由 .dark 类切换（与项目 theme store 一致）；
// 启用 CSS Layer，确保 Tailwind 工具类优先级高于 PrimeVue 基础样式
app.use(PrimeVue, {
    ripple: true,
    theme: {
        preset: Aura,
        options: {
            prefix: 'p',
            darkModeSelector: '.dark',
            cssLayer: {
                name: 'primevue',
                order: 'theme, base, primevue, utilities',
            },
        },
    },
})

app.use(ToastService)
app.use(ConfirmationService)
app.use(DialogService)

app.directive('tooltip', Tooltip)
app.directive('ripple', Ripple)
app.directive('styleclass', StyleClass)

app.mount('#app')
