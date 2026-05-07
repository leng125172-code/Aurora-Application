<template>
  <div>
    <!-- 端点行 -->
    <button
      class="w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-muted/40 transition-colors"
      @click="isOpen = !isOpen"
    >
      <span
        class="inline-flex items-center justify-center rounded border px-2 py-0.5 text-xs font-bold font-mono min-w-[60px]"
        :class="methodColor(endpoint.method)"
      >
        {{ endpoint.method }}
      </span>
      <span class="flex-1 font-mono text-sm truncate">{{ endpoint.path }}</span>
      <span v-if="endpoint.operation.summary" class="text-muted-foreground text-xs hidden md:block">
        {{ endpoint.operation.summary }}
      </span>
      <Badge v-if="endpoint.operation.deprecated" variant="destructive" class="text-xs">
        已废弃
      </Badge>
    </button>

    <!-- 调试面板（展开） -->
    <Transition name="slide">
      <div v-if="isOpen" class="border-t bg-muted/20">
        <ApiDebugPanel :endpoint="endpoint" :doc="doc" />
      </div>
    </Transition>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import type { ApiEndpoint, SwaggerDocument } from '@/types/swagger'
import { Badge } from '@/components/ui/badge'
import { methodColor } from '@/api/swagger'
import ApiDebugPanel from './ApiDebugPanel.vue'

defineProps<{
  endpoint: ApiEndpoint
  doc: SwaggerDocument
}>()

const isOpen = ref(false)
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
  max-height: 3000px;
  opacity: 1;
}
</style>
