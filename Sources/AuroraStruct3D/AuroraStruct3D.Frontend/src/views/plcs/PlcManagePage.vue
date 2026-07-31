<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
    PlcAuthenticationType,
    PlcMessageSecurityMode,
    PlcProtocolType,
    createPlc,
    deletePlc,
    getPlcDrivers,
    getPlcs,
    testPlc,
    type PlcDeviceDto,
    type PlcDriverDescriptor,
    type SavePlcDeviceDto,
} from '@/api/plcs'

const router = useRouter()
const devices = ref<PlcDeviceDto[]>([])
const drivers = ref<PlcDriverDescriptor[]>([])
const loading = ref(false)
const showEditor = ref(false)
const testMessage = ref('')
const form = reactive<SavePlcDeviceDto>({
    name: '',
    protocol: PlcProtocolType.OpcUa,
    driverId: 'opc-ua',
    isEnabled: true,
    endpointUrl: 'opc.tcp://localhost:4840',
    authenticationType: PlcAuthenticationType.Anonymous,
    userName: '',
    securityPolicy: 'None',
    messageSecurityMode: PlcMessageSecurityMode.None,
    autoTrustServerCertificate: true,
    connectTimeoutMs: 5000,
    operationTimeoutMs: 5000,
    sessionTimeoutMs: 60000,
    keepAliveMs: 5000,
    reconnectInitialMs: 1000,
    reconnectMaxMs: 30000,
    idleTimeoutMs: 600000,
})

async function load() {
    loading.value = true
    try {
        ;[devices.value, drivers.value] = await Promise.all([getPlcs(), getPlcDrivers()])
    } finally {
        loading.value = false
    }
}
async function save() {
    await createPlc({ ...form })
    showEditor.value = false
    await load()
}
async function test(device: PlcDeviceDto) {
    const result = await testPlc(device.id)
    testMessage.value = result.success ? `连接成功（${result.durationMs} ms）` : `连接失败：${result.error}`
}
async function remove(device: PlcDeviceDto) {
    if (!confirm(`确认删除 PLC“${device.name}”？`)) return
    await deletePlc(device.id)
    await load()
}
onMounted(load)
</script>

<template>
    <div class="space-y-5 p-6">
        <div class="flex items-center justify-between">
            <div>
                <h1 class="text-2xl font-bold">PLC 通讯管理</h1>
                <p class="text-sm text-muted-foreground">OPC UA 已启用；S7、FINS、MC/SLMP 为后续驱动占位。</p>
            </div>
            <button class="rounded-md bg-primary px-4 py-2 text-primary-foreground" @click="showEditor = !showEditor">新增 PLC</button>
        </div>

        <div v-if="showEditor" class="grid gap-3 rounded-lg border p-4 md:grid-cols-2">
            <label class="space-y-1 text-sm">名称<input v-model="form.name" class="w-full rounded border bg-background p-2" /></label>
            <label class="space-y-1 text-sm">驱动
                <select v-model="form.driverId" class="w-full rounded border bg-background p-2">
                    <option v-for="driver in drivers" :key="driver.driverId" :value="driver.driverId" :disabled="!driver.isInstalled">
                        {{ driver.displayName }}{{ driver.isInstalled ? '' : '（未安装）' }}
                    </option>
                </select>
            </label>
            <label class="space-y-1 text-sm md:col-span-2">Endpoint URL<input v-model="form.endpointUrl" class="w-full rounded border bg-background p-2" /></label>
            <label class="space-y-1 text-sm">认证方式
                <select v-model.number="form.authenticationType" class="w-full rounded border bg-background p-2">
                    <option :value="0">匿名</option><option :value="1">用户名密码</option><option :value="2">客户端证书</option>
                </select>
            </label>
            <label class="space-y-1 text-sm">消息安全
                <select v-model.number="form.messageSecurityMode" class="w-full rounded border bg-background p-2">
                    <option :value="0">None</option><option :value="1">Sign</option><option :value="2">SignAndEncrypt</option>
                </select>
            </label>
            <template v-if="form.authenticationType === PlcAuthenticationType.UserName">
                <label class="space-y-1 text-sm">用户名<input v-model="form.userName" class="w-full rounded border bg-background p-2" /></label>
                <label class="space-y-1 text-sm">密码<input v-model="form.password" type="password" class="w-full rounded border bg-background p-2" /></label>
            </template>
            <label class="flex items-center gap-2 text-sm"><input v-model="form.autoTrustServerCertificate" type="checkbox" />首次自动信任服务端证书</label>
            <div class="flex justify-end gap-2 md:col-span-2">
                <button class="rounded border px-4 py-2" @click="showEditor = false">取消</button>
                <button class="rounded bg-primary px-4 py-2 text-primary-foreground" @click="save">保存</button>
            </div>
        </div>

        <p v-if="testMessage" class="rounded border p-3 text-sm">{{ testMessage }}</p>
        <div class="overflow-hidden rounded-lg border">
            <table class="w-full text-sm">
                <thead class="bg-muted/60"><tr><th class="p-3 text-left">名称</th><th class="p-3 text-left">协议</th><th class="p-3 text-left">端点</th><th class="p-3">状态</th><th class="p-3">操作</th></tr></thead>
                <tbody>
                    <tr v-for="device in devices" :key="device.id" class="border-t">
                        <td class="p-3 font-medium">{{ device.name }}</td>
                        <td class="p-3">{{ device.driverId }}</td>
                        <td class="p-3 font-mono text-xs">{{ device.endpointUrl }}</td>
                        <td class="p-3 text-center">{{ ['已断开', '连接中', '已连接', '重连中', '故障'][device.connectionStatus] }}</td>
                        <td class="space-x-2 p-3 text-center">
                            <button class="rounded border px-2 py-1" @click="test(device)">测试</button>
                            <button class="rounded border px-2 py-1" @click="router.push(`/plcs/${device.id}/control`)">点位</button>
                            <button class="rounded border px-2 py-1 text-destructive" @click="remove(device)">删除</button>
                        </td>
                    </tr>
                    <tr v-if="!loading && devices.length === 0"><td colspan="5" class="p-8 text-center text-muted-foreground">暂无 PLC 设备</td></tr>
                </tbody>
            </table>
        </div>
    </div>
</template>
