<script setup lang="ts">
// 相机管理页：PrimeVue DataTable + Dialog 重构版
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import ToggleSwitch from 'primevue/toggleswitch'
import { useCameraStore } from '@/stores/cameras'
import { type UpdateCameraDeviceDto, CameraCapability, CameraStatus } from '@/api/cameras'
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
        const result = await store.scan()
        const failedDrivers = result.drivers.filter((driver) => driver.error)
        if (failedDrivers.length > 0 || result.conflicts > 0) {
            toast.warning(
                `扫描到 ${result.totalDiscovered} 台，冲突 ${result.conflicts} 台，驱动失败 ${failedDrivers.length} 个`
            )
        } else {
            toast.success(t('camera.scanSuccess', { count: result.totalDiscovered }))
        }
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
        // 同步展开状态：有说明则展开，无说明则折叠
        if (editForm.value.description) {
            expandedRows.value = { ...expandedRows.value, [editingId.value]: true }
        } else {
            const next = { ...expandedRows.value }
            delete next[editingId.value]
            expandedRows.value = next
        }
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

const capabilityNames: Array<[CameraCapability, string]> = [
    [CameraCapability.Preview, '预览'],
    [CameraCapability.Snapshot, '快照'],
    [CameraCapability.SoftwareTrigger, '软触发'],
    [CameraCapability.ExternalTrigger, '外触发'],
    [CameraCapability.ParameterNodes, '参数节点'],
    [CameraCapability.Temperature, '温度'],
    [CameraCapability.RtpStream, 'RTP'],
]

function cameraCapabilities(capabilities: CameraCapability): string[] {
    return capabilityNames.filter(([flag]) => (capabilities & flag) !== 0).map(([, name]) => name)
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

// ─── 分页状态 ─────────────────────────────────────────────────────────────
const pageSize = ref(10)
const first = ref(0)

const totalCount = computed(() => store.cameras.length)
const currentPage = computed(() => Math.floor(first.value / pageSize.value) + 1)
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize.value)))
const pagedCameras = computed(() => store.cameras.slice(first.value, first.value + pageSize.value))

function goToPage(page: number): void {
    first.value = (page - 1) * pageSize.value
}
// ─── 展开行状态（有描述的行自动展开） ─────────────────────────────
const expandedRows = ref<Record<string, boolean>>({})

watch(
    () => store.cameras,
    (cameras) => {
        const next: Record<string, boolean> = {}
        for (const cam of cameras) {
            if (cam.description) next[cam.id] = true
        }
        expandedRows.value = next
    },
    { immediate: true }
)
watch(totalCount, () => {
    if (first.value >= totalCount.value && totalCount.value > 0) {
        first.value = 0
    }
})

onMounted(() => {
    void store.fetchList()
})
</script>

<template>
    <div class="flex flex-col gap-4">
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
        <AppCard :beam="true" class="hidden md:block">
            <DataTable
                :value="pagedCameras"
                :loading="store.loading"
                data-key="id"
                v-model:expandedRows="expandedRows"
                size="small"
                striped-rows
                :pt="{ root: { class: 'overflow-hidden' } }"
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
                <Column header="驱动 / 设备标识" style="min-width: 11rem">
                    <template #body="{ data }">
                        <div class="space-y-0.5 text-xs">
                            <div>{{ data.driverId }}</div>
                            <div class="text-muted-foreground">{{ data.hardwareId ?? '待重新绑定' }}</div>
                            <div class="text-muted-foreground">{{ data.connectionSummary ?? '—' }}</div>
                        </div>
                    </template>
                </Column>
                <Column header="能力" style="min-width: 12rem">
                    <template #body="{ data }">
                        <div class="flex flex-wrap gap-1">
                            <span
                                v-for="capability in cameraCapabilities(data.capabilities)"
                                :key="capability"
                                class="rounded bg-muted px-1.5 py-0.5 text-[11px]"
                            >
                                {{ capability }}
                            </span>
                            <span
                                v-if="cameraCapabilities(data.capabilities).length === 0"
                                class="text-xs text-amber-600"
                            >
                                待扫描
                            </span>
                        </div>
                    </template>
                </Column>
                <Column :header="t('camera.enabled')" style="min-width: 5rem">
                    <template #body="{ data }">
                        <span :class="data.isEnabled ? 'text-green-600' : 'text-muted-foreground'">
                            {{ data.isEnabled ? '✓' : '✗' }}
                        </span>
                    </template>
                </Column>
                <Column :header="t('camera.status')" style="min-width: 6rem">
                    <template #body="{ data }">
                        <span :class="statusClass(data.status)">{{ statusText(data.status) }}</span>
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
                                :disabled="!data.isOnline || !data.hardwareId"
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
                <template #expansion="{ data }">
                    <div
                        v-if="data.description"
                        class="bg-muted/20 px-8 py-2.5 text-sm text-muted-foreground border-t border-border/30"
                    >
                        {{ data.description }}
                    </div>
                </template>
            </DataTable>

            <!-- 自定义分页控件 -->
            <div
                v-if="totalCount > pageSize"
                class="flex items-center justify-between px-4 py-3 border-t border-border/50 text-sm"
            >
                <span class="text-muted-foreground text-xs">
                    {{ t('management.totalRecords', { total: totalCount }) }}
                    &nbsp;·&nbsp;
                    {{
                        t('management.pageRange', {
                            from: (currentPage - 1) * pageSize + 1,
                            to: Math.min(currentPage * pageSize, totalCount),
                        })
                    }}
                </span>
                <div class="flex items-center gap-1">
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage <= 1 || store.loading"
                        @click="goToPage(1)"
                    >
                        «
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage <= 1 || store.loading"
                        @click="goToPage(currentPage - 1)"
                    >
                        ‹
                    </Button>
                    <span class="px-3 text-muted-foreground">{{ currentPage }} / {{ totalPages }}</span>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage >= totalPages || store.loading"
                        @click="goToPage(currentPage + 1)"
                    >
                        ›
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage >= totalPages || store.loading"
                        @click="goToPage(totalPages)"
                    >
                        »
                    </Button>
                </div>
            </div>
        </AppCard>

        <!-- 移动端卡片视图（< md 时显示） -->
        <div v-if="!store.loading && pagedCameras.length > 0" class="grid grid-cols-1 gap-3 sm:grid-cols-2 md:hidden">
            <AppCard v-for="cam in pagedCameras" :key="cam.id" :beam="false">
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
                        <div>驱动：{{ cam.driverId }}</div>
                        <div>设备标识：{{ cam.hardwareId ?? '待重新绑定' }}</div>
                        <div>连接：{{ cam.connectionSummary ?? '—' }}</div>
                        <div>能力：{{ cameraCapabilities(cam.capabilities).join('、') || '待扫描' }}</div>
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
                            :disabled="!cam.isOnline || !cam.hardwareId"
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
            draggable
            :style="{ width: '420px' }"
        >
            <div class="flex flex-col gap-3 p-3">
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
