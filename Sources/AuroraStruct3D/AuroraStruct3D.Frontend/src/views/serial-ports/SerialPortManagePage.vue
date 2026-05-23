<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { Pencil, PlugZap, RefreshCw, ScanLine, Send, Unplug } from 'lucide-vue-next'
import { toast } from 'vue-sonner'

import {
    SerialPortHandshake,
    SerialPortParity,
    type SerialPortConfigDto,
    SerialPortStopBits,
    type UpdateSerialPortConfigDto,
} from '@/api/serial-ports'
import { useSerialPortStore } from '@/stores/serialPorts'

interface RawLogItem {
    id: number
    time: string
    sentHex: string
    responseHex: string
    responseText: string
    responseLength: number
}

const store = useSerialPortStore()
const scanning = ref(false)
const connecting = ref(false)
const sending = ref(false)
const selectedId = ref('')
const connectBaudRate = ref(115200)

const rawForm = ref({
    payload: '',
    isHex: true,
    appendNewLine: false,
    expectedResponseLength: -1,
    timeoutMs: 500,
})
const rawLogs = ref<RawLogItem[]>([])

const showEditDialog = ref(false)
const editingId = ref('')
const editing = ref(false)
const editForm = ref<UpdateSerialPortConfigDto>({
    displayName: '',
    description: '',
    isEnabled: true,
    baudRate: 115200,
    dataBits: 8,
    parity: SerialPortParity.None,
    stopBits: SerialPortStopBits.One,
    handshake: SerialPortHandshake.None,
})

const selectedPort = computed(() => store.ports.find((port) => port.id === selectedId.value) ?? null)
const supportedBaudRates = computed(() => selectedPort.value?.supportedBaudRates ?? [110, 300, 600, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600])

watch(
    () => store.ports,
    (ports) => {
        if (ports.length === 0) {
            selectedId.value = ''
            return
        }
        if (!selectedId.value || !ports.some((port) => port.id === selectedId.value)) {
            selectedId.value = ports[0].id
        }
    },
    { deep: true }
)

watch(
    selectedPort,
    (port) => {
        if (!port) return
        connectBaudRate.value = port.baudRate || 115200
    },
    { immediate: true }
)

async function refreshList() {
    await store.fetchList()
}

async function handleScan() {
    scanning.value = true
    try {
        const result = await store.scan()
        toast.success(`扫描完成，发现 ${result.count} 个串口`)
    } catch {
        // httpClient 已统一弹出错误
    } finally {
        scanning.value = false
    }
}

async function handleConnect(port: SerialPortConfigDto) {
    connecting.value = true
    try {
        const baudRate = connectBaudRate.value || port.baudRate || 115200
        await store.connect(port.id, { baudRate })
        toast.success('串口已连接')
    } catch {
        // 忽略
    } finally {
        connecting.value = false
    }
}

async function handleDisconnect(port: SerialPortConfigDto) {
    connecting.value = true
    try {
        await store.disconnect(port.id)
        toast.success('串口已断开')
    } catch {
        // 忽略
    } finally {
        connecting.value = false
    }
}

function openEdit(port: SerialPortConfigDto) {
    editingId.value = port.id
    editForm.value = {
        displayName: port.displayName,
        description: port.description ?? '',
        isEnabled: port.isEnabled,
        baudRate: port.baudRate || 115200,
        dataBits: port.dataBits,
        parity: port.parity,
        stopBits: port.stopBits,
        handshake: port.handshake,
    }
    showEditDialog.value = true
}

async function handleEdit() {
    editing.value = true
    try {
        await store.update(editingId.value, editForm.value)
        toast.success('串口信息已更新')
        showEditDialog.value = false
    } catch {
        // 忽略
    } finally {
        editing.value = false
    }
}

async function handleSendRaw() {
    const port = selectedPort.value
    if (!port) return
    sending.value = true
    try {
        const response = await store.sendRaw(port.id, rawForm.value)
        rawLogs.value.unshift({
            id: Date.now(),
            time: new Date().toLocaleTimeString(),
            sentHex: response.sentHex,
            responseHex: response.responseHex,
            responseText: response.responseText,
            responseLength: response.responseLength,
        })
        rawLogs.value = rawLogs.value.slice(0, 80)
    } catch {
        // 忽略
    } finally {
        sending.value = false
    }
}

function parityText(value: SerialPortParity): string {
    switch (value) {
        case SerialPortParity.Odd:
            return '奇校验'
        case SerialPortParity.Even:
            return '偶校验'
        case SerialPortParity.Mark:
            return 'Mark'
        case SerialPortParity.Space:
            return 'Space'
        default:
            return '无校验'
    }
}

function stopBitsText(value: SerialPortStopBits): string {
    switch (value) {
        case SerialPortStopBits.Two:
            return '2'
        case SerialPortStopBits.OnePointFive:
            return '1.5'
        case SerialPortStopBits.None:
            return '0'
        default:
            return '1'
    }
}

function handshakeText(value: SerialPortHandshake): string {
    switch (value) {
        case SerialPortHandshake.XOnXOff:
            return 'XOn/XOff'
        case SerialPortHandshake.RequestToSend:
            return 'RTS'
        case SerialPortHandshake.RequestToSendXOnXOff:
            return 'RTS + XOn/XOff'
        default:
            return '无'
    }
}

onMounted(() => {
    void refreshList()
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
            <h1 class="text-lg font-semibold">485 串口管理</h1>
            <div class="flex flex-wrap gap-2">
                <button class="inline-flex items-center gap-1.5 rounded border px-3 py-1.5 text-sm hover:bg-muted/50" @click="refreshList">
                    <RefreshCw class="size-4" />
                    刷新
                </button>
                <button
                    class="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-60"
                    :disabled="scanning"
                    @click="handleScan"
                >
                    <ScanLine class="size-4" />
                    {{ scanning ? '扫描中…' : '扫描串口' }}
                </button>
            </div>
        </div>

        <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_420px]">
            <div class="min-w-0">
                <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">加载中…</div>
                <div v-else-if="store.ports.length === 0" class="py-8 text-center text-sm text-muted-foreground">
                    暂无串口，请点击「扫描串口」同步本机串口
                </div>
                <div v-else class="overflow-auto rounded-lg border">
                    <table class="w-full min-w-[920px] text-sm">
                        <thead class="border-b bg-muted/50">
                            <tr>
                                <th class="px-3 py-2 text-left font-medium">名称</th>
                                <th class="px-3 py-2 text-left font-medium">串口号</th>
                                <th class="px-3 py-2 text-left font-medium">连接波特率</th>
                                <th class="px-3 py-2 text-left font-medium">参数</th>
                                <th class="px-3 py-2 text-left font-medium">状态</th>
                                <th class="px-3 py-2 text-left font-medium">控制</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr
                                v-for="port in store.ports"
                                :key="port.id"
                                :class="[
                                    'cursor-pointer border-b last:border-0 hover:bg-muted/30',
                                    selectedId === port.id ? 'bg-muted/40' : '',
                                ]"
                                @click="selectedId = port.id"
                            >
                                <td class="px-3 py-2">
                                    <div class="font-medium">{{ port.displayName }}</div>
                                    <div class="max-w-[220px] truncate text-xs text-muted-foreground">{{ port.description || '—' }}</div>
                                </td>
                                <td class="px-3 py-2 font-mono text-xs">{{ port.portName }}</td>
                                <td class="px-3 py-2">{{ port.baudRate || '未设置' }}</td>
                                <td class="px-3 py-2 text-xs text-muted-foreground">
                                    {{ port.dataBits }} / {{ parityText(port.parity) }} / {{ stopBitsText(port.stopBits) }} / {{ handshakeText(port.handshake) }}
                                </td>
                                <td class="px-3 py-2">
                                    <span :class="port.isOpen ? 'text-green-600' : 'text-muted-foreground'">
                                        {{ port.isOpen ? '已连接' : '未连接' }}
                                    </span>
                                </td>
                                <td class="px-3 py-2">
                                    <div class="flex flex-wrap gap-1.5">
                                        <button
                                            v-if="!port.isOpen"
                                            class="inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs text-green-600 hover:bg-green-50"
                                            :disabled="connecting"
                                            @click.stop="handleConnect(port)"
                                        >
                                            <PlugZap class="size-3.5" />
                                            连接
                                        </button>
                                        <button
                                            v-else
                                            class="inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs text-orange-500 hover:bg-orange-50"
                                            :disabled="connecting"
                                            @click.stop="handleDisconnect(port)"
                                        >
                                            <Unplug class="size-3.5" />
                                            断开
                                        </button>
                                        <button
                                            class="inline-flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-muted/50"
                                            @click.stop="openEdit(port)"
                                        >
                                            <Pencil class="size-3.5" />
                                            编辑
                                        </button>
                                    </div>
                                </td>
                            </tr>
                        </tbody>
                    </table>
                </div>
            </div>

            <aside class="rounded-lg border p-4">
                <div v-if="!selectedPort" class="py-10 text-center text-sm text-muted-foreground">请选择一个串口</div>
                <div v-else class="flex flex-col gap-4">
                    <div class="flex items-start justify-between gap-3">
                        <div>
                            <h2 class="text-base font-semibold">{{ selectedPort.displayName }}</h2>
                            <div class="mt-1 font-mono text-xs text-muted-foreground">{{ selectedPort.portName }}</div>
                        </div>
                        <span :class="selectedPort.isOpen ? 'text-sm text-green-600' : 'text-sm text-muted-foreground'">
                            {{ selectedPort.isOpen ? '已连接' : '未连接' }}
                        </span>
                    </div>

                    <div class="grid grid-cols-[1fr_auto] gap-2">
                        <label class="flex flex-col gap-1 text-sm">
                            波特率
                            <select v-model.number="connectBaudRate" class="rounded border bg-background px-2 py-1.5 text-sm">
                                <option v-for="rate in supportedBaudRates" :key="rate" :value="rate">{{ rate }}</option>
                            </select>
                        </label>
                        <button
                            v-if="!selectedPort.isOpen"
                            class="mt-6 inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-60"
                            :disabled="connecting"
                            @click="handleConnect(selectedPort)"
                        >
                            <PlugZap class="size-4" />
                            连接
                        </button>
                        <button
                            v-else
                            class="mt-6 inline-flex items-center gap-1.5 rounded border px-3 py-1.5 text-sm text-orange-500 hover:bg-orange-50 disabled:opacity-60"
                            :disabled="connecting"
                            @click="handleDisconnect(selectedPort)"
                        >
                            <Unplug class="size-4" />
                            断开
                        </button>
                    </div>

                    <div class="border-t pt-4">
                        <div class="mb-3 flex items-center justify-between">
                            <h3 class="text-sm font-medium">串口调试</h3>
                            <button class="rounded border px-2 py-0.5 text-xs hover:bg-muted/50" @click="rawLogs = []">清空</button>
                        </div>
                        <textarea
                            v-model="rawForm.payload"
                            class="h-24 w-full resize-none rounded border bg-background px-2 py-1.5 font-mono text-xs"
                            placeholder="01 03 10 03 00 01 B0 CA"
                        />
                        <div class="mt-3 grid grid-cols-2 gap-2">
                            <label class="flex items-center gap-2 text-sm">
                                <input v-model="rawForm.isHex" type="checkbox" />
                                HEX
                            </label>
                            <label class="flex items-center gap-2 text-sm">
                                <input v-model="rawForm.appendNewLine" type="checkbox" :disabled="rawForm.isHex" />
                                追加换行
                            </label>
                            <label class="flex flex-col gap-1 text-sm">
                                响应长度
                                <input v-model.number="rawForm.expectedResponseLength" class="rounded border bg-background px-2 py-1.5 text-sm" min="-1" type="number" />
                            </label>
                            <label class="flex flex-col gap-1 text-sm">
                                超时 ms
                                <input v-model.number="rawForm.timeoutMs" class="rounded border bg-background px-2 py-1.5 text-sm" min="50" type="number" />
                            </label>
                        </div>
                        <button
                            class="mt-3 inline-flex w-full items-center justify-center gap-1.5 rounded bg-primary px-3 py-2 text-sm text-primary-foreground hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-60"
                            :disabled="sending || !rawForm.payload.trim()"
                            @click="handleSendRaw"
                        >
                            <Send class="size-4" />
                            {{ sending ? '发送中…' : '发送' }}
                        </button>
                    </div>

                    <div class="max-h-[340px] overflow-auto rounded border bg-muted/20">
                        <div v-if="rawLogs.length === 0" class="py-8 text-center text-xs text-muted-foreground">暂无收发记录</div>
                        <div v-for="item in rawLogs" :key="item.id" class="border-b p-3 last:border-0">
                            <div class="mb-1 flex items-center justify-between text-xs text-muted-foreground">
                                <span>{{ item.time }}</span>
                                <span>{{ item.responseLength }} B</span>
                            </div>
                            <div class="font-mono text-xs text-blue-600">TX {{ item.sentHex || '—' }}</div>
                            <div class="mt-1 font-mono text-xs text-green-600">RX {{ item.responseHex || '—' }}</div>
                            <div v-if="item.responseText" class="mt-1 break-words text-xs text-muted-foreground">{{ item.responseText }}</div>
                        </div>
                    </div>
                </div>
            </aside>
        </div>

        <div v-if="showEditDialog" class="fixed inset-0 z-50 flex items-center justify-center bg-black/40" @click.self="showEditDialog = false">
            <div class="w-[460px] rounded-lg border bg-background p-6 shadow-lg">
                <h2 class="mb-4 text-base font-semibold">编辑串口</h2>
                <div class="grid gap-3">
                    <label class="flex flex-col gap-1 text-sm">
                        名称
                        <input v-model="editForm.displayName" class="rounded border bg-background px-2 py-1.5 text-sm" />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        描述
                        <input v-model="editForm.description" class="rounded border bg-background px-2 py-1.5 text-sm" />
                    </label>
                    <div class="grid grid-cols-2 gap-3">
                        <label class="flex flex-col gap-1 text-sm">
                            波特率
                            <select v-model.number="editForm.baudRate" class="rounded border bg-background px-2 py-1.5 text-sm">
                                <option v-for="rate in supportedBaudRates" :key="rate" :value="rate">{{ rate }}</option>
                            </select>
                        </label>
                        <label class="flex flex-col gap-1 text-sm">
                            数据位
                            <input v-model.number="editForm.dataBits" class="rounded border bg-background px-2 py-1.5 text-sm" max="8" min="5" type="number" />
                        </label>
                        <label class="flex flex-col gap-1 text-sm">
                            校验位
                            <select v-model.number="editForm.parity" class="rounded border bg-background px-2 py-1.5 text-sm">
                                <option :value="SerialPortParity.None">无校验</option>
                                <option :value="SerialPortParity.Odd">奇校验</option>
                                <option :value="SerialPortParity.Even">偶校验</option>
                                <option :value="SerialPortParity.Mark">Mark</option>
                                <option :value="SerialPortParity.Space">Space</option>
                            </select>
                        </label>
                        <label class="flex flex-col gap-1 text-sm">
                            停止位
                            <select v-model.number="editForm.stopBits" class="rounded border bg-background px-2 py-1.5 text-sm">
                                <option :value="SerialPortStopBits.One">1</option>
                                <option :value="SerialPortStopBits.OnePointFive">1.5</option>
                                <option :value="SerialPortStopBits.Two">2</option>
                            </select>
                        </label>
                    </div>
                    <label class="flex flex-col gap-1 text-sm">
                        流控
                        <select v-model.number="editForm.handshake" class="rounded border bg-background px-2 py-1.5 text-sm">
                            <option :value="SerialPortHandshake.None">无</option>
                            <option :value="SerialPortHandshake.XOnXOff">XOn/XOff</option>
                            <option :value="SerialPortHandshake.RequestToSend">RTS</option>
                            <option :value="SerialPortHandshake.RequestToSendXOnXOff">RTS + XOn/XOff</option>
                        </select>
                    </label>
                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="editForm.isEnabled" type="checkbox" />
                        启用
                    </label>
                </div>
                <div class="mt-5 flex justify-end gap-2">
                    <button class="rounded border px-4 py-1.5 text-sm hover:bg-muted/50" @click="showEditDialog = false">取消</button>
                    <button
                        class="rounded bg-primary px-4 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                        :disabled="editing"
                        @click="handleEdit"
                    >
                        {{ editing ? '保存中…' : '保存' }}
                    </button>
                </div>
            </div>
        </div>
    </div>
</template>
