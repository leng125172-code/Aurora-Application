<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import { useAuthStore } from '@/stores/auth'
import {
    PlcTagAccess,
    PlcTagDataType,
    browsePlc,
    createPlcTag,
    deletePlcTag,
    getPlc,
    getPlcTags,
    readPlcTags,
    subscribePlcTags,
    unsubscribePlcTags,
    writePlcTag,
    type PlcBrowseNode,
    type PlcDeviceDto,
    type PlcTagDto,
    type PlcTagValueDto,
} from '@/api/plcs'
import { presentPlcValue } from '@/utils/plc-value-display'
import { useAppConfirm } from '@/composables/useAppConfirm'

const id = useRoute().params.id as string
const auth = useAuthStore()
const confirmAction = useAppConfirm()
const dataTypeOptions = Object.entries(PlcTagDataType)
    .filter(([, value]) => typeof value === 'number')
    .map(([label, value]) => ({ label, value: value as PlcTagDataType }))
const device = ref<PlcDeviceDto>()
const tags = ref<PlcTagDto[]>([])
const nodes = ref<PlcBrowseNode[]>([])
const values = reactive<Record<string, PlcTagValueDto>>({})
const writeValues = reactive<Record<string, string>>({})
const parentAddress = ref<string>()
const subscriptionId = ref<string>()
type RealtimeSubscriptionStatus = 'stopped' | 'starting' | 'active' | 'reconnecting' | 'error'
const realtimeStatus = ref<RealtimeSubscriptionStatus>('stopped')
const realtimeStatusText: Record<RealtimeSubscriptionStatus, string> = {
    stopped: '未订阅',
    starting: '订阅中',
    active: '已订阅',
    reconnecting: '重连中',
    error: '订阅失败',
}
const realtimeStatusClass: Record<RealtimeSubscriptionStatus, string> = {
    stopped: 'text-muted-foreground',
    starting: 'text-amber-600',
    active: 'text-emerald-600',
    reconnecting: 'text-amber-600',
    error: 'text-destructive',
}
const hub = new HubConnectionBuilder()
    .withUrl('/signalr-hubs/plc', { accessTokenFactory: () => auth.token || '' })
    .withAutomaticReconnect()
    .build()
const newTag = reactive({
    code: '',
    name: '',
    address: '',
    dataType: PlcTagDataType.Double,
    access: PlcTagAccess.ReadWrite,
    isEnabled: true,
    samplingIntervalMs: 500,
    scale: 1,
    offset: 0,
})
async function load() {
    ;[device.value, tags.value] = await Promise.all([getPlc(id), getPlcTags(id)])
}
async function startRealtime() {
    if (subscriptionId.value) return
    realtimeStatus.value = 'starting'
    try {
        if (hub.state === HubConnectionState.Disconnected) await hub.start()
        if (hub.connectionId && tags.value.length) {
            subscriptionId.value = await subscribePlcTags(
                id,
                hub.connectionId,
                tags.value.map((x) => x.id)
            )
            realtimeStatus.value = 'active'
        } else {
            realtimeStatus.value = 'stopped'
        }
    } catch (error) {
        realtimeStatus.value = 'error'
        throw error
    }
}
async function stopRealtime() {
    const activeSubscriptionId = subscriptionId.value
    subscriptionId.value = undefined
    realtimeStatus.value = 'stopped'
    if (!activeSubscriptionId) return
    try {
        await unsubscribePlcTags(id, activeSubscriptionId)
    } catch {
        /* 离线时服务端会清理跟踪记录 */
    }
}
async function browse(parent?: string) {
    parentAddress.value = parent
    nodes.value = (await browsePlc(id, parent)).items
}
function selectNode(node: PlcBrowseNode) {
    if (node.hasChildren) void browse(node.address)
    else {
        newTag.address = node.address
        newTag.name = node.displayName
        newTag.code = node.browseName.replace(/[^a-zA-Z0-9_]/g, '_')
    }
}
async function addTag() {
    await createPlcTag(id, { ...newTag })
    await load()
    await stopRealtime()
    await startRealtime()
}
async function removeTag(tag: PlcTagDto) {
    if (!(await confirmAction({ message: `删除 PLC 点位“${tag.name}”吗？` }))) return
    await stopRealtime()
    await deletePlcTag(id, tag.id)
    delete values[tag.id]
    delete writeValues[tag.id]
    await load()
    await startRealtime()
}
async function readAll() {
    const result = await readPlcTags(
        id,
        tags.value.map((x) => x.id)
    )
    for (const value of result) values[value.tagId] = value
}
async function write(tag: PlcTagDto) {
    await writePlcTag(id, tag.id, writeValues[tag.id])
    await readAll()
}
hub.on('ValueChanged', (value: PlcTagValueDto) => {
    values[value.tagId] = value
})
hub.onreconnecting(() => {
    realtimeStatus.value = 'reconnecting'
})
hub.onreconnected(async () => {
    subscriptionId.value = undefined
    try {
        await startRealtime()
    } catch {
        /* 下次页面操作再重试，避免前端重连风暴 */
    }
})
hub.onclose(() => {
    subscriptionId.value = undefined
    if (realtimeStatus.value !== 'stopped') realtimeStatus.value = 'error'
})
onMounted(async () => {
    await load()
    await browse()
    await startRealtime()
})
onUnmounted(async () => {
    await stopRealtime()
    if (hub.state !== HubConnectionState.Disconnected) await hub.stop()
})
</script>

<template>
    <div class="space-y-5 p-1 sm:p-2">
        <div>
            <h1 class="text-2xl font-bold">{{ device?.name || 'PLC 点位控制' }}</h1>
            <p class="font-mono text-sm text-muted-foreground">{{ device?.endpointUrl }}</p>
        </div>
        <div class="grid gap-5 xl:grid-cols-2">
            <section class="space-y-3 rounded-lg border p-4">
                <div class="flex items-center justify-between">
                    <h2 class="font-semibold">OPC UA 节点浏览</h2>
                    <Button size="small" severity="secondary" outlined label="刷新" @click="browse(parentAddress)" />
                </div>
                <Button v-if="parentAddress" size="small" text label="返回根节点" @click="browse()" />
                <div class="max-h-80 overflow-auto rounded border">
                    <Button
                        v-for="node in nodes"
                        :key="node.address"
                        size="small"
                        severity="secondary"
                        text
                        class="!flex !w-full !justify-between !rounded-none !border-b !p-2 !text-left"
                        @click="selectNode(node)"
                    >
                        <span>{{ node.displayName }}</span>
                        <span class="text-xs text-muted-foreground">{{ node.nodeClass }}</span>
                    </Button>
                </div>
                <div class="grid gap-2 md:grid-cols-2">
                    <InputText v-model="newTag.code" size="small" placeholder="业务代码" />
                    <InputText v-model="newTag.name" size="small" placeholder="名称" />
                    <InputText
                        v-model="newTag.address"
                        size="small"
                        placeholder="NodeId，例如 ns=2;s=Tag1"
                        class="md:col-span-2"
                    />
                    <Select
                        v-model="newTag.dataType"
                        size="small"
                        :options="dataTypeOptions"
                        option-label="label"
                        option-value="value"
                    />
                    <Button size="small" label="加入点位表" @click="addTag" />
                </div>
            </section>
            <section class="space-y-3 rounded-lg border p-4">
                <div class="flex items-center justify-between">
                    <div>
                        <h2 class="font-semibold">实时点位</h2>
                        <div class="text-xs" :class="realtimeStatusClass[realtimeStatus]">
                            订阅状态：{{ realtimeStatusText[realtimeStatus] }}
                        </div>
                    </div>
                    <div class="flex gap-2">
                        <Button
                            size="small"
                            severity="secondary"
                            outlined
                            :label="subscriptionId ? '停止订阅' : '开始订阅'"
                            @click="subscriptionId ? stopRealtime() : startRealtime()"
                        />
                        <Button size="small" severity="secondary" outlined label="批量读取" @click="readAll" />
                    </div>
                </div>
                <div class="max-h-[32rem] overflow-auto">
                    <div
                        v-for="tag in tags"
                        :key="tag.id"
                        class="grid grid-cols-[1fr_1fr_auto] items-center gap-2 border-b py-2"
                    >
                        <div>
                            <div class="font-medium">{{ tag.name }}</div>
                            <div class="font-mono text-xs text-muted-foreground">
                                {{ tag.code }} · {{ tag.address }}
                            </div>
                        </div>
                        <div>
                            <div>{{ presentPlcValue(values[tag.id]?.engineeringValue).summary }} {{ tag.unit }}</div>
                            <div class="text-xs text-muted-foreground">{{ values[tag.id]?.quality }}</div>
                            <details
                                v-if="presentPlcValue(values[tag.id]?.engineeringValue).details.length"
                                class="mt-1 text-xs"
                            >
                                <summary class="cursor-pointer text-primary">查看详情</summary>
                                <dl class="mt-1 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 rounded bg-muted p-2">
                                    <template
                                        v-for="detail in presentPlcValue(values[tag.id]?.engineeringValue).details"
                                        :key="detail.label"
                                    >
                                        <dt class="text-muted-foreground">{{ detail.label }}</dt>
                                        <dd class="break-all">{{ detail.value }}</dd>
                                    </template>
                                </dl>
                                <details v-if="presentPlcValue(values[tag.id]?.engineeringValue).rawJson" class="mt-1">
                                    <summary class="cursor-pointer text-muted-foreground">原始 JSON（调试）</summary>
                                    <pre
                                        class="mt-1 max-h-48 overflow-auto rounded bg-muted p-2 text-[10px] leading-4"
                                        >{{ presentPlcValue(values[tag.id]?.engineeringValue).rawJson }}</pre
                                    >
                                </details>
                            </details>
                        </div>
                        <div class="flex gap-1">
                            <template v-if="(tag.access & PlcTagAccess.Write) !== 0">
                                <InputText v-model="writeValues[tag.id]" size="small" class="w-24" />
                                <Button size="small" severity="secondary" outlined label="写入" @click="write(tag)" />
                            </template>
                            <Button size="small" severity="danger" text label="删除" @click="removeTag(tag)" />
                        </div>
                    </div>
                    <p v-if="tags.length === 0" class="p-8 text-center text-muted-foreground">请从左侧节点树添加点位</p>
                </div>
            </section>
        </div>
    </div>
</template>
