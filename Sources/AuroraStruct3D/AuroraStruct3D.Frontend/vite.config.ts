import { fileURLToPath, URL } from "node:url";
import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import tailwindcss from "@tailwindcss/vite";
import AutoImport from "unplugin-auto-import/vite";

// 后端 ABP HttpApi.Host 默认监听端口
const ABP_BACKEND_URL = "http://localhost:44315";

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
    // 加载 .env 文件（仅前缀 VITE_ 的会注入到客户端）
    const env = loadEnv(mode, process.cwd(), "");
    const backendUrl = env.VITE_ABP_BACKEND_URL || ABP_BACKEND_URL;

    return {
        plugins: [
            vue(),
            tailwindcss(),
            // 自动导入 Vue / VueUse 等常用 API，主要服务于 Inspira UI 动画组件
            // 生成 auto-imports.d.ts 给 TS 识别（被 tsconfig.app.json include 包含）
            AutoImport({
                imports: ["vue", "vue-router", "@vueuse/core"],
                dts: "./src/auto-imports.d.ts",
                vueTemplate: true,
                dirs: [],
            }),
        ],
        resolve: {
            alias: {
                // shadcn-vue / Inspira UI 强制使用 @/ 路径别名
                "@": fileURLToPath(new URL("./src", import.meta.url)),
            },
        },
        server: {
            host: "0.0.0.0",
            port: 5173,
            strictPort: true,
            // 开发期：把 ABP 后端相关路径全部代理到 44315
            proxy: {
                "/api": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                },
                "/swagger": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                },
                "/hangfire": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                },
                "/cap": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                },
                "/profiler": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                },
                "/health": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                },
                "/abp": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                },
                "/connect": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                },
                "/signalr": {
                    target: backendUrl,
                    changeOrigin: true,
                    secure: false,
                    ws: true,
                },
            },
        },
        build: {
            // 构建产物直接输出至后端 wwwroot 目录
            outDir: fileURLToPath(
                new URL(
                    "../AuroraStruct3D.HttpApi.Host/wwwroot",
                    import.meta.url,
                ),
            ),
            chunkSizeWarningLimit: 1200,
            // 每次构建前清空 outDir，避免旧 hash 产物残留导致编码乱码
            emptyOutDir: true,
            // 生产环境也生成sourcemap，方便调试3D和WebSocket相关问题
            sourcemap: true,
            target: "es2022",
            // 禁用所有资源内联，确保所有文件都生成独立的固定名称文件
            assetsInlineLimit: 0,

            // 👇 禁用代码混淆与变量重命名（核心配置）
            esbuild: {
                // 保留所有原始变量名、函数名和类名
                keepNames: true,
                // 完全禁用标识符混淆（不会把变量名改成a/b/c）
                minifyIdentifiers: false,
                // 保留空格和换行（可选，完全可读模式）
                // minifyWhitespace: false,
                // 保留注释（可选，调试用）
                // keepComments: true
            },

            rollupOptions: {
                // 过滤第三方包（signalr、vueuse 等）中 Rolldown 无法识别位置的 #__PURE__ 注解警告
                onwarn(warning, warn) {
                    if (warning.code === 'INVALID_ANNOTATION') return
                    warn(warning)
                },
                output: {
                    // 👇 禁用所有文件名哈希（核心配置）
                    // 入口文件名称（无哈希）
                    entryFileNames: "assets/[name].js",
                    // 代码分包文件名称（无哈希）
                    chunkFileNames: "assets/[name].js",
                    // 静态资源文件名称（无哈希）
                    assetFileNames: "assets/[name].[ext]",

                    // 代码分包，避免单 chunk 过大（Vite 8 / rolldown 要求函数形式）
                    manualChunks(id: string) {
                        if (!id.includes("node_modules")) return;
                        if (
                            /[\\/]node_modules[\\/](vue|vue-router|pinia)[\\/]/.test(
                                id,
                            )
                        )
                            return "vue";
                        if (
                            /[\\/]node_modules[\\/](vue-i18n|@intlify)[\\/]/.test(
                                id,
                            )
                        )
                            return "i18n";
                        if (
                            /[\\/]node_modules[\\/](radix-vue|reka-ui|class-variance-authority|clsx|tailwind-merge)[\\/]/.test(
                                id,
                            )
                        )
                            return "ui";
                        // echarts 体积最大，单独分包
                        if (
                            /[\\/]node_modules[\\/](echarts|zrender)[\\/]/.test(
                                id,
                            )
                        )
                            return "echarts";
                        // SignalR 单独分包
                        if (
                            /[\\/]node_modules[\\/]@microsoft[\\/]signalr[\\/]/.test(
                                id,
                            )
                        )
                            return "signalr";
                        // VueUse 单独分包
                        if (
                            /[\\/]node_modules[\\/]@vueuse[\\/]/.test(id)
                        )
                            return "vueuse";
                        // 动画库单独分包
                        if (
                            /[\\/]node_modules[\\/](motion-v|motion)[\\/]/.test(
                                id,
                            )
                        )
                            return "motion";
                        // 其他第三方库统一打包到 vendor
                        return "vendor";
                    },
                },
            },
        },
        define: {
            __APP_VERSION__: JSON.stringify(process.env.npm_package_version),
            __WORKFLOW_DEBUG__: JSON.stringify(
                env.VITE_ENABLE_WORKFLOW_DEBUG === "true" || mode === "debug",
            ),
        },
    };
});
