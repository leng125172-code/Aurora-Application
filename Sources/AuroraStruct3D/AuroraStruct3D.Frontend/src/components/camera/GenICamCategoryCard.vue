<script setup lang="ts">
// 动态渲染一个 GenICam Category 分组卡片
// 内部根据 Visibility 过滤后逐节点渲染 GenICamNodeField
// 视觉风格参考 Dashboard 页面：使用 AppCard（PrimeVue Card + Inspira BorderBeam）
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { GenICamCategoryDto, GenICamNodeDto } from '@/api/cameras'
import GenICamNodeField from './GenICamNodeField.vue'
import Button from 'primevue/button'
import { AppCard } from '@/components/primevue'

const { t } = useI18n()

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
    <AppCard :beam-size="80" :beam-duration="8">
        <div class="flex flex-row items-center justify-between px-3 py-2">
            <div class="text-sm font-semibold">
                {{ category.displayName || category.name }}
            </div>
            <Button text size="small" severity="primary" :disabled="disabled || loading" @click="emit('refresh')">
                {{ loading ? t('camera.categoryLoading') : t('camera.categoryRefresh') }}
            </Button>
        </div>
        <div class="divide-y border-t">
            <template v-if="visibleNodes.length === 0">
                <div class="px-3 py-4 text-center text-xs text-muted-foreground">
                    {{ t('camera.categoryNoNodes', { visibility }) }}
                </div>
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
    </AppCard>
</template>
