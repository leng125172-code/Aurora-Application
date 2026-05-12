<script setup lang="ts">
/**
 * 通用 IFrame 嵌入视图：用于把后端自带的管理面板（Swagger / CAP / Hangfire / MiniProfiler）
 * 内嵌到 SPA 中，避免破坏 SPA 路由体验
 */
import { computed } from 'vue'

interface Props {
    src: string
    title?: string
}

const props = defineProps<Props>()

const safeTitle = computed(() => props.title ?? 'Embedded Dashboard')
</script>

<template>
    <div class="h-full w-full overflow-hidden rounded-md border bg-card">
        <iframe :src="src" :title="safeTitle" class="h-full w-full" frameborder="0" referrerpolicy="no-referrer" />
    </div>
</template>

<style scoped>
/* 让 iframe 撑满父容器；父容器需要明确高度 */
iframe {
    min-height: calc(100vh - 7rem);
}
</style>
