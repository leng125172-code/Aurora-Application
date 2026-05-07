<template>
  <div class="relative min-h-full">
    <!-- 背景装饰 -->
    <InteractiveGridPattern
      class="absolute inset-0 opacity-20 pointer-events-none"
      :width="40"
      :height="40"
    />

    <!-- 页面内容 -->
    <div class="relative z-10 space-y-6">
      <!-- 标题 -->
      <div class="text-center pt-4">
        <SparklesText text="API 接口文档" class="text-3xl font-bold" />
        <p class="text-muted-foreground mt-2 text-sm">
          共 {{ groups.length }} 个分组 · {{ totalEndpoints }} 个接口
        </p>
      </div>

      <!-- 工具栏 -->
      <div class="flex flex-wrap gap-3 items-center">
        <Input v-model="searchText" placeholder="搜索接口路径或描述..." class="flex-1 min-w-48" />
        <Select v-model="filterMethod">
          <SelectTrigger class="w-36">
            <SelectValue placeholder="全部方法" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="ALL">全部方法</SelectItem>
            <SelectItem value="GET">GET</SelectItem>
            <SelectItem value="POST">POST</SelectItem>
            <SelectItem value="PUT">PUT</SelectItem>
            <SelectItem value="DELETE">DELETE</SelectItem>
            <SelectItem value="PATCH">PATCH</SelectItem>
          </SelectContent>
        </Select>
        <Button variant="outline" size="sm" @click="toggleAll">
          {{ allExpanded ? '全部收起' : '全部展开' }}
        </Button>
      </div>

      <!-- 方法统计徽章 -->
      <div class="flex flex-wrap gap-2">
        <Badge variant="secondary">GET {{ methodCount('GET') }}</Badge>
        <Badge variant="secondary">POST {{ methodCount('POST') }}</Badge>
        <Badge variant="secondary">PUT {{ methodCount('PUT') }}</Badge>
        <Badge variant="secondary">DELETE {{ methodCount('DELETE') }}</Badge>
        <Badge variant="secondary">PATCH {{ methodCount('PATCH') }}</Badge>
      </div>

      <!-- 加载骨架屏 -->
      <div v-if="loading" class="space-y-4">
        <Skeleton v-for="i in 5" :key="i" class="h-16 w-full rounded-lg" />
      </div>

      <!-- 错误提示 -->
      <div v-else-if="error" class="text-center text-destructive py-12">{{ error }}</div>

      <!-- 接口分组列表 -->
      <div v-else class="space-y-3">
        <ApiGroupPanel
          v-for="group in filteredGroups"
          :key="group.tag"
          :group="group"
          :doc="doc!"
          :force-expand="allExpanded"
        />
        <div v-if="filteredGroups.length === 0" class="text-center text-muted-foreground py-12">
          未找到匹配的接口
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { fetchSwaggerDocument, groupEndpointsByTag } from '@/api/swagger'
import type { SwaggerDocument, ApiGroup } from '@/types/swagger'
import ApiGroupPanel from './ApiGroupPanel.vue'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { SparklesText } from '@/components/ui/sparkles-text'
import { InteractiveGridPattern } from '@/components/ui/interactive-grid-pattern'

const doc = ref<SwaggerDocument | null>(null)
const groups = ref<ApiGroup[]>([])
const loading = ref(true)
const error = ref('')
const searchText = ref('')
const filterMethod = ref('ALL')
const allExpanded = ref(false)

const totalEndpoints = computed(() =>
  groups.value.reduce((s, g) => s + g.endpoints.length, 0),
)

const filteredGroups = computed<ApiGroup[]>(() => {
  return groups.value
    .map((g) => {
      const eps = g.endpoints.filter((ep) => {
        const matchMethod = filterMethod.value === 'ALL' || ep.method === filterMethod.value
        const q = searchText.value.toLowerCase()
        const matchText =
          !q ||
          ep.path.toLowerCase().includes(q) ||
          (ep.operation.summary ?? '').toLowerCase().includes(q)
        return matchMethod && matchText
      })
      return { ...g, endpoints: eps }
    })
    .filter((g) => g.endpoints.length > 0)
})

function methodCount(method: string): number {
  return groups.value.reduce(
    (s, g) => s + g.endpoints.filter((e) => e.method === method).length,
    0,
  )
}

function toggleAll() {
  allExpanded.value = !allExpanded.value
}

onMounted(async () => {
  try {
    doc.value = await fetchSwaggerDocument()
    groups.value = groupEndpointsByTag(doc.value)
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : '加载失败'
  } finally {
    loading.value = false
  }
})
</script>
