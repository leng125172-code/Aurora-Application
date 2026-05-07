import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'

// 后端 ABP HttpApi.Host 默认监听端口
const ABP_BACKEND_URL = 'http://localhost:44315'

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
    // 加载 .env 文件（仅前缀 VITE_ 的会注入到客户端）
    const env = loadEnv(mode, process.cwd(), '')
    const backendUrl = env.VITE_ABP_BACKEND_URL || ABP_BACKEND_URL

    return {
        plugins: [vue(), tailwindcss()],
        resolve: {
            alias: {
                // shadcn-vue / Inspira UI 强制使用 @/ 路径别名
                '@': fileURLToPath(new URL('./src', import.meta.url)),
            },
        },
        server: {
            host: '0.0.0.0',
            port: 5173,
            strictPort: true,
            // 开发期：把 ABP 后端相关路径全部代理到 44315
            proxy: {
                '/api': { target: backendUrl, changeOrigin: true, secure: false },
                '/swagger': { target: backendUrl, changeOrigin: true, secure: false },
                '/hangfire': { target: backendUrl, changeOrigin: true, secure: false },
                '/cap': { target: backendUrl, changeOrigin: true, secure: false },
                '/profiler': { target: backendUrl, changeOrigin: true, secure: false },
                '/health': { target: backendUrl, changeOrigin: true, secure: false },
                '/abp': { target: backendUrl, changeOrigin: true, secure: false },
                '/connect': { target: backendUrl, changeOrigin: true, secure: false },
                '/signalr': { target: backendUrl, changeOrigin: true, secure: false, ws: true },
            },
        },
        build: {
            // 构建产物直接输出至后端 wwwroot 目录
            outDir: fileURLToPath(
                new URL('../AuroraStruct3D.HttpApi.Host/wwwroot', import.meta.url),
            ),
            // 每次构建前清空 outDir，避免旧 hash 产物残留导致编码乱码
            emptyOutDir: true,
            sourcemap: mode !== 'production',
            target: 'es2022',
            rollupOptions: {
                output: {
                    // 简单的代码分包，避免单 chunk 过大（Vite 8 / rolldown 要求函数形式）
                    manualChunks(id: string) {
                        if (!id.includes('node_modules')) return
                        if (/[\\/]node_modules[\\/](vue|vue-router|pinia)[\\/]/.test(id)) return 'vue'
                        if (/[\\/]node_modules[\\/](vue-i18n|@intlify)[\\/]/.test(id)) return 'i18n'
                        if (/[\\/]node_modules[\\/](radix-vue|reka-ui|class-variance-authority|clsx|tailwind-merge)[\\/]/.test(id)) return 'ui'
                    },
                },
            },
        },
        define: {
            __APP_VERSION__: JSON.stringify(process.env.npm_package_version),
        },
    }
})
