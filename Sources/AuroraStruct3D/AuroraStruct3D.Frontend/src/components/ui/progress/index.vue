<script setup lang="ts">
/**
 * 线性进度条组件（shadcn-vue 风格）
 */
withDefaults(
    defineProps<{
        modelValue?: number
        class?: string
    }>(),
    {
        modelValue: 0,
    }
)
</script>

<template>
    <div :class="['relative h-2 w-full overflow-hidden rounded-full bg-secondary', $props.class]">
        <div
            class="h-full bg-primary transition-all"
            :style="{ width: `${Math.min(100, Math.max(0, modelValue ?? 0))}%` }"
        />
    </div>
</template>
