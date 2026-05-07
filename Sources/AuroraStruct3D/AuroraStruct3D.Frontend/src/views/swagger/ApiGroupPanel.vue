<template>
  <div class="relative rounded-lg border bg-card overflow-hidden">
    <!-- GlowBorder 装饰层（不包裹内容） -->
    <GlowBorder class="absolute inset-0 pointer-events-none rounded-lg" :border-width="1" />

    <!-- 分组标题栏 -->
    <button
      class="relative z-10 w-full flex items-center justify-between px-4 py-3 text-left hover:bg-muted/50 transition-colors"
      @click="isOpen = !isOpen"
    >
      <span class="font-semibold text-sm">{{ group.tag }}</span>
      <div class="flex items-center gap-2">
        <Badge variant="secondary">{{ group.endpoints.length }} 个接口</Badge>
        <span class="text-muted-foreground text-xs">{{ isOpen ? '▲' : '▼' }}</span>
      </div>
    </button>

    <!-- 接口列表（展开/收起动画） -->
    <Transition name="slide">
      <div v-if="isOpen" class="relative z-10 border-t divide-y">
        <ApiEndpointRow
          v-for="(ep, idx) in group.endpoints"
          :key="idx"
          :endpoint="ep"
          :doc="doc"
        />
      </div>
    </Transition>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import type { ApiGroup, SwaggerDocument } from '@/types/swagger'
import { Badge } from '@/components/ui/badge'
import { GlowBorder } from '@/components/ui/glow-border'
import ApiEndpointRow from './ApiEndpointRow.vue'

const props = defineProps<{
  group: ApiGroup
  doc: SwaggerDocument
  forceExpand?: boolean
}>()

const isOpen = ref(false)

watch(
  () => props.forceExpand,
  (val) => {
    if (val !== undefined) isOpen.value = val
  },
)
</script>

<style scoped>
.slide-enter-active,
.slide-leave-active {
  transition: all 0.2s ease;
  overflow: hidden;
}
.slide-enter-from,
.slide-leave-to {
  max-height: 0;
  opacity: 0;
}
.slide-enter-to,
.slide-leave-from {
  max-height: 2000px;
  opacity: 1;
}
</style>
