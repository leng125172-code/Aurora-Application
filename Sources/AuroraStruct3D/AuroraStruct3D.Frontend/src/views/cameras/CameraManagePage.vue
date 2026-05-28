<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useCameraStore } from '@/stores/cameras'
import { type UpdateCameraDeviceDto, CameraStatus } from '@/api/cameras'
import { toast } from 'vue-sonner'
import { Card, CardContent } from '@/components/ui/card'
import { BorderBeam } from '@/components/ui/border-beam'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogClose } from '@/components/ui/dialog'

const { t } = useI18n()

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
        toast.success(t('camera.openSuccess'))
    } catch {
        // httpClient 已统一弹 toast
    }
}

async function handleClose(id: string) {
    try {
        await store.close(id)
        toast.success(t('camera.closeSuccess'))
    } catch {
        // 忽略
    }
}

async function handleScan() {
    scanning.value = true
    try {
        const count = await store.scan()
        toast.success(t('camera.scanSuccess', { count }))
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
        toast.success(t('camera.updateSuccess'))
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
            return t('camera.statusReady')
        case CameraStatus.Capturing:
            return t('camera.statusCapturing')
        case CameraStatus.Error:
            return t('camera.statusError')
        case CameraStatus.Closed:
            return t('camera.statusClosed')
        default:
            return t('camera.statusUnknown')
    }
}

onMounted(() => {
    void store.fetchList()
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('camera.title') }}</h1>
            <div class="flex gap-2">
                <Button variant="outline" size="sm" @click="void store.fetchList()">{{ t('camera.refresh') }}</Button>
                <Button size="sm" :disabled="scanning" @click="handleScan">
                    <span v-if="scanning">{{ t('camera.scanning') }}</span>
                    <span v-else>{{ t('camera.scan') }}</span>
                </Button>
            </div>
        </div>

        <!-- 设备列表 -->
        <div v-if="store.loading" class="py-8 text-center text-sm text-muted-foreground">
            {{ t('common.loading') }}
        </div>
        <div v-else-if="store.cameras.length === 0" class="py-8 text-center text-sm text-muted-foreground">
            {{ t('camera.noDevices') }}
        </div>
        <Card v-else class="hidden overflow-auto md:block">
            <CardContent class="p-0">
                <table class="w-full min-w-[800px] text-sm">
                    <thead class="border-b bg-muted/50">
                        <tr>
                            <th class="px-3 py-2 text-left font-medium">{{ t('camera.index') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('camera.name') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('camera.model') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('camera.serialNumber') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('camera.status') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('camera.description') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('common.actions') }}</th>
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
                                    <Button variant="outline" size="xs" @click="goToControl(cam.id)">
                                        {{ t('camera.control') }}
                                    </Button>
                                    <Button
                                        v-if="cam.status === CameraStatus.Closed || cam.status === CameraStatus.Unknown"
                                        variant="outline"
                                        size="xs"
                                        class="text-green-600 hover:bg-green-50 dark:hover:bg-green-900/30"
                                        @click="handleOpen(cam.id)"
                                    >
                                        {{ t('camera.open') }}
                                    </Button>
                                    <Button
                                        v-else-if="cam.status !== CameraStatus.Error"
                                        variant="outline"
                                        size="xs"
                                        class="text-orange-500 hover:bg-orange-50 dark:hover:bg-orange-900/30"
                                        @click="handleClose(cam.id)"
                                    >
                                        {{ t('camera.close') }}
                                    </Button>
                                    <Button variant="outline" size="xs" @click="openEdit(cam.id)">
                                        {{ t('common.edit') }}
                                    </Button>
                                </div>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </CardContent>
        </Card>

        <!-- 移动端卡片视图（< md 时显示，避免横向滚动表格） -->
        <div v-if="!store.loading && store.cameras.length > 0" class="grid grid-cols-1 gap-3 sm:grid-cols-2 md:hidden">
            <Card v-for="cam in store.cameras" :key="cam.id" class="overflow-hidden">
                <CardContent class="space-y-2 p-3 text-sm">
                    <div class="flex items-start justify-between gap-2">
                        <div class="min-w-0">
                            <div class="truncate font-medium">{{ cam.name }}</div>
                            <div class="truncate text-xs text-muted-foreground">{{ cam.model ?? '—' }}</div>
                        </div>
                        <span :class="['shrink-0 text-xs', statusClass(cam.status)]">
                            {{ statusText(cam.status) }}
                        </span>
                    </div>
                    <div class="text-xs text-muted-foreground">
                        <div>{{ t('camera.index') }}：{{ cam.deviceIndex }}</div>
                        <div class="font-mono">SN：{{ cam.serialNumber ?? '—' }}</div>
                        <div v-if="cam.description" class="truncate">
                            {{ t('camera.description') }}：{{ cam.description }}
                        </div>
                    </div>
                    <div class="flex flex-wrap gap-1.5 border-t pt-2">
                        <Button variant="outline" size="xs" @click="goToControl(cam.id)">
                            {{ t('camera.control') }}
                        </Button>
                        <Button
                            v-if="cam.status === CameraStatus.Closed || cam.status === CameraStatus.Unknown"
                            variant="outline"
                            size="xs"
                            class="text-green-600 hover:bg-green-50 dark:hover:bg-green-900/30"
                            @click="handleOpen(cam.id)"
                        >
                            {{ t('camera.open') }}
                        </Button>
                        <Button
                            v-else-if="cam.status !== CameraStatus.Error"
                            variant="outline"
                            size="xs"
                            class="text-orange-500 hover:bg-orange-50 dark:hover:bg-orange-900/30"
                            @click="handleClose(cam.id)"
                        >
                            {{ t('camera.close') }}
                        </Button>
                        <Button variant="outline" size="xs" @click="openEdit(cam.id)">
                            {{ t('common.edit') }}
                        </Button>
                    </div>
                </CardContent>
            </Card>
        </div>

        <!-- 编辑对话框 -->
        <Dialog v-model:open="showEditDialog">
            <DialogContent class="w-[calc(100vw-2rem)] max-w-[420px]">
                <BorderBeam :size="80" :duration="8" />
                <DialogHeader>
                    <DialogTitle>{{ t('camera.editTitle') }}</DialogTitle>
                </DialogHeader>
                <div class="flex flex-col gap-3">
                    <div class="flex flex-col gap-1">
                        <Label>{{ t('camera.name') }}</Label>
                        <Input v-model="editForm.name" :placeholder="t('camera.namePlaceholder')" />
                    </div>
                    <div class="flex flex-col gap-1">
                        <Label>{{ t('camera.description') }}</Label>
                        <Input v-model="editForm.description" />
                    </div>
                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="editForm.isEnabled" type="checkbox" />
                        {{ t('camera.enabled') }}
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
