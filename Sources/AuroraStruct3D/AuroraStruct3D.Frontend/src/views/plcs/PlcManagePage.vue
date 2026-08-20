<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Select from 'primevue/select'
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
import { useAppConfirm } from '@/composables/useAppConfirm'

const router = useRouter()
const confirmAction = useAppConfirm()
const authenticationOptions = [
    { label: '匿名', value: PlcAuthenticationType.Anonymous },
    { label: '用户名密码', value: PlcAuthenticationType.UserName },
    { label: '客户端证书', value: PlcAuthenticationType.Certificate },
]
const securityModeOptions = [
    { label: 'None', value: PlcMessageSecurityMode.None },
    { label: 'Sign', value: PlcMessageSecurityMode.Sign },
    { label: 'SignAndEncrypt', value: PlcMessageSecurityMode.SignAndEncrypt },
]
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
    if (!(await confirmAction({ message: `确认删除 PLC“${device.name}”？` }))) return
    await deletePlc(device.id)
    await load()
}
onMounted(load)
</script>

<template>
    <div class="space-y-5 p-1 sm:p-2">
        <div class="flex items-center justify-between">
            <div>
                <h1 class="text-2xl font-bold">PLC 通讯管理</h1>
                <p class="text-sm text-muted-foreground">OPC UA 已启用；S7、FINS、MC/SLMP 为后续驱动占位。</p>
            </div>
            <Button size="small" label="新增 PLC" @click="showEditor = !showEditor" />
        </div>

        <div v-if="showEditor" class="grid gap-3 rounded-lg border p-4 md:grid-cols-2">
            <label class="space-y-1 text-sm">
                名称
                <InputText v-model="form.name" size="small" class="w-full" />
            </label>
            <label class="space-y-1 text-sm">
                驱动
                <Select
                    v-model="form.driverId"
                    size="small"
                    class="w-full"
                    :options="drivers"
                    option-label="displayName"
                    option-value="driverId"
                />
            </label>
            <label class="space-y-1 text-sm md:col-span-2">
                Endpoint URL
                <InputText v-model="form.endpointUrl" size="small" class="w-full" />
            </label>
            <label class="space-y-1 text-sm">
                认证方式
                <Select
                    v-model="form.authenticationType"
                    size="small"
                    class="w-full"
                    :options="authenticationOptions"
                    option-label="label"
                    option-value="value"
                />
            </label>
            <label class="space-y-1 text-sm">
                消息安全
                <Select
                    v-model="form.messageSecurityMode"
                    size="small"
                    class="w-full"
                    :options="securityModeOptions"
                    option-label="label"
                    option-value="value"
                />
            </label>
            <template v-if="form.authenticationType === PlcAuthenticationType.UserName">
                <label class="space-y-1 text-sm">
                    用户名
                    <InputText v-model="form.userName" size="small" class="w-full" />
                </label>
                <label class="space-y-1 text-sm">
                    密码
                    <Password
                        v-model="form.password"
                        size="small"
                        :feedback="false"
                        toggle-mask
                        class="w-full"
                        input-class="w-full"
                    />
                </label>
            </template>
            <label class="flex items-center gap-2 text-sm">
                <Checkbox v-model="form.autoTrustServerCertificate" binary />
                首次自动信任服务端证书
            </label>
            <div class="flex justify-end gap-2 md:col-span-2">
                <Button size="small" severity="secondary" outlined label="取消" @click="showEditor = false" />
                <Button size="small" label="保存" @click="save" />
            </div>
        </div>

        <p v-if="testMessage" class="rounded border p-3 text-sm">{{ testMessage }}</p>
        <DataTable
            :value="devices"
            :loading="loading"
            data-key="id"
            striped-rows
            class="overflow-hidden rounded-lg border"
        >
            <Column field="name" header="名称">
                <template #body="{ data }">
                    <span class="font-medium">{{ data.name }}</span>
                </template>
            </Column>
            <Column field="driverId" header="协议" />
            <Column field="endpointUrl" header="端点">
                <template #body="{ data }">
                    <span class="font-mono text-xs">{{ data.endpointUrl }}</span>
                </template>
            </Column>
            <Column header="状态">
                <template #body="{ data }">
                    {{ ['已断开', '连接中', '已连接', '重连中', '故障'][data.connectionStatus] }}
                </template>
            </Column>
            <Column header="操作">
                <template #body="{ data }">
                    <div class="flex gap-1">
                        <Button size="small" severity="secondary" outlined label="测试" @click="test(data)" />
                        <Button
                            size="small"
                            severity="secondary"
                            outlined
                            label="点位"
                            @click="router.push(`/plcs/${data.id}/control`)"
                        />
                        <Button size="small" severity="danger" text label="删除" @click="remove(data)" />
                    </div>
                </template>
            </Column>
            <template #empty><div class="p-8 text-center text-muted-foreground">暂无 PLC 设备</div></template>
        </DataTable>
    </div>
</template>
