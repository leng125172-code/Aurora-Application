/// <reference types="vite/client" />

// Vue 单文件组件类型声明
declare module '*.vue' {
    import type { DefineComponent } from 'vue'
    const component: DefineComponent<object, object, unknown>
    export default component
}

// 自定义 Vite 环境变量
interface ImportMetaEnv {
    readonly VITE_ABP_BACKEND_URL?: string
    readonly VITE_APP_TITLE?: string
}

interface ImportMeta {
    readonly env: ImportMetaEnv
}

// vite.config 中通过 define 注入的全局常量
declare const __APP_VERSION__: string
