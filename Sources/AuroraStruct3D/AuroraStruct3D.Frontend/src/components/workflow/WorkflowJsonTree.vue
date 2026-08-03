<script setup lang="ts">
import { computed } from 'vue'

defineOptions({ name: 'WorkflowJsonTree' })

const props = withDefaults(defineProps<{
    value: unknown
    label?: string
    depth?: number
}>(), {
    label: '',
    depth: 0,
})

const isContainer = computed(
    () => props.value !== null && typeof props.value === 'object',
)
const entries = computed<[string, unknown][]>(() => {
    if (Array.isArray(props.value)) {
        return props.value.map((value, index) => [String(index), value])
    }
    if (isContainer.value) {
        return Object.entries(props.value as Record<string, unknown>)
    }
    return []
})
const containerLabel = computed(
    () => Array.isArray(props.value) ? `[${entries.value.length}]` : `{${entries.value.length}}`,
)

function primitiveText(value: unknown): string {
    if (typeof value === 'string') return JSON.stringify(value)
    if (value === undefined) return 'undefined'
    return String(value)
}
</script>

<template>
    <details v-if="isContainer" :open="depth < 2" class="font-mono text-xs">
        <summary class="cursor-pointer select-none py-0.5">
            <span v-if="label" class="text-sky-700 dark:text-sky-400">{{ label }}: </span>
            <span class="text-muted-foreground">{{ containerLabel }}</span>
        </summary>
        <div class="ml-4 border-l border-border pl-2">
            <WorkflowJsonTree
                v-for="[key, child] in entries"
                :key="key"
                :label="key"
                :value="child"
                :depth="depth + 1"
            />
            <div v-if="entries.length === 0" class="py-0.5 text-muted-foreground">空</div>
        </div>
    </details>
    <div v-else class="py-0.5 font-mono text-xs">
        <span v-if="label" class="text-sky-700 dark:text-sky-400">{{ label }}: </span>
        <span
            :class="{
                'text-emerald-700 dark:text-emerald-400': typeof value === 'boolean',
                'text-violet-700 dark:text-violet-400': typeof value === 'number',
                'text-amber-700 dark:text-amber-400': typeof value === 'string',
                'text-muted-foreground': value === null || value === undefined,
            }"
        >{{ primitiveText(value) }}</span>
    </div>
</template>
