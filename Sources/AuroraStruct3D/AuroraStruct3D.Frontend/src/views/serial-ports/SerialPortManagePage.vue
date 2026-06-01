<script setup lang="ts">
import { computed, onMounted, ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Pencil, PlugZap, RefreshCw, ScanLine, Send, Unplug } from '@lucide/vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import Dialog from 'primevue/dialog'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ToggleSwitch from 'primevue/toggleswitch'
import { AppCard } from '@/components/primevue'
import { useAppToast } from '@/composables/useAppToast'

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
const toast = useAppToast()
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

// ─── 展开行（有描述的串口自动展开）─────────────────────
const expandedRows = ref<Record<string, boolean>>({}) as Ref<Record<string, boolean>>
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

// 编辑对话框中下拉框的可选项（PrimeVue Select 需要数组 + label/value）
const parityOptions = computed(() => [
    { label: t('serialPort.parityNone'), value: String(SerialPortParity.None) },
    { label: t('serialPort.parityOdd'), value: String(SerialPortParity.Odd) },
    { label: t('serialPort.parityEven'), value: String(SerialPortParity.Even) },
    { label: t('serialPort.parityMark'), value: String(SerialPortParity.Mark) },
    { label: t('serialPort.paritySpace'), value: String(SerialPortParity.Space) },
])
const stopBitsOptions = computed(() => [
    { label: '1', value: String(SerialPortStopBits.One) },
    { label: '1.5', value: String(SerialPortStopBits.OnePointFive) },
    { label: '2', value: String(SerialPortStopBits.Two) },
])
const handshakeOptions = computed(() => [
    { label: t('serialPort.handshakeNone'), value: String(SerialPortHandshake.None) },
    { label: t('serialPort.handshakeXOnXOff'), value: String(SerialPortHandshake.XOnXOff) },
    { label: t('serialPort.handshakeRts'), value: String(SerialPortHandshake.RequestToSend) },
    { label: t('serialPort.handshakeRtsXOn'), value: String(SerialPortHandshake.RequestToSendXOnXOff) },
])

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
        // 自动展开有描述的行
        const next: Record<string, boolean> = {}
        for (const port of ports) {
            if (port.description) next[port.id] = true
        }
        expandedRows.value = next
    },
    { deep: true, immediate: true }
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
    <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-center justify-between gap-3">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('serialPort.title') }}</h1>
            <div class="flex flex-wrap gap-2">
                <Button severity="secondary" outlined size="small" @click="refreshList">
                    <RefreshCw class="size-4" />
                    {{ t('serialPort.refresh') }}
                </Button>
                <Button size="small" :disabled="scanning" @click="handleScan">
                    <ScanLine class="size-4" />
                    {{ scanning ? t('serialPort.scanning') : t('serialPort.scan') }}
                </Button>
            </div>
        </div>

        <!-- 列表卡 -->
        <div>
            <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">
                {{ t('common.loading') }}
            </div>
            <div v-else-if="store.ports.length === 0" class="py-8 text-center text-sm text-muted-foreground">
                {{ t('serialPort.noDevices') }}
            </div>
            <AppCard v-else>
                <DataTable
                    :value="store.ports"
                    :loading="store.loading"
                    v-model:expandedRows="expandedRows"
                    striped-rows
                    size="small"
                    data-key="id"
                    :row-class="(row) => (row.id === selectedId ? 'cursor-pointer !bg-muted/40' : 'cursor-pointer')"
                    @row-click="(e) => (selectedId = e.data.id)"
                >
                    <template #expansion="{ data }">
                        <div
                            v-if="data.description"
                            class="bg-muted/20 px-8 py-2.5 text-sm text-muted-foreground border-t border-border/30"
                        >
                            {{ data.description }}
                        </div>
                    </template>
                    <Column :header="t('serialPort.name')" style="min-width: 10rem">
                        <template #body="{ data }">
                            <div class="font-medium">{{ data.displayName }}</div>
                        </template>
                    </Column>
                    <Column :header="t('serialPort.portName')" style="min-width: 7rem">
                        <template #body="{ data }">
                            <span class="font-mono text-xs">{{ data.portName }}</span>
                        </template>
                    </Column>
                    <Column :header="t('serialPort.baudRate')" style="min-width: 7rem">
                        <template #body="{ data }">
                            {{ data.baudRate || t('serialPort.notSet') }}
                        </template>
                    </Column>
                    <Column :header="t('serialPort.params')" style="min-width: 12rem">
                        <template #body="{ data }">
                            <span class="text-xs text-muted-foreground">
                                {{ data.dataBits }} / {{ parityText(data.parity) }} /
                                {{ stopBitsText(data.stopBits) }} / {{ handshakeText(data.handshake) }}
                            </span>
                        </template>
                    </Column>
                    <Column :header="t('serialPort.status')" style="min-width: 7rem">
                        <template #body="{ data }">
                            <span :class="data.isOpen ? 'text-green-600' : 'text-muted-foreground'">
                                {{ data.isOpen ? t('serialPort.connected') : t('serialPort.disconnected') }}
                            </span>
                        </template>
                    </Column>
                    <Column :header="t('common.actions')" style="min-width: 7rem">
                        <template #body="{ data }">
                            <Button severity="secondary" outlined size="small" @click.stop="openEdit(data)">
                                <Pencil class="size-3.5" />
                                {{ t('common.edit') }}
                            </Button>
                        </template>
                    </Column>
                </DataTable>
            </AppCard>
        </div>

        <!-- 串口调试卡 -->
        <AppCard :beam-size="80" :beam-duration="8" :beam-delay="2">
            <div v-if="!selectedPort" class="py-10 text-center text-sm text-muted-foreground">
                {{ t('serialPort.selectPort') }}
            </div>
            <div v-else class="flex flex-col gap-4 p-4">
                <!-- 标题行：名称 + 连接状态 + 连接/断开按钮 -->
                <div class="flex flex-wrap items-center gap-3">
                    <div class="flex-1 min-w-0">
                        <span class="font-semibold">{{ selectedPort.displayName }}</span>
                        <span class="ml-2 font-mono text-xs text-muted-foreground">{{ selectedPort.portName }}</span>
                        <span
                            :class="['ml-3 text-xs', selectedPort.isOpen ? 'text-green-600' : 'text-muted-foreground']"
                        >
                            {{ selectedPort.isOpen ? t('serialPort.connected') : t('serialPort.disconnected') }}
                        </span>
                    </div>
                    <div class="flex items-center gap-2">
                        <Select
                            v-model="connectBaudRateStr"
                            :options="supportedBaudRates.map(String)"
                            size="small"
                            class="!text-xs w-[10rem]"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                        <Button
                            v-if="!selectedPort.isOpen"
                            size="small"
                            :disabled="connecting"
                            @click="handleConnect(selectedPort)"
                        >
                            <PlugZap class="size-4" />
                            {{ t('serialPort.connect') }}
                        </Button>
                        <Button
                            v-else
                            severity="secondary"
                            outlined
                            size="small"
                            class="text-orange-500 hover:bg-orange-50 dark:hover:bg-orange-900/30"
                            :disabled="connecting"
                            @click="handleDisconnect(selectedPort)"
                        >
                            <Unplug class="size-4" />
                            {{ t('serialPort.disconnect') }}
                        </Button>
                    </div>
                </div>

                <!-- 调试区：发送 + 参数 + 日志横向并排 -->
                <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
                    <!-- 左：发送区 -->
                    <div class="flex flex-col gap-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">{{ t('serialPort.debug') }}</span>
                        </div>
                        <textarea
                            v-model="rawForm.payload"
                            class="h-24 w-full resize-none rounded border bg-background px-2 py-1.5 font-mono text-xs"
                            placeholder="01 03 10 03 00 01 B0 CA"
                        />
                        <div class="grid grid-cols-2 gap-x-4 gap-y-2">
                            <label class="flex items-center gap-2 text-sm">
                                <input v-model="rawForm.isHex" type="checkbox" />
                                {{ t('serialPort.hexMode') }}
                            </label>
                            <label class="flex items-center gap-2 text-sm">
                                <input v-model="rawForm.appendNewLine" type="checkbox" :disabled="rawForm.isHex" />
                                {{ t('serialPort.appendNewLine') }}
                            </label>
                            <div class="flex flex-col gap-1">
                                <label class="text-xs text-muted-foreground">
                                    {{ t('serialPort.expectedLength') }}
                                </label>
                                <InputNumber v-model="rawForm.expectedResponseLength" :min="-1" size="small" />
                            </div>
                            <div class="flex flex-col gap-1">
                                <label class="text-xs text-muted-foreground">{{ t('serialPort.timeoutMs') }}</label>
                                <InputNumber v-model="rawForm.timeoutMs" :min="50" size="small" />
                            </div>
                        </div>
                        <Button
                            size="small"
                            class="w-full"
                            :disabled="sending || !rawForm.payload.trim()"
                            @click="handleSendRaw"
                        >
                            <Send class="size-4" />
                            {{ sending ? t('serialPort.sending') : t('serialPort.send') }}
                        </Button>
                    </div>

                    <!-- 右：日志区 -->
                    <div class="flex flex-col gap-2">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">{{ t('serialPort.debug') }}</span>
                            <Button severity="secondary" outlined size="small" @click="rawLogs = []">
                                {{ t('serialPort.clearLog') }}
                            </Button>
                        </div>
                        <div class="max-h-[280px] overflow-auto rounded border bg-muted/20">
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
                </div>
            </div>
        </AppCard>

        <!-- 编辑串口对话框 -->
        <Dialog
            v-model:visible="showEditDialog"
            modal
            draggable
            :header="t('serialPort.editTitle')"
            :style="{ width: '460px' }"
        >
            <div class="grid gap-3 p-3">
                <div class="flex flex-col gap-1">
                    <label class="text-sm">{{ t('serialPort.displayName') }}</label>
                    <InputText v-model="editForm.displayName" size="small" class="!text-xs" />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm">{{ t('serialPort.description') }}</label>
                    <InputText v-model="editForm.description" size="small" class="!text-xs" />
                </div>
                <div class="grid grid-cols-2 gap-3">
                    <div class="flex flex-col gap-1">
                        <label class="text-sm">{{ t('serialPort.baudRate') }}</label>
                        <Select
                            v-model="editBaudRateStr"
                            :options="supportedBaudRates.map(String)"
                            size="small"
                            class="!text-xs"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                    </div>
                    <div class="flex flex-col gap-1">
                        <label class="text-sm">{{ t('serialPort.dataBits') }}</label>
                        <InputNumber v-model="editForm.dataBits" :min="5" :max="8" size="small" class="!text-xs" />
                    </div>
                    <div class="flex flex-col gap-1">
                        <label class="text-sm">{{ t('serialPort.parity') }}</label>
                        <Select
                            v-model="editParityStr"
                            :options="parityOptions"
                            option-label="label"
                            option-value="value"
                            size="small"
                            class="!text-xs"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                    </div>
                    <div class="flex flex-col gap-1">
                        <label class="text-sm">{{ t('serialPort.stopBits') }}</label>
                        <Select
                            v-model="editStopBitsStr"
                            :options="stopBitsOptions"
                            option-label="label"
                            option-value="value"
                            size="small"
                            class="!text-xs"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                    </div>
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm">{{ t('serialPort.handshake') }}</label>
                    <Select
                        v-model="editHandshakeStr"
                        :options="handshakeOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="!text-xs"
                        :pt="{
                            root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                            label: {
                                class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                            },
                            dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                        }"
                    />
                </div>
                <div class="flex items-center gap-2">
                    <ToggleSwitch v-model="editForm.isEnabled" />
                    <span class="text-sm">{{ t('serialPort.enabled') }}</span>
                </div>
            </div>
            <template #footer>
                <Button severity="secondary" outlined @click="showEditDialog = false" size="small" class="!text-xs">
                    {{ t('common.cancel') }}
                </Button>
                <Button :disabled="editing" @click="handleEdit" size="small" class="!text-xs">
                    {{ editing ? t('common.saving') : t('common.save') }}
                </Button>
            </template>
        </Dialog>
    </div>
</template>
