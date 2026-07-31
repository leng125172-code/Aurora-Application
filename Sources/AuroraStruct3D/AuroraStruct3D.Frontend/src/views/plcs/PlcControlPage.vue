<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr'
import { useAuthStore } from '@/stores/auth'
import {
    PlcTagAccess,
    PlcTagDataType,
    browsePlc,
    createPlcTag,
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

const id = useRoute().params.id as string
const auth = useAuthStore()
const device = ref<PlcDeviceDto>()
const tags = ref<PlcTagDto[]>([])
const nodes = ref<PlcBrowseNode[]>([])
const values = reactive<Record<string, PlcTagValueDto>>({})
const writeValues = reactive<Record<string, string>>({})
const parentAddress = ref<string>()
const subscriptionId = ref<string>()
const hub = new HubConnectionBuilder()
    .withUrl('/signalr-hubs/plc', { accessTokenFactory: () => auth.token || '' })
    .withAutomaticReconnect()
    .build()
const newTag = reactive({
    code: '', name: '', address: '', dataType: PlcTagDataType.Double, access: PlcTagAccess.ReadWrite,
    isEnabled: true, samplingIntervalMs: 500, scale: 1, offset: 0,
})
async function load() {
    ;[device.value, tags.value] = await Promise.all([getPlc(id), getPlcTags(id)])
}
async function startRealtime() {
    if (hub.state === HubConnectionState.Disconnected) await hub.start()
    if (hub.connectionId && tags.value.length) {
        subscriptionId.value = await subscribePlcTags(id, hub.connectionId, tags.value.map((x) => x.id))
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
    if (subscriptionId.value) await unsubscribePlcTags(id, subscriptionId.value)
    await startRealtime()
}
async function readAll() {
    const result = await readPlcTags(id, tags.value.map((x) => x.id))
    for (const value of result) values[value.tagId] = value
}
async function write(tag: PlcTagDto) {
    await writePlcTag(id, tag.id, writeValues[tag.id])
    await readAll()
}
hub.on('ValueChanged', (value: PlcTagValueDto) => { values[value.tagId] = value })
onMounted(async () => { await load(); await browse(); await startRealtime() })
onUnmounted(async () => {
    if (subscriptionId.value) await unsubscribePlcTags(id, subscriptionId.value)
    if (hub.state !== HubConnectionState.Disconnected) await hub.stop()
})
</script>

<template>
    <div class="space-y-5 p-6">
        <div><h1 class="text-2xl font-bold">{{ device?.name || 'PLC 点位控制' }}</h1><p class="font-mono text-sm text-muted-foreground">{{ device?.endpointUrl }}</p></div>
        <div class="grid gap-5 xl:grid-cols-2">
            <section class="space-y-3 rounded-lg border p-4">
                <div class="flex items-center justify-between"><h2 class="font-semibold">OPC UA 节点浏览</h2><button class="rounded border px-2 py-1" @click="browse(parentAddress)">刷新</button></div>
                <button v-if="parentAddress" class="text-sm text-primary" @click="browse()">返回根节点</button>
                <div class="max-h-80 overflow-auto rounded border">
                    <button v-for="node in nodes" :key="node.address" class="flex w-full justify-between border-b p-2 text-left hover:bg-muted" @click="selectNode(node)">
                        <span>{{ node.displayName }}</span><span class="text-xs text-muted-foreground">{{ node.nodeClass }}</span>
                    </button>
                </div>
                <div class="grid gap-2 md:grid-cols-2">
                    <input v-model="newTag.code" placeholder="业务代码" class="rounded border bg-background p-2" />
                    <input v-model="newTag.name" placeholder="名称" class="rounded border bg-background p-2" />
                    <input v-model="newTag.address" placeholder="NodeId，例如 ns=2;s=Tag1" class="rounded border bg-background p-2 md:col-span-2" />
                    <select v-model.number="newTag.dataType" class="rounded border bg-background p-2"><option v-for="(name, value) in PlcTagDataType" v-show="typeof name === 'string'" :key="value" :value="Number(value)">{{ name }}</option></select>
                    <button class="rounded bg-primary p-2 text-primary-foreground" @click="addTag">加入点位表</button>
                </div>
            </section>
            <section class="space-y-3 rounded-lg border p-4">
                <div class="flex items-center justify-between"><h2 class="font-semibold">实时点位</h2><button class="rounded border px-3 py-1" @click="readAll">批量读取</button></div>
                <div class="max-h-[32rem] overflow-auto">
                    <div v-for="tag in tags" :key="tag.id" class="grid grid-cols-[1fr_1fr_auto] items-center gap-2 border-b py-2">
                        <div><div class="font-medium">{{ tag.name }}</div><div class="font-mono text-xs text-muted-foreground">{{ tag.code }} · {{ tag.address }}</div></div>
                        <div><div>{{ values[tag.id]?.engineeringValue ?? '—' }} {{ tag.unit }}</div><div class="text-xs text-muted-foreground">{{ values[tag.id]?.quality }}</div></div>
                        <div v-if="(tag.access & PlcTagAccess.Write) !== 0" class="flex gap-1"><input v-model="writeValues[tag.id]" class="w-24 rounded border bg-background p-1" /><button class="rounded border px-2" @click="write(tag)">写入</button></div>
                    </div>
                    <p v-if="tags.length === 0" class="p-8 text-center text-muted-foreground">请从左侧节点树添加点位</p>
                </div>
            </section>
        </div>
    </div>
</template>
