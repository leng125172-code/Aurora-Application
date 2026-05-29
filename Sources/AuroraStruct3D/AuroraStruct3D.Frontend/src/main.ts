/**
 * 应用入口：装配 Vue + Pinia + Router + I18n + 全局样式
 */
import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from '@/App.vue'
import { router } from '@/router'
import { i18n } from '@/i18n'

// 全局样式（Tailwind 基础层 + shadcn-vue CSS 变量）
import '@/assets/styles/index.css'
import 'vue-sonner/style.css'

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.use(i18n)

app.mount('#app')
