<script setup lang="ts">
// 相机管理页：PrimeVue DataTable + Dialog 重构版
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import ToggleSwitch from 'primevue/toggleswitch'
import { useCameraStore } from '@/stores/cameras'
import { type UpdateCameraDeviceDto, CameraStatus } from '@/api/cameras'
import { useAppToast } from '@/composables/useAppToast'
import { AppCard } from '@/components/primevue'

const { t } = useI18n()

const router = useRouter()
const store = useCameraStore()
const toast = useAppToast()

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
                <Button severity="secondary" outlined size="small" @click="void store.fetchList()">
                    {{ t('camera.refresh') }}
                </Button>
                <Button :loading="scanning" size="small" @click="handleScan">
                    {{ scanning ? t('camera.scanning') : t('camera.scan') }}
                </Button>
            </div>
        </div>

        <!-- 桌面端表格 -->
        <DataTable
            :value="store.cameras"
            :loading="store.loading"
            data-key="id"
            size="small"
            striped-rows
            class="hidden md:block rounded-lg border"
        >
            <template #empty>
                <div class="py-6 text-center text-sm text-muted-foreground">{{ t('camera.noDevices') }}</div>
            </template>
            <template #loading>
                <div class="py-6 text-center text-sm text-muted-foreground">{{ t('common.loading') }}</div>
            </template>

            <Column field="deviceIndex" :header="t('camera.index')" style="min-width: 4rem" />
            <Column field="name" :header="t('camera.name')" style="min-width: 9rem">
                <template #body="{ data }">
                    <span class="font-medium">{{ data.name }}</span>
                </template>
            </Column>
            <Column :header="t('camera.model')" style="min-width: 8rem">
                <template #body="{ data }">
                    <span class="text-xs text-muted-foreground">{{ data.model ?? '—' }}</span>
                </template>
            </Column>
            <Column :header="t('camera.serialNumber')" style="min-width: 10rem">
                <template #body="{ data }">
                    <span class="font-mono text-xs text-muted-foreground">{{ data.serialNumber ?? '—' }}</span>
                </template>
            </Column>
            <Column :header="t('camera.status')" style="min-width: 6rem">
                <template #body="{ data }">
                    <span :class="statusClass(data.status)">{{ statusText(data.status) }}</span>
                </template>
            </Column>
            <Column :header="t('camera.description')" style="min-width: 10rem; max-width: 14rem">
                <template #body="{ data }">
                    <span class="truncate text-xs text-muted-foreground">{{ data.description ?? '—' }}</span>
                </template>
            </Column>
            <Column :header="t('common.actions')" style="min-width: 16rem">
                <template #body="{ data }">
                    <div class="flex flex-wrap gap-1.5">
                        <Button severity="secondary" size="small" outlined @click="goToControl(data.id)">
                            {{ t('camera.control') }}
                        </Button>
                        <Button
                            v-if="data.status === CameraStatus.Closed || data.status === CameraStatus.Unknown"
                            severity="success"
                            size="small"
                            outlined
                            @click="handleOpen(data.id)"
                        >
                            {{ t('camera.open') }}
                        </Button>
                        <Button
                            v-else-if="data.status !== CameraStatus.Error"
                            severity="warn"
                            size="small"
                            outlined
                            @click="handleClose(data.id)"
                        >
                            {{ t('camera.close') }}
                        </Button>
                        <Button severity="secondary" size="small" outlined @click="openEdit(data.id)">
                            {{ t('common.edit') }}
                        </Button>
                    </div>
                </template>
            </Column>
        </DataTable>

        <!-- 移动端卡片视图（< md 时显示） -->
        <div v-if="!store.loading && store.cameras.length > 0" class="grid grid-cols-1 gap-3 sm:grid-cols-2 md:hidden">
            <AppCard v-for="cam in store.cameras" :key="cam.id" :beam="false">
                <div class="space-y-2 p-3 text-sm">
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
                        <Button severity="secondary" size="small" outlined @click="goToControl(cam.id)">
                            {{ t('camera.control') }}
                        </Button>
                        <Button
                            v-if="cam.status === CameraStatus.Closed || cam.status === CameraStatus.Unknown"
                            severity="success"
                            size="small"
                            outlined
                            @click="handleOpen(cam.id)"
                        >
                            {{ t('camera.open') }}
                        </Button>
                        <Button
                            v-else-if="cam.status !== CameraStatus.Error"
                            severity="warn"
                            size="small"
                            outlined
                            @click="handleClose(cam.id)"
                        >
                            {{ t('camera.close') }}
                        </Button>
                        <Button severity="secondary" size="small" outlined @click="openEdit(cam.id)">
                            {{ t('common.edit') }}
                        </Button>
                    </div>
                </div>
            </AppCard>
        </div>

        <!-- 编辑对话框 -->
        <Dialog
            v-model:visible="showEditDialog"
            :header="t('camera.editTitle')"
            modal
            :style="{ width: '420px' }"
            :draggable="false"
        >
            <div class="flex flex-col gap-3">
                <label class="flex flex-col gap-1 text-sm">
                    <span>{{ t('camera.name') }}</span>
                    <InputText v-model="editForm.name" size="small" :placeholder="t('camera.namePlaceholder')" />
                </label>
                <label class="flex flex-col gap-1 text-sm">
                    <span>{{ t('camera.description') }}</span>
                    <InputText v-model="editForm.description" size="small" />
                </label>
                <label class="flex items-center gap-2 text-sm">
                    <ToggleSwitch v-model="editForm.isEnabled" />
                    <span>{{ t('camera.enabled') }}</span>
                </label>
            </div>
            <template #footer>
                <Button severity="secondary" outlined size="small" @click="showEditDialog = false">
                    {{ t('common.cancel') }}
                </Button>
                <Button :loading="editing" size="small" @click="handleEdit">
                    {{ editing ? t('common.saving') : t('common.save') }}
                </Button>
            </template>
        </Dialog>
    </div>
</template>
