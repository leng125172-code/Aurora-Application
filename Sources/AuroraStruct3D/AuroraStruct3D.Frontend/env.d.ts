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
    // Nuxt 兼容 shim：Inspira UI 组件中可能使用 import.meta.client / server
    readonly client?: boolean
    readonly server?: boolean
}

// NodeJS 命名空间 shim：少数 Inspira UI 组件引用 NodeJS.Timeout
declare namespace NodeJS {
    type Timeout = ReturnType<typeof setTimeout>
    type Timer = ReturnType<typeof setTimeout>
}

// vite.config 中通过 define 注入的全局常量
declare const __APP_VERSION__: string
