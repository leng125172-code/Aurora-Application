<template>
    <div class="p-4 space-y-4">
        <!-- 接口摘要 -->
        <div v-if="endpoint.operation.summary || endpoint.operation.description" class="text-sm text-muted-foreground">
            {{ endpoint.operation.summary ?? endpoint.operation.description }}
        </div>

        <Tabs value="params">
            <TabList>
                <Tab value="params">{{ t('swaggerPage.tabParams') }}</Tab>
                <Tab value="body" :disabled="!hasBody">{{ t('swaggerPage.tabBody') }}</Tab>
                <Tab value="headers">{{ t('swaggerPage.tabHeaders') }}</Tab>
            </TabList>

            <TabPanels>
                <!-- 参数 Tab -->
                <TabPanel value="params" class="space-y-3 mt-3">
                    <div
                        v-if="pathParams.length === 0 && queryParams.length === 0"
                        class="text-sm text-muted-foreground"
                    >
                        {{ t('swaggerPage.noParams') }}
                    </div>

                    <div v-if="pathParams.length > 0">
                        <p class="text-xs font-semibold text-muted-foreground mb-2">
                            {{ t('swaggerPage.pathParams') }}
                        </p>
                        <div v-for="p in pathParams" :key="p.name" class="flex items-center gap-2 mb-2">
                            <label class="text-xs font-mono w-32 shrink-0">{{ p.name }}</label>
                            <InputText
                                v-model="req.pathParams[p.name]"
                                :placeholder="p.description ?? p.name"
                                size="small"
                                class="flex-1"
                            />
                        </div>
                    </div>

                    <div v-if="queryParams.length > 0">
                        <p class="text-xs font-semibold text-muted-foreground mb-2">
                            {{ t('swaggerPage.queryParams') }}
                        </p>
                        <div v-for="p in queryParams" :key="p.name" class="flex items-center gap-2 mb-2">
                            <label class="text-xs font-mono w-32 shrink-0">{{ p.name }}</label>
                            <InputText
                                v-model="req.queryParams[p.name]"
                                :placeholder="p.description ?? p.name"
                                size="small"
                                class="flex-1"
                            />
                        </div>
                    </div>
                </TabPanel>

                <!-- 请求体 Tab -->
                <TabPanel value="body" class="mt-3">
                    <Textarea
                        v-model="req.body"
                        rows="8"
                        size="small"
                        class="w-full font-mono text-xs resize-y"
                        :placeholder="t('swaggerPage.bodyPlaceholder')"
                    />
                </TabPanel>

                <!-- 请求头 Tab -->
                <TabPanel value="headers" class="mt-3 space-y-2">
                    <div v-for="(_, key) in req.headers" :key="key" class="flex items-center gap-2">
                        <InputText :model-value="key" readonly class="w-36 font-mono text-xs" />
                        <InputText v-model="req.headers[key]" class="flex-1 font-mono text-xs" />
                    </div>
                    <Button severity="secondary" outlined size="small" @click="addHeader">
                        {{ t('swaggerPage.addHeader') }}
                    </Button>
                </TabPanel>
            </TabPanels>
        </Tabs>

        <!-- 发送按钮 -->
        <div class="flex gap-2 items-center">
            <Button size="small" :disabled="sending" @click="sendRequest">
                {{ sending ? t('swaggerPage.sending') : t('swaggerPage.send') }}
            </Button>
            <Button text severity="secondary" size="small" @click="reset">{{ t('swaggerPage.reset') }}</Button>
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
                <Button text severity="secondary" size="small" class="ml-auto text-xs" @click="copyResponse">
                    {{ t('swaggerPage.copyResponse') }}
                </Button>
            </div>
            <div class="h-48 rounded-md border overflow-auto">
                <pre class="p-3 font-mono text-xs whitespace-pre-wrap break-all">{{ response.body }}</pre>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ApiEndpoint, SwaggerDocument, DebugRequest, DebugResponse } from '@/types/swagger'
import { executeDebugRequest, generateExampleBody, statusColor } from '@/api/swagger'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Skeleton from 'primevue/skeleton'
import { useAppToast } from '@/composables/useAppToast'

const { t } = useI18n()
const toast = useAppToast()

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

const pathParams = computed(() => (props.endpoint.operation.parameters ?? []).filter((p) => p.in === 'path'))
const queryParams = computed(() => (props.endpoint.operation.parameters ?? []).filter((p) => p.in === 'query'))
const hasBody = computed(() => ['POST', 'PUT', 'PATCH'].includes(props.endpoint.method))

onMounted(() => {
    // 初始化路径/查询参数
    for (const p of pathParams.value) req.value.pathParams[p.name] = ''
    for (const p of queryParams.value) req.value.queryParams[p.name] = ''
    // 自动生成示例请求体
    if (hasBody.value) {
        const schema = props.endpoint.operation.requestBody?.content?.['application/json']?.schema
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
    toast.success(t('swaggerPage.copied'))
}
</script>
