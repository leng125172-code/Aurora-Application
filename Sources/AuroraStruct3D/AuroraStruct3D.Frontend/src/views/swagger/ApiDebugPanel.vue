<template>
  <div class="p-4 space-y-4">
    <!-- 接口摘要 -->
    <div v-if="endpoint.operation.summary || endpoint.operation.description" class="text-sm text-muted-foreground">
      {{ endpoint.operation.summary ?? endpoint.operation.description }}
    </div>

    <Tabs default-value="params">
      <TabsList>
        <TabsTrigger value="params">参数</TabsTrigger>
        <TabsTrigger value="body" :disabled="!hasBody">请求体</TabsTrigger>
        <TabsTrigger value="headers">请求头</TabsTrigger>
      </TabsList>

      <!-- 参数 Tab -->
      <TabsContent value="params" class="space-y-3 mt-3">
        <div v-if="pathParams.length === 0 && queryParams.length === 0" class="text-sm text-muted-foreground">
          该接口无参数
        </div>

        <div v-if="pathParams.length > 0">
          <p class="text-xs font-semibold text-muted-foreground mb-2">路径参数</p>
          <div v-for="p in pathParams" :key="p.name" class="flex items-center gap-2 mb-2">
            <label class="text-xs font-mono w-32 shrink-0">{{ p.name }}</label>
            <Input
              v-model="req.pathParams[p.name]"
              :placeholder="p.description ?? p.name"
              size="sm"
              class="flex-1"
            />
          </div>
        </div>

        <div v-if="queryParams.length > 0">
          <p class="text-xs font-semibold text-muted-foreground mb-2">查询参数</p>
          <div v-for="p in queryParams" :key="p.name" class="flex items-center gap-2 mb-2">
            <label class="text-xs font-mono w-32 shrink-0">{{ p.name }}</label>
            <Input
              v-model="req.queryParams[p.name]"
              :placeholder="p.description ?? p.name"
              size="sm"
              class="flex-1"
            />
          </div>
        </div>
      </TabsContent>

      <!-- 请求体 Tab -->
      <TabsContent value="body" class="mt-3">
        <textarea
          v-model="req.body"
          rows="8"
          class="w-full rounded-md border bg-background p-3 font-mono text-xs resize-y focus:outline-none focus:ring-1 focus:ring-ring"
          placeholder="JSON 请求体..."
        />
      </TabsContent>

      <!-- 请求头 Tab -->
      <TabsContent value="headers" class="mt-3 space-y-2">
        <div
          v-for="(_, key) in req.headers"
          :key="key"
          class="flex items-center gap-2"
        >
          <Input :model-value="key" readonly class="w-36 font-mono text-xs" />
          <Input v-model="req.headers[key]" class="flex-1 font-mono text-xs" />
        </div>
        <Button variant="outline" size="sm" @click="addHeader">添加请求头</Button>
      </TabsContent>
    </Tabs>

    <!-- 发送按钮 -->
    <div class="flex gap-2">
      <Button :disabled="sending" @click="sendRequest">
        {{ sending ? '请求中...' : '发送请求' }}
      </Button>
      <Button variant="ghost" size="sm" @click="reset">重置</Button>
    </div>

    <!-- 响应结果 -->
    <div v-if="sending" class="space-y-2">
      <Skeleton class="h-4 w-32" />
      <Skeleton class="h-24 w-full" />
    </div>

    <div v-else-if="response" class="space-y-2">
      <div class="flex items-center gap-3 text-sm">
        <span :class="statusColor(response.status)" class="font-bold font-mono">
          {{ response.status }} {{ response.statusText }}
        </span>
        <span class="text-muted-foreground text-xs">{{ response.duration }}ms</span>
        <Button variant="ghost" size="sm" class="ml-auto text-xs" @click="copyResponse">
          复制响应
        </Button>
      </div>
      <ScrollArea class="h-48 rounded-md border">
        <pre class="p-3 font-mono text-xs whitespace-pre-wrap break-all">{{ response.body }}</pre>
      </ScrollArea>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import type { ApiEndpoint, SwaggerDocument, DebugRequest, DebugResponse } from '@/types/swagger'
import { executeDebugRequest, generateExampleBody, statusColor } from '@/api/swagger'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { toast } from 'vue-sonner'

const props = defineProps<{
  endpoint: ApiEndpoint
  doc: SwaggerDocument
}>()

const sending = ref(false)
const response = ref<DebugResponse | null>(null)

const req = ref<DebugRequest>({
  pathParams: {},
  queryParams: {},
  headers: {},
  body: '',
})

const pathParams = computed(() =>
  (props.endpoint.operation.parameters ?? []).filter((p) => p.in === 'path'),
)
const queryParams = computed(() =>
  (props.endpoint.operation.parameters ?? []).filter((p) => p.in === 'query'),
)
const hasBody = computed(() =>
  ['POST', 'PUT', 'PATCH'].includes(props.endpoint.method),
)

onMounted(() => {
  // 初始化路径/查询参数
  for (const p of pathParams.value) req.value.pathParams[p.name] = ''
  for (const p of queryParams.value) req.value.queryParams[p.name] = ''
  // 自动生成示例请求体
  if (hasBody.value) {
    const schema =
      props.endpoint.operation.requestBody?.content?.['application/json']?.schema
    req.value.body = generateExampleBody(props.doc, schema)
  }
})

async function sendRequest() {
  sending.value = true
  response.value = null
  try {
    response.value = await executeDebugRequest(props.endpoint, req.value)
  } finally {
    sending.value = false
  }
}

function reset() {
  for (const k of Object.keys(req.value.pathParams)) req.value.pathParams[k] = ''
  for (const k of Object.keys(req.value.queryParams)) req.value.queryParams[k] = ''
  req.value.body = ''
  response.value = null
}

function addHeader() {
  req.value.headers['X-Custom-Header'] = ''
}

async function copyResponse() {
  if (!response.value) return
  await navigator.clipboard.writeText(response.value.body)
  toast.success('已复制到剪贴板')
}
</script>
