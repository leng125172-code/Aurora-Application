<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useCameraStore } from '@/stores/cameras'
import { type UpdateCameraDeviceDto, CameraStatus } from '@/api/cameras'
import { toast } from 'vue-sonner'

const router = useRouter()
const store = useCameraStore()

// ─── 扫描状态 ──────────────────────────────────────────────────────────────
const scanning = ref(false)

// ─── 编辑对话框状态 ────────────────────────────────────────────────────────
const showEditDialog = ref(false)
const editingId = ref('')
const editing = ref(false)

const editForm = ref<UpdateCameraDeviceDto>({
    name: '',
    description: '',
    isEnabled: true,
})

// ─── 操作函数 ─────────────────────────────────────────────────────────────

async function handleOpen(id: string) {
    try {
        await store.open(id)
        toast.success('相机已打开')
    } catch {
        // httpClient 已统一弹 toast
    }
}

async function handleClose(id: string) {
    try {
        await store.close(id)
        toast.success('相机已关闭')
    } catch {
        // 忽略
    }
}

async function handleScan() {
    scanning.value = true
    try {
        const count = await store.scan()
        toast.success(`扫描完成，发现 ${count} 台相机`)
    } catch {
        // 忽略
    } finally {
        scanning.value = false
    }
}

function openEdit(id: string) {
    const cam = store.cameras.find((c) => c.id === id)
    if (!cam) return
    editingId.value = id
    editForm.value = {
        name: cam.name,
        description: cam.description ?? '',
        isEnabled: cam.isEnabled,
    }
    showEditDialog.value = true
}

async function handleEdit() {
    editing.value = true
    try {
        await store.update(editingId.value, editForm.value)
        toast.success('更新成功')
        showEditDialog.value = false
    } catch {
        // 忽略
    } finally {
        editing.value = false
    }
}

function goToControl(id: string) {
    store.selectCamera(id)
    void router.push({ name: 'CameraControl', params: { id } })
}

function statusClass(status: CameraStatus): string {
    switch (status) {
        case CameraStatus.Ready:
            return 'text-green-600'
        case CameraStatus.Capturing:
            return 'text-blue-500'
        case CameraStatus.Error:
            return 'text-red-500'
        case CameraStatus.Closed:
            return 'text-muted-foreground'
        default:
            return 'text-muted-foreground'
    }
}

function statusText(status: CameraStatus): string {
    switch (status) {
        case CameraStatus.Ready:
            return '就绪'
        case CameraStatus.Capturing:
            return '采集中'
        case CameraStatus.Error:
            return '错误'
        case CameraStatus.Closed:
            return '已关闭'
        default:
            return '未知'
    }
}

onMounted(() => {
    void store.fetchList()
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <div class="flex items-center justify-between">
            <h1 class="text-lg font-semibold">相机设备管理</h1>
            <div class="flex gap-2">
                <button class="rounded border px-3 py-1.5 text-sm hover:bg-muted/50" @click="void store.fetchList()">
                    刷新
                </button>
                <button
                    class="rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-60"
                    :disabled="scanning"
                    @click="handleScan"
                >
                    <span v-if="scanning">扫描中…</span>
                    <span v-else>扫描设备</span>
                </button>
            </div>
        </div>

        <!-- 设备列表 -->
        <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">加载中…</div>
        <div v-else-if="store.cameras.length === 0" class="py-8 text-center text-sm text-muted-foreground">
            暂无相机设备，请点击「扫描设备」自动发现
        </div>
        <div v-else class="overflow-auto rounded-lg border">
            <table class="w-full min-w-[800px] text-sm">
                <thead class="border-b bg-muted/50">
                    <tr>
                        <th class="px-3 py-2 text-left font-medium">序号</th>
                        <th class="px-3 py-2 text-left font-medium">名称</th>
                        <th class="px-3 py-2 text-left font-medium">型号</th>
                        <th class="px-3 py-2 text-left font-medium">序列号</th>
                        <th class="px-3 py-2 text-left font-medium">状态</th>
                        <th class="px-3 py-2 text-left font-medium">描述</th>
                        <th class="px-3 py-2 text-left font-medium">操作</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="cam in store.cameras" :key="cam.id" class="border-b last:border-0 hover:bg-muted/30">
                        <td class="px-3 py-2">{{ cam.deviceIndex }}</td>
                        <td class="px-3 py-2 font-medium">{{ cam.name }}</td>
                        <td class="px-3 py-2 text-xs text-muted-foreground">{{ cam.model ?? '—' }}</td>
                        <td class="px-3 py-2 font-mono text-xs text-muted-foreground">
                            {{ cam.serialNumber ?? '—' }}
                        </td>
                        <td :class="['px-3 py-2', statusClass(cam.status)]">
                            {{ statusText(cam.status) }}
                        </td>
                        <td class="max-w-[180px] truncate px-3 py-2 text-xs text-muted-foreground">
                            {{ cam.description ?? '—' }}
                        </td>
                        <td class="px-3 py-2">
                            <div class="flex flex-wrap gap-1.5">
                                <button
                                    class="rounded border px-2 py-0.5 text-xs hover:bg-muted/50"
                                    @click="goToControl(cam.id)"
                                >
                                    控制
                                </button>
                                <button
                                    v-if="cam.status === CameraStatus.Closed || cam.status === CameraStatus.Unknown"
                                    class="rounded border px-2 py-0.5 text-xs text-green-600 hover:bg-green-50"
                                    @click="handleOpen(cam.id)"
                                >
                                    打开
                                </button>
                                <button
                                    v-else-if="cam.status !== CameraStatus.Error"
                                    class="rounded border px-2 py-0.5 text-xs text-orange-500 hover:bg-orange-50"
                                    @click="handleClose(cam.id)"
                                >
                                    关闭
                                </button>
                                <button
                                    class="rounded border px-2 py-0.5 text-xs hover:bg-muted/50"
                                    @click="openEdit(cam.id)"
                                >
                                    编辑
                                </button>
                            </div>
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>

        <!-- 编辑对话框 -->
        <div
            v-if="showEditDialog"
            class="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
            @click.self="showEditDialog = false"
        >
            <div class="w-[420px] rounded-lg border bg-background p-6 shadow-lg">
                <h2 class="mb-4 text-base font-semibold">编辑相机信息</h2>

                <div class="flex flex-col gap-3">
                    <label class="flex flex-col gap-1 text-sm">
                        名称
                        <input
                            v-model="editForm.name"
                            class="rounded border bg-background px-2 py-1.5 text-sm"
                            placeholder="相机显示名称"
                        />
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
                        class="rounded border px-4 py-1.5 text-sm hover:bg-muted/50"
                        @click="showEditDialog = false"
                    >
                        取消
                    </button>
                    <button
                        :disabled="editing"
                        class="rounded bg-primary px-4 py-1.5 text-sm text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                        @click="handleEdit"
                    >
                        {{ editing ? '保存中…' : '保存' }}
                    </button>
                </div>
            </div>
        </div>
    </div>
</template>
