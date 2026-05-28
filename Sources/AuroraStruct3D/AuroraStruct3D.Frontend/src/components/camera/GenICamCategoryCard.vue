<script setup lang="ts">
// 动态渲染一个 GenICam Category 分组卡片
// 内部根据 Visibility 过滤后逐节点渲染 GenICamNodeField
// 视觉风格参考 Dashboard 页面：使用 Card/CardHeader/CardTitle/CardContent
import { computed } from 'vue'
import type { GenICamCategoryDto, GenICamNodeDto } from '@/api/cameras'
import GenICamNodeField from './GenICamNodeField.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'

const props = defineProps<{
    cameraId: string
    category: GenICamCategoryDto
    /** 节点当前值映射 nodeName -> value */
    values: Record<string, string | null>
    /** 'Beginner' | 'Expert' | 'Guru'：仅显示该可见性及更低层级节点 */
    visibility: 'Beginner' | 'Expert' | 'Guru'
    disabled?: boolean
    loading?: boolean
}>()

const emit = defineEmits<{
    (e: 'refresh'): void
    (e: 'node-updated', node: GenICamNodeDto, newValue: string): void
}>()

const visibilityRank: Record<string, number> = {
    Beginner: 1,
    Expert: 2,
    Guru: 3,
    Invisible: 4,
}

const visibleNodes = computed<GenICamNodeDto[]>(() => {
    const limit = visibilityRank[props.visibility] ?? 1
    return props.category.nodes.filter((n) => {
        // 过滤掉 Category 本身节点（仅作为分组标签）
        if (n.nodeType === 'Category') return false
        const rank = visibilityRank[n.visibility] ?? 4
        return rank <= limit && n.access !== 'NotImplemented' && n.access !== 'NotAvailable'
    })
})
</script>

<template>
    <Card class="relative">
        <CardHeader class="flex flex-row items-center justify-between space-y-0 py-3">
            <CardTitle class="text-sm font-semibold">
                {{ category.displayName || category.name }}
            </CardTitle>
            <button
                :disabled="disabled || loading"
                class="text-xs text-primary hover:underline disabled:opacity-40"
                @click="emit('refresh')"
            >
                {{ loading ? '加载中…' : '刷新' }}
            </button>
        </CardHeader>
        <CardContent class="p-0">
            <div class="divide-y border-t">
                <template v-if="visibleNodes.length === 0">
                    <div class="px-3 py-4 text-center text-xs text-muted-foreground">无 {{ visibility }} 可见参数</div>
                </template>
                <GenICamNodeField
                    v-for="node in visibleNodes"
                    :key="node.nodeName"
                    :camera-id="cameraId"
                    :node="node"
                    :value="values[node.nodeName] ?? node.currentValue"
                    :disabled="disabled"
                    @updated="(n, v) => emit('node-updated', n, v)"
                />
            </div>
        </CardContent>
    </Card>
</template>
