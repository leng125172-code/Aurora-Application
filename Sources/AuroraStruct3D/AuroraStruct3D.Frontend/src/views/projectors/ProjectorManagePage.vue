<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useProjectorStore } from '@/stores/projectors'
import {
    type CreateTcpProjectorDeviceDto,
    type CreateHidProjectorDeviceDto,
    ProjectorConnectionType,
    ProjectorConnectionStatus,
    ProjectorLedStatus,
} from '@/api/projectors'
import { toast } from 'vue-sonner'

const router = useRouter()
const store = useProjectorStore()

// ─── 新增对话框状态 ────────────────────────────────────────────────────────
const showAddDialog = ref(false)
const addMode = ref<'tcp' | 'hid'>('tcp')
const adding = ref(false)

const tcpForm = ref<CreateTcpProjectorDeviceDto>({
    name: '',
    deviceIndex: 0,
    description: '',
    isEnabled: true,
    ipAddress: '',
    tcpPort: 1234,
    connectTimeoutMs: 5000,
})

const hidForm = ref<CreateHidProjectorDeviceDto>({
    name: '',
    deviceIndex: 0,
    description: '',
    isEnabled: true,
    hidDeviceIndex: 0,
    connectTimeoutMs: 5000,
})

// ─── 操作函数 ─────────────────────────────────────────────────────────────

async function handleConnect(id: string) {
    try {
        await store.connect(id)
        toast.success('连接成功')
    } catch {
        // httpClient 已统一弹 toast，此处不再重复
    }
}

async function handleDisconnect(id: string) {
    try {
        await store.disconnect(id)
        toast.success('已断开')
    } catch {
        // 忽略
    }
}

async function handleDelete(id: string, name: string) {
    if (!confirm(`确定删除投影机 [${name}]？`)) return
    try {
        await store.remove(id)
        toast.success('已删除')
    } catch {
        // 忽略
    }
}

async function handleAdd() {
    adding.value = true
    try {
        if (addMode.value === 'tcp') {
            await store.createTcp(tcpForm.value)
        } else {
            await store.createHid(hidForm.value)
        }
        toast.success('添加成功')
        showAddDialog.value = false
        resetForms()
    } catch {
        // 忽略
    } finally {
        adding.value = false
    }
}

function resetForms() {
    tcpForm.value = {
        name: '',
        deviceIndex: 0,
        description: '',
        isEnabled: true,
        ipAddress: '',
        tcpPort: 1234,
        connectTimeoutMs: 5000,
    }
    hidForm.value = {
        name: '',
        deviceIndex: 0,
        description: '',
        isEnabled: true,
        hidDeviceIndex: 0,
        connectTimeoutMs: 5000,
    }
}

function goToControl(id: string) {
    store.selectProjector(id)
    void router.push({ name: 'ProjectorControl', params: { id } })
}

function connectionStatusClass(status: ProjectorConnectionStatus): string {
    switch (status) {
        case ProjectorConnectionStatus.Connected:
            return 'text-green-600'
        case ProjectorConnectionStatus.ConnectionFailed:
            return 'text-red-500'
        case ProjectorConnectionStatus.Disconnected:
            return 'text-muted-foreground'
        default:
            return 'text-muted-foreground'
    }
}

function ledStatusClass(status: ProjectorLedStatus): string {
    return status === ProjectorLedStatus.On ? 'text-yellow-500' : 'text-muted-foreground'
}

onMounted(() => {
    void store.fetchList()
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <div class="flex items-center justify-between">
            <h1 class="text-lg font-semibold">投影机设备管理</h1>
            <div class="flex gap-2">
                <button class="rounded border px-3 py-1.5 text-sm hover:bg-muted/50" @click="void store.fetchList()">
                    刷新
                </button>
                <button
                    class="rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90"
                    @click="showAddDialog = true"
                >
                    + 添加投影机
                </button>
            </div>
        </div>

        <!-- 设备列表 -->
        <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">加载中…</div>
        <div v-else-if="store.projectors.length === 0" class="py-8 text-center text-sm text-muted-foreground">
            暂无投影机设备
        </div>
        <div v-else class="overflow-auto rounded-lg border">
            <table class="w-full min-w-[900px] text-sm">
                <thead class="border-b bg-muted/50">
                    <tr>
                        <th class="px-3 py-2 text-left font-medium">序号</th>
                        <th class="px-3 py-2 text-left font-medium">名称</th>
                        <th class="px-3 py-2 text-left font-medium">连接方式</th>
                        <th class="px-3 py-2 text-left font-medium">地址</th>
                        <th class="px-3 py-2 text-left font-medium">连接状态</th>
                        <th class="px-3 py-2 text-left font-medium">LED</th>
                        <th class="px-3 py-2 text-left font-medium">固件版本</th>
                        <th class="px-3 py-2 text-left font-medium">操作</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="p in store.projectors" :key="p.id" class="border-b last:border-0 hover:bg-muted/30">
                        <td class="px-3 py-2">{{ p.deviceIndex }}</td>
                        <td class="px-3 py-2 font-medium">{{ p.name }}</td>
                        <td class="px-3 py-2">
                            {{ p.connectionType === ProjectorConnectionType.Tcp ? 'TCP' : 'USB HID' }}
                        </td>
                        <td class="px-3 py-2 font-mono text-xs">
                            <span v-if="p.connectionType === ProjectorConnectionType.Tcp">
                                {{ p.ipAddress }}:{{ p.tcpPort }}
                            </span>
                            <span v-else>HID[{{ p.hidDeviceIndex }}]</span>
                        </td>
                        <td :class="['px-3 py-2', connectionStatusClass(p.connectionStatus)]">
                            {{ p.connectionStatusText }}
                        </td>
                        <td :class="['px-3 py-2', ledStatusClass(p.ledStatus)]">
                            {{ p.ledStatusText }}
                        </td>
                        <td class="px-3 py-2 text-xs text-muted-foreground">
                            {{ p.firmwareVersion ?? '—' }}
                        </td>
                        <td class="px-3 py-2">
                            <div class="flex flex-wrap gap-1.5">
                                <button
                                    class="rounded border px-2 py-0.5 text-xs hover:bg-muted/50"
                                    @click="goToControl(p.id)"
                                >
                                    控制
                                </button>
                                <button
                                    v-if="p.connectionStatus !== ProjectorConnectionStatus.Connected"
                                    class="rounded border px-2 py-0.5 text-xs text-green-600 hover:bg-green-50"
                                    @click="handleConnect(p.id)"
                                >
                                    连接
                                </button>
                                <button
                                    v-else
                                    class="rounded border px-2 py-0.5 text-xs text-orange-500 hover:bg-orange-50"
                                    @click="handleDisconnect(p.id)"
                                >
                                    断开
                                </button>
                                <button
                                    class="rounded border px-2 py-0.5 text-xs text-destructive hover:bg-destructive/10"
                                    @click="handleDelete(p.id, p.name)"
                                >
                                    删除
                                </button>
                            </div>
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>

        <!-- 添加投影机对话框 -->
        <div
            v-if="showAddDialog"
            class="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
            @click.self="showAddDialog = false"
        >
            <div class="w-[480px] rounded-lg border bg-background p-6 shadow-lg">
                <h2 class="mb-4 text-base font-semibold">添加投影机</h2>

                <!-- 连接方式切换 -->
                <div class="mb-4 flex gap-2">
                    <button
                        :class="[
                            'flex-1 rounded border py-1.5 text-sm',
                            addMode === 'tcp' ? 'bg-primary text-primary-foreground' : 'hover:bg-muted/50',
                        ]"
                        @click="addMode = 'tcp'"
                    >
                        TCP 网络
                    </button>
                    <button
                        :class="[
                            'flex-1 rounded border py-1.5 text-sm',
                            addMode === 'hid' ? 'bg-primary text-primary-foreground' : 'hover:bg-muted/50',
                        ]"
                        @click="addMode = 'hid'"
                    >
                        USB HID
                    </button>
                </div>

                <!-- TCP 表单 -->
                <div v-if="addMode === 'tcp'" class="flex flex-col gap-3">
                    <label class="flex flex-col gap-1 text-sm">
                        名称
                        <input
                            v-model="tcpForm.name"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                            placeholder="如：主投影机"
                        />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        序号
                        <input
                            v-model.number="tcpForm.deviceIndex"
                            type="number"
                            min="0"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                        />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        IP 地址
                        <input
                            v-model="tcpForm.ipAddress"
                            class="rounded border bg-background px-2 py-1.5 font-mono text-sm"
                            placeholder="192.168.100.100"
                        />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        TCP 端口
                        <input
                            v-model.number="tcpForm.tcpPort"
                            type="number"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                        />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        描述
                        <input v-model="tcpForm.description" class="rounded border bg-background px-2 py-1.5 text-sm" />
                    </label>
                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="tcpForm.isEnabled" type="checkbox" />
                        启用
                    </label>
                </div>

                <!-- HID 表单 -->
                <div v-else class="flex flex-col gap-3">
                    <label class="flex flex-col gap-1 text-sm">
                        名称
                        <input
                            v-model="hidForm.name"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                            placeholder="如：主投影机"
                        />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        序号
                        <input
                            v-model.number="hidForm.deviceIndex"
                            type="number"
                            min="0"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                        />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        HID 设备索引
                        <input
                            v-model.number="hidForm.hidDeviceIndex"
                            type="number"
                            min="0"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                        />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        描述
                        <input v-model="hidForm.description" class="rounded border bg-background px-2 py-1.5 text-sm" />
                    </label>
                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="hidForm.isEnabled" type="checkbox" />
                        启用
                    </label>
                </div>

                <div class="mt-5 flex justify-end gap-2">
                    <button
                        class="rounded border px-4 py-1.5 text-sm text-muted-foreground hover:bg-muted/50"
                        @click="showAddDialog = false"
                    >
                        取消
                    </button>
                    <button
                        :disabled="adding"
                        class="rounded bg-primary px-4 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                        @click="handleAdd"
                    >
                        {{ adding ? '添加中…' : '确定添加' }}
                    </button>
                </div>
            </div>
        </div>
    </div>
</template>
