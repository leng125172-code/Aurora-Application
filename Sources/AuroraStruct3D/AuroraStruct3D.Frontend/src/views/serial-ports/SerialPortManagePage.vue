<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Pencil, PlugZap, RefreshCw, ScanLine, Send, Unplug } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { Card, CardContent } from '@/components/ui/card'
import { BorderBeam } from '@/components/ui/border-beam'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogClose } from '@/components/ui/dialog'

import { SerialPortHandshake, SerialPortParity, type SerialPortConfigDto, SerialPortStopBits } from '@/api/serial-ports'
import { useSerialPortStore } from '@/stores/serialPorts'

interface RawLogItem {
    id: number
    time: string
    sentHex: string
    responseHex: string
    responseText: string
    responseLength: number
}

const { t } = useI18n()
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

// 编辑表单本地类型（description 固定为 string，避免 null 与 Input v-model 冲突）
interface EditSerialPortForm {
    displayName: string
    description: string
    isEnabled: boolean
    baudRate: number
    dataBits: number
    parity: SerialPortParity
    stopBits: SerialPortStopBits
    handshake: SerialPortHandshake
}
const editForm = ref<EditSerialPortForm>({
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
const supportedBaudRates = computed(
    () =>
        selectedPort.value?.supportedBaudRates ?? [
            110, 300, 600, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600,
        ]
)

// ─── 字符串双向绑定（shadcn-vue Select 需要字符串值） ─────────────────────
const connectBaudRateStr = computed({
    get: () => String(connectBaudRate.value),
    set: (v: string) => {
        connectBaudRate.value = parseInt(v)
    },
})
const editBaudRateStr = computed({
    get: () => String(editForm.value.baudRate),
    set: (v: string) => {
        editForm.value.baudRate = parseInt(v)
    },
})
const editParityStr = computed({
    get: () => String(editForm.value.parity),
    set: (v: string) => {
        editForm.value.parity = parseInt(v) as SerialPortParity
    },
})
const editStopBitsStr = computed({
    get: () => String(editForm.value.stopBits),
    set: (v: string) => {
        editForm.value.stopBits = parseInt(v) as SerialPortStopBits
    },
})
const editHandshakeStr = computed({
    get: () => String(editForm.value.handshake),
    set: (v: string) => {
        editForm.value.handshake = parseInt(v) as SerialPortHandshake
    },
})

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

/**
 * 将连续十六进制字符串格式化为 "XX XX XX" 形式，便于阅读。
 */
function formatHexBytes(hex: string | null | undefined): string {
    if (!hex) return '—'
    const cleaned = hex.replace(/\s+/g, '')
    const matched = cleaned.match(/.{1,2}/g)
    return matched ? matched.join(' ').toUpperCase() : cleaned.toUpperCase()
}

async function handleScan() {
    scanning.value = true
    try {
        const result = await store.scan()
        toast.success(t('serialPort.scanSuccess', { count: result.count }))
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
        toast.success(t('serialPort.connectSuccess'))
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
        toast.success(t('serialPort.disconnectSuccess'))
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
        toast.success(t('serialPort.updateSuccess'))
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
            return t('serialPort.parityOdd')
        case SerialPortParity.Even:
            return t('serialPort.parityEven')
        case SerialPortParity.Mark:
            return t('serialPort.parityMark')
        case SerialPortParity.Space:
            return t('serialPort.paritySpace')
        default:
            return t('serialPort.parityNone')
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
            return t('serialPort.handshakeXOnXOff')
        case SerialPortHandshake.RequestToSend:
            return t('serialPort.handshakeRts')
        case SerialPortHandshake.RequestToSendXOnXOff:
            return t('serialPort.handshakeRtsXOn')
        default:
            return t('serialPort.handshakeNone')
    }
}

onMounted(() => {
    void refreshList()
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('serialPort.title') }}</h1>
            <div class="flex flex-wrap gap-2">
                <Button variant="outline" size="sm" @click="refreshList">
                    <RefreshCw class="size-4" />
                    {{ t('serialPort.refresh') }}
                </Button>
                <Button size="sm" :disabled="scanning" @click="handleScan">
                    <ScanLine class="size-4" />
                    {{ scanning ? t('serialPort.scanning') : t('serialPort.scan') }}
                </Button>
            </div>
        </div>

        <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_420px]">
            <div class="min-w-0">
                <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">
                    {{ t('common.loading') }}
                </div>
                <div v-else-if="store.ports.length === 0" class="py-8 text-center text-sm text-muted-foreground">
                    {{ t('serialPort.noDevices') }}
                </div>
                <Card v-else class="overflow-auto">
                    <CardContent class="p-0">
                        <table class="w-full min-w-[920px] text-sm">
                            <thead class="border-b bg-muted/50">
                                <tr>
                                    <th class="px-3 py-2 text-left font-medium">{{ t('serialPort.name') }}</th>
                                    <th class="px-3 py-2 text-left font-medium">{{ t('serialPort.portName') }}</th>
                                    <th class="px-3 py-2 text-left font-medium">{{ t('serialPort.baudRate') }}</th>
                                    <th class="px-3 py-2 text-left font-medium">{{ t('serialPort.params') }}</th>
                                    <th class="px-3 py-2 text-left font-medium">{{ t('serialPort.status') }}</th>
                                    <th class="px-3 py-2 text-left font-medium">{{ t('common.actions') }}</th>
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
                                        <div
                                            v-if="port.description"
                                            class="max-w-[220px] truncate text-xs text-muted-foreground"
                                        >
                                            {{ port.description }}
                                        </div>
                                    </td>
                                    <td class="px-3 py-2 font-mono text-xs">{{ port.portName }}</td>
                                    <td class="px-3 py-2">{{ port.baudRate || t('serialPort.notSet') }}</td>
                                    <td class="px-3 py-2 text-xs text-muted-foreground">
                                        {{ port.dataBits }} / {{ parityText(port.parity) }} /
                                        {{ stopBitsText(port.stopBits) }} / {{ handshakeText(port.handshake) }}
                                    </td>
                                    <td class="px-3 py-2">
                                        <span :class="port.isOpen ? 'text-green-600' : 'text-muted-foreground'">
                                            {{ port.isOpen ? t('serialPort.connected') : t('serialPort.disconnected') }}
                                        </span>
                                    </td>
                                    <td class="px-3 py-2">
                                        <div class="flex flex-wrap gap-1.5">
                                            <Button
                                                v-if="!port.isOpen"
                                                variant="outline"
                                                size="xs"
                                                class="text-green-600 hover:bg-green-50 dark:hover:bg-green-900/30"
                                                :disabled="connecting"
                                                @click.stop="handleConnect(port)"
                                            >
                                                <PlugZap class="size-3.5" />
                                                {{ t('serialPort.connect') }}
                                            </Button>
                                            <Button
                                                v-else
                                                variant="outline"
                                                size="xs"
                                                class="text-orange-500 hover:bg-orange-50 dark:hover:bg-orange-900/30"
                                                :disabled="connecting"
                                                @click.stop="handleDisconnect(port)"
                                            >
                                                <Unplug class="size-3.5" />
                                                {{ t('serialPort.disconnect') }}
                                            </Button>
                                            <Button variant="outline" size="xs" @click.stop="openEdit(port)">
                                                <Pencil class="size-3.5" />
                                                {{ t('common.edit') }}
                                            </Button>
                                        </div>
                                    </td>
                                </tr>
                            </tbody>
                        </table>
                    </CardContent>
                </Card>
            </div>

            <Card class="relative">
                <BorderBeam :size="80" :duration="8" :delay="2" />
                <CardContent class="p-4">
                    <div v-if="!selectedPort" class="py-10 text-center text-sm text-muted-foreground">
                        {{ t('serialPort.selectPort') }}
                    </div>
                    <div v-else class="flex flex-col gap-4">
                        <div class="flex items-start justify-between gap-3">
                            <div>
                                <h2 class="text-base font-semibold">{{ selectedPort.displayName }}</h2>
                                <div class="mt-1 font-mono text-xs text-muted-foreground">
                                    {{ selectedPort.portName }}
                                </div>
                            </div>
                            <span
                                :class="
                                    selectedPort.isOpen ? 'text-sm text-green-600' : 'text-sm text-muted-foreground'
                                "
                            >
                                {{ selectedPort.isOpen ? t('serialPort.connected') : t('serialPort.disconnected') }}
                            </span>
                        </div>

                        <div class="grid grid-cols-[1fr_auto] gap-2">
                            <div class="flex flex-col gap-1">
                                <Label class="text-sm">{{ t('serialPort.baudRate') }}</Label>
                                <Select v-model="connectBaudRateStr">
                                    <SelectTrigger>
                                        <SelectValue />
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectItem
                                            v-for="rate in supportedBaudRates"
                                            :key="rate"
                                            :value="String(rate)"
                                        >
                                            {{ rate }}
                                        </SelectItem>
                                    </SelectContent>
                                </Select>
                            </div>
                            <Button
                                v-if="!selectedPort.isOpen"
                                size="sm"
                                class="mt-6"
                                :disabled="connecting"
                                @click="handleConnect(selectedPort)"
                            >
                                <PlugZap class="size-4" />
                                {{ t('serialPort.connect') }}
                            </Button>
                            <Button
                                v-else
                                variant="outline"
                                size="sm"
                                class="mt-6 text-orange-500 hover:bg-orange-50 dark:hover:bg-orange-900/30"
                                :disabled="connecting"
                                @click="handleDisconnect(selectedPort)"
                            >
                                <Unplug class="size-4" />
                                {{ t('serialPort.disconnect') }}
                            </Button>
                        </div>

                        <div class="border-t pt-4">
                            <div class="mb-3 flex items-center justify-between">
                                <h3 class="text-sm font-medium">{{ t('serialPort.debug') }}</h3>
                                <Button variant="outline" size="xs" @click="rawLogs = []">
                                    {{ t('serialPort.clearLog') }}
                                </Button>
                            </div>
                            <textarea
                                v-model="rawForm.payload"
                                class="h-24 w-full resize-none rounded border bg-background px-2 py-1.5 font-mono text-xs"
                                placeholder="01 03 10 03 00 01 B0 CA"
                            />
                            <div class="mt-3 grid grid-cols-2 gap-2">
                                <label class="flex items-center gap-2 text-sm">
                                    <input v-model="rawForm.isHex" type="checkbox" />
                                    {{ t('serialPort.hexMode') }}
                                </label>
                                <label class="flex items-center gap-2 text-sm">
                                    <input v-model="rawForm.appendNewLine" type="checkbox" :disabled="rawForm.isHex" />
                                    {{ t('serialPort.appendNewLine') }}
                                </label>
                                <div class="flex flex-col gap-1">
                                    <Label class="text-sm">{{ t('serialPort.expectedLength') }}</Label>
                                    <Input v-model.number="rawForm.expectedResponseLength" min="-1" type="number" />
                                </div>
                                <div class="flex flex-col gap-1">
                                    <Label class="text-sm">{{ t('serialPort.timeoutMs') }}</Label>
                                    <Input v-model.number="rawForm.timeoutMs" min="50" type="number" />
                                </div>
                            </div>
                            <Button
                                size="sm"
                                class="mt-3 w-full"
                                :disabled="sending || !rawForm.payload.trim()"
                                @click="handleSendRaw"
                            >
                                <Send class="size-4" />
                                {{ sending ? t('serialPort.sending') : t('serialPort.send') }}
                            </Button>
                        </div>

                        <div class="max-h-[340px] overflow-auto rounded border bg-muted/20">
                            <div v-if="rawLogs.length === 0" class="py-8 text-center text-xs text-muted-foreground">
                                {{ t('serialPort.noLogs') }}
                            </div>
                            <div v-for="item in rawLogs" :key="item.id" class="border-b p-3 last:border-0">
                                <div class="mb-1 flex items-center justify-between text-xs text-muted-foreground">
                                    <span>{{ item.time }}</span>
                                    <span>{{ item.responseLength }} B</span>
                                </div>
                                <div class="font-mono text-xs text-blue-600">TX {{ formatHexBytes(item.sentHex) }}</div>
                                <div class="mt-1 font-mono text-xs text-green-600">
                                    RX {{ formatHexBytes(item.responseHex) }}
                                </div>
                                <div v-if="item.responseText" class="mt-1 break-words text-xs text-muted-foreground">
                                    {{ item.responseText }}
                                </div>
                            </div>
                        </div>
                    </div>
                </CardContent>
            </Card>
        </div>

        <!-- 编辑串口对话框 -->
        <Dialog v-model:open="showEditDialog">
            <DialogContent class="w-[460px]">
                <BorderBeam :size="80" :duration="8" />
                <DialogHeader>
                    <DialogTitle>{{ t('serialPort.editTitle') }}</DialogTitle>
                </DialogHeader>
                <div class="grid gap-3">
                    <div class="flex flex-col gap-1">
                        <Label>{{ t('serialPort.displayName') }}</Label>
                        <Input v-model="editForm.displayName" />
                    </div>
                    <div class="flex flex-col gap-1">
                        <Label>{{ t('serialPort.description') }}</Label>
                        <Input v-model="editForm.description" />
                    </div>
                    <div class="grid grid-cols-2 gap-3">
                        <div class="flex flex-col gap-1">
                            <Label>{{ t('serialPort.baudRate') }}</Label>
                            <Select v-model="editBaudRateStr">
                                <SelectTrigger>
                                    <SelectValue />
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem v-for="rate in supportedBaudRates" :key="rate" :value="String(rate)">
                                        {{ rate }}
                                    </SelectItem>
                                </SelectContent>
                            </Select>
                        </div>
                        <div class="flex flex-col gap-1">
                            <Label>{{ t('serialPort.dataBits') }}</Label>
                            <Input v-model.number="editForm.dataBits" max="8" min="5" type="number" />
                        </div>
                        <div class="flex flex-col gap-1">
                            <Label>{{ t('serialPort.parity') }}</Label>
                            <Select v-model="editParityStr">
                                <SelectTrigger>
                                    <SelectValue />
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem :value="String(SerialPortParity.None)">
                                        {{ t('serialPort.parityNone') }}
                                    </SelectItem>
                                    <SelectItem :value="String(SerialPortParity.Odd)">
                                        {{ t('serialPort.parityOdd') }}
                                    </SelectItem>
                                    <SelectItem :value="String(SerialPortParity.Even)">
                                        {{ t('serialPort.parityEven') }}
                                    </SelectItem>
                                    <SelectItem :value="String(SerialPortParity.Mark)">
                                        {{ t('serialPort.parityMark') }}
                                    </SelectItem>
                                    <SelectItem :value="String(SerialPortParity.Space)">
                                        {{ t('serialPort.paritySpace') }}
                                    </SelectItem>
                                </SelectContent>
                            </Select>
                        </div>
                        <div class="flex flex-col gap-1">
                            <Label>{{ t('serialPort.stopBits') }}</Label>
                            <Select v-model="editStopBitsStr">
                                <SelectTrigger>
                                    <SelectValue />
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem :value="String(SerialPortStopBits.One)">1</SelectItem>
                                    <SelectItem :value="String(SerialPortStopBits.OnePointFive)">1.5</SelectItem>
                                    <SelectItem :value="String(SerialPortStopBits.Two)">2</SelectItem>
                                </SelectContent>
                            </Select>
                        </div>
                    </div>
                    <div class="flex flex-col gap-1">
                        <Label>{{ t('serialPort.handshake') }}</Label>
                        <Select v-model="editHandshakeStr">
                            <SelectTrigger>
                                <SelectValue />
                            </SelectTrigger>
                            <SelectContent>
                                <SelectItem :value="String(SerialPortHandshake.None)">
                                    {{ t('serialPort.handshakeNone') }}
                                </SelectItem>
                                <SelectItem :value="String(SerialPortHandshake.XOnXOff)">
                                    {{ t('serialPort.handshakeXOnXOff') }}
                                </SelectItem>
                                <SelectItem :value="String(SerialPortHandshake.RequestToSend)">
                                    {{ t('serialPort.handshakeRts') }}
                                </SelectItem>
                                <SelectItem :value="String(SerialPortHandshake.RequestToSendXOnXOff)">
                                    {{ t('serialPort.handshakeRtsXOn') }}
                                </SelectItem>
                            </SelectContent>
                        </Select>
                    </div>
                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="editForm.isEnabled" type="checkbox" />
                        {{ t('serialPort.enabled') }}
                    </label>
                </div>
                <DialogFooter class="gap-2">
                    <DialogClose as-child>
                        <Button variant="outline">{{ t('common.cancel') }}</Button>
                    </DialogClose>
                    <Button :disabled="editing" @click="handleEdit">
                        {{ editing ? t('common.saving') : t('common.save') }}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    </div>
</template>
