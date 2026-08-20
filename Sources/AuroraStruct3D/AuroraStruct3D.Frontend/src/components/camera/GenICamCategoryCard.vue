<script setup lang="ts">
// 动态渲染一个 GenICam Category 分组卡片
// 内部根据 Visibility 过滤后逐节点渲染 GenICamNodeField
// 子 Category 以可折叠 accordion section 形式内嵌渲染（递归支持多级嵌套）
// 视觉风格参考 Dashboard 页面：使用 AppCard（PrimeVue Card + Inspira BorderBeam）
import { computed, ref } from 'vue'
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

/** 子分类展开状态，key 为子分类 name */
const expandedChildren = ref<Record<string, boolean>>({})

function toggleChild(name: string) {
    expandedChildren.value[name] = !expandedChildren.value[name]
}

/** 过滤子分类：有可见节点或更深子分类才显示 */
function hasVisibleContent(cat: GenICamCategoryDto): boolean {
    const limit = visibilityRank[props.visibility] ?? 1
    const hasNodes = cat.nodes.some((n) => {
        if (n.nodeType === 'Category') return false
        const rank = visibilityRank[n.visibility] ?? 4
        return rank <= limit && n.access !== 'NotImplemented' && n.access !== 'NotAvailable'
    })
    if (hasNodes) return true
    return cat.children.some(hasVisibleContent)
}

const visibleChildren = computed(() => props.category.children.filter(hasVisibleContent))
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
            <template v-if="visibleNodes.length === 0 && visibleChildren.length === 0">
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
            <!-- 子分类：可折叠 accordion section，递归渲染 -->
            <template v-for="child in visibleChildren" :key="child.name">
                <div>
                    <!-- 子分类标题行：点击折叠/展开 -->
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        type="button"
                        class="flex w-full items-center gap-1.5 px-3 py-1.5 text-left text-xs font-medium text-muted-foreground hover:text-foreground transition-colors"
                        @click="toggleChild(child.name)"
                    >
                        <svg
                            class="size-3 shrink-0 transition-transform duration-150"
                            :class="expandedChildren[child.name] ? 'rotate-90' : ''"
                            viewBox="0 0 12 12"
                            fill="currentColor"
                        >
                            <path
                                d="M4 2l4 4-4 4"
                                stroke="currentColor"
                                stroke-width="1.5"
                                fill="none"
                                stroke-linecap="round"
                                stroke-linejoin="round"
                            />
                        </svg>
                        {{ child.displayName || child.name }}
                        <span class="ml-auto text-[10px] text-muted-foreground/60">
                            ({{ child.nodes.length + child.children.length }})
                        </span>
                    </Button>
                    <!-- 子分类内容：展开时显示，左侧加缩进线 -->
                    <div v-if="expandedChildren[child.name]" class="border-l-2 border-border/40 ml-3">
                        <!-- 子分类直属叶子节点 -->
                        <div class="divide-y">
                            <GenICamNodeField
                                v-for="node in child.nodes.filter(
                                    (n) =>
                                        n.nodeType !== 'Category' &&
                                        (visibilityRank[n.visibility] ?? 4) <= (visibilityRank[visibility] ?? 1) &&
                                        n.access !== 'NotImplemented' &&
                                        n.access !== 'NotAvailable'
                                )"
                                :key="node.nodeName"
                                :camera-id="cameraId"
                                :node="node"
                                :value="values[node.nodeName] ?? node.currentValue"
                                :disabled="disabled"
                                @updated="(n, v) => emit('node-updated', n, v)"
                            />
                        </div>
                        <!-- 孙子分类：递归使用同组件（通过 defineComponent 自引用） -->
                        <template v-for="grandchild in child.children.filter(hasVisibleContent)" :key="grandchild.name">
                            <div>
                                <Button
                                    size="small"
                                    text
                                    severity="secondary"
                                    type="button"
                                    class="flex w-full items-center gap-1.5 px-3 py-1.5 text-left text-xs text-muted-foreground/80 hover:text-foreground transition-colors"
                                    @click="toggleChild(grandchild.name)"
                                >
                                    <svg
                                        class="size-3 shrink-0 transition-transform duration-150"
                                        :class="expandedChildren[grandchild.name] ? 'rotate-90' : ''"
                                        viewBox="0 0 12 12"
                                        fill="currentColor"
                                    >
                                        <path
                                            d="M4 2l4 4-4 4"
                                            stroke="currentColor"
                                            stroke-width="1.5"
                                            fill="none"
                                            stroke-linecap="round"
                                            stroke-linejoin="round"
                                        />
                                    </svg>
                                    {{ grandchild.displayName || grandchild.name }}
                                    <span class="ml-auto text-[10px] text-muted-foreground/60">
                                        ({{ grandchild.nodes.length + grandchild.children.length }})
                                    </span>
                                </Button>
                                <div
                                    v-if="expandedChildren[grandchild.name]"
                                    class="border-l-2 border-border/30 ml-3 divide-y"
                                >
                                    <GenICamNodeField
                                        v-for="node in grandchild.nodes.filter(
                                            (n) =>
                                                n.nodeType !== 'Category' &&
                                                (visibilityRank[n.visibility] ?? 4) <=
                                                    (visibilityRank[visibility] ?? 1) &&
                                                n.access !== 'NotImplemented' &&
                                                n.access !== 'NotAvailable'
                                        )"
                                        :key="node.nodeName"
                                        :camera-id="cameraId"
                                        :node="node"
                                        :value="values[node.nodeName] ?? node.currentValue"
                                        :disabled="disabled"
                                        @updated="(n, v) => emit('node-updated', n, v)"
                                    />
                                </div>
                            </div>
                        </template>
                    </div>
                </div>
            </template>
        </div>
    </AppCard>
</template>
