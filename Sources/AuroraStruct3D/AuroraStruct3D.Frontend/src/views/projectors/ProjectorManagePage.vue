<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useProjectorStore } from '@/stores/projectors'
import {
    type UpdateProjectorDeviceDto,
    type ProjectorDeviceDto,
    ProjectorConnectionType,
    ProjectorConnectionStatus,
    ProjectorLedStatus,
} from '@/api/projectors'
import { toast } from 'vue-sonner'

const router = useRouter()
const store = useProjectorStore()

// ─── 扫描状态 ─────────────────────────────────────────────────────────────
const scanning = ref(false)

async function handleScan() {
    scanning.value = true
    try {
        const count = await store.scan()
        toast.success(`扫描完成，检测到 ${count} 台投影机`)
    } catch {
        // httpClient 已统一弹 toast
    } finally {
        scanning.value = false
    }
}

// ─── 编辑对话框状态 ────────────────────────────────────────────────────────
const showEditDialog = ref(false)
const editingProjector = ref<ProjectorDeviceDto | null>(null)
const editForm = ref<UpdateProjectorDeviceDto>({ name: '', description: '', isEnabled: true })
const updating = ref(false)

function handleEdit(p: ProjectorDeviceDto) {
    editingProjector.value = p
    editForm.value = { name: p.name, description: p.description ?? '', isEnabled: p.isEnabled }
    showEditDialog.value = true
}

async function handleUpdate() {
    if (!editingProjector.value) return
    updating.value = true
    try {
        await store.update(editingProjector.value.id, editForm.value)
        toast.success('更新成功')
        showEditDialog.value = false
    } catch {
        // 忽略
    } finally {
        updating.value = false
    }
}

// ─── 连接操作 ─────────────────────────────────────────────────────────────

async function handleConnect(id: string) {
    try {
        await store.connect(id)
        toast.success('连接成功')
    } catch {
        // 忽略
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

// ─── 辅助函数 ─────────────────────────────────────────────────────────────

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
                    :disabled="scanning"
                    class="rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                    @click="handleScan"
                >
                    {{ scanning ? '扫描中…' : '扫描投影机' }}
                </button>
            </div>
        </div>

        <!-- 设备列表 -->
        <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">加载中…</div>
        <div v-else-if="store.projectors.length === 0" class="py-8 text-center text-sm text-muted-foreground">
            暂无投影机设备，请点击"扫描投影机"检测已连接设备
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
                                    class="rounded border px-2 py-0.5 text-xs hover:bg-muted/50"
                                    @click="handleEdit(p)"
                                >
                                    编辑
                                </button>
                            </div>
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>

        <!-- 编辑投影机对话框 -->
        <div
            v-if="showEditDialog"
            class="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
            @click.self="showEditDialog = false"
        >
            <div class="w-[420px] rounded-lg border bg-background p-6 shadow-lg">
                <h2 class="mb-4 text-base font-semibold">编辑投影机</h2>
                <div class="flex flex-col gap-3">
                    <label class="flex flex-col gap-1 text-sm">
                        名称
                        <input v-model="editForm.name" class="rounded border bg-background px-2 py-1.5 text-sm" />
                    </label>
                    <label class="flex flex-col gap-1 text-sm">
                        描述
                        <input
                            v-model="editForm.description"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                        />
                    </label>
                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="editForm.isEnabled" type="checkbox" />
                        启用
                    </label>
                </div>
                <div class="mt-5 flex justify-end gap-2">
                    <button
                        class="rounded border px-4 py-1.5 text-sm text-muted-foreground hover:bg-muted/50"
                        @click="showEditDialog = false"
                    >
                        取消
                    </button>
                    <button
                        :disabled="updating"
                        class="rounded bg-primary px-4 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                        @click="handleUpdate"
                    >
                        {{ updating ? '保存中…' : '保存' }}
                    </button>
                </div>
            </div>
        </div>
    </div>
</template>
