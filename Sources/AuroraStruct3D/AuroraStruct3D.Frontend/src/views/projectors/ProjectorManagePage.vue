<script setup lang="ts">
// 光机管理页：PrimeVue DataTable + Dialog + Button 重构版
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import ToggleSwitch from 'primevue/toggleswitch'
import { AppCard } from '@/components/primevue'
import { useProjectorStore } from '@/stores/projectors'
import {
    type UpdateProjectorDeviceDto,
    type ProjectorDeviceDto,
    ProjectorConnectionType,
    ProjectorConnectionStatus,
} from '@/api/projectors'
import { useAppToast } from '@/composables/useAppToast'

const { t } = useI18n()
const router = useRouter()
const store = useProjectorStore()
const toast = useAppToast()

// ─── 扫描状态 ─────────────────────────────────────────────────────────────
const scanning = ref(false)

async function handleScan() {
    scanning.value = true
    try {
        const count = await store.scan()
        toast.success(t('projector.scanSuccess', { count }))
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
const isEditingUsbProjector = computed(
    () => editingProjector.value?.connectionType === ProjectorConnectionType.UsbHid
)
const canUpdate = computed(() => {
    if (!editForm.value.name.trim()) return false
    if (!isEditingUsbProjector.value) return true
    const identity = editForm.value.deviceHardwareId
    return identity != null && Number.isInteger(identity) && identity >= 1 && identity <= 255
})

function handleEdit(p: ProjectorDeviceDto) {
    editingProjector.value = p
    editForm.value = {
        name: p.name,
        description: p.description ?? '',
        isEnabled: p.isEnabled,
        deviceHardwareId:
            p.connectionType === ProjectorConnectionType.UsbHid && p.deviceHardwareId >= 1
                ? p.deviceHardwareId
                : null,
    }
    showEditDialog.value = true
}

async function handleUpdate() {
    if (!editingProjector.value || !canUpdate.value) return
    updating.value = true
    const id = editingProjector.value.id
    try {
        await store.update(id, editForm.value)
        toast.success(t('projector.updateSuccess'))
        showEditDialog.value = false
        // 同步展开状态：有说明则展开，无说明则折叠
        if (editForm.value.description) {
            expandedRows.value = { ...expandedRows.value, [id]: true }
        } else {
            const next = { ...expandedRows.value }
            delete next[id]
            expandedRows.value = next
        }
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
        toast.success(t('projector.connectSuccess'))
    } catch {
        // 忽略
    }
}

async function handleDisconnect(id: string) {
    try {
        await store.disconnect(id)
        toast.success(t('projector.disconnectSuccess'))
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

function connectionStatusLabel(status: ProjectorConnectionStatus): string {
    switch (status) {
        case ProjectorConnectionStatus.Connected:
            return t('projector.statusConnected')
        case ProjectorConnectionStatus.Disconnected:
            return t('projector.statusDisconnected')
        case ProjectorConnectionStatus.ConnectionFailed:
            return t('projector.statusConnectionFailed')
        default:
            return t('projector.statusUnknown')
    }
}

// ─── 分页状态 ─────────────────────────────────────────────────────────────
const pageSize = ref(10)
const first = ref(0)

const totalCount = computed(() => store.projectors.length)
const currentPage = computed(() => Math.floor(first.value / pageSize.value) + 1)
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize.value)))
const pagedProjectors = computed(() => store.projectors.slice(first.value, first.value + pageSize.value))

function goToPage(page: number): void {
    first.value = (page - 1) * pageSize.value
}

// 切换页时重置 first（防止切换筛选/刷新后超出范围）
watch(totalCount, () => {
    if (first.value >= totalCount.value && totalCount.value > 0) {
        first.value = 0
    }
})

// 自动展开有描述信息的行（PrimeVue v4 DataTable 设有 dataKey 时，expandedRows 用 Record<id, boolean> 格式）
const expandedRows = ref<Record<string, boolean>>({})
watch(
    () => store.projectors,
    (projectors) => {
        const map: Record<string, boolean> = {}
        for (const p of projectors) {
            if (p.description) map[p.id] = true
        }
        expandedRows.value = map
    },
    { immediate: true }
)

onMounted(() => {
    void store.fetchList()
})
</script>

<template>
    <div class="flex flex-col gap-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('projector.title') }}</h1>
            <div class="flex gap-2">
                <Button severity="secondary" size="small" outlined @click="void store.fetchList()">
                    {{ t('projector.refresh') }}
                </Button>
                <Button :loading="scanning" size="small" @click="handleScan">
                    {{ scanning ? t('projector.scanning') : t('projector.scan') }}
                </Button>
            </div>
        </div>

        <!-- 设备列表 -->
        <AppCard :beam="true">
            <DataTable
                v-model:expandedRows="expandedRows"
                :value="pagedProjectors"
                :loading="store.loading"
                data-key="id"
                size="small"
                striped-rows
                :pt="{ root: { class: 'overflow-hidden' } }"
            >
                <template #expansion="{ data }">
                    <div
                        v-if="data.description"
                        class="bg-muted/20 px-8 py-2.5 text-sm text-muted-foreground border-t border-border/30"
                    >
                        {{ data.description }}
                    </div>
                </template>
                <template #empty>
                    <div class="py-6 text-center text-sm text-muted-foreground">{{ t('projector.noDevices') }}</div>
                </template>
                <template #loading>
                    <div class="py-6 text-center text-sm text-muted-foreground">{{ t('common.loading') }}</div>
                </template>

                <Column field="deviceIndex" :header="t('projector.index')" style="min-width: 4rem" />
                <Column field="name" :header="t('projector.name')" style="min-width: 9rem">
                    <template #body="{ data }">
                        <span class="font-medium">{{ data.name }}</span>
                    </template>
                </Column>
                <Column :header="t('projector.connectionType')" style="min-width: 7rem">
                    <template #body="{ data }">
                        {{ data.connectionType === ProjectorConnectionType.Tcp ? 'TCP' : 'USB HID' }}
                    </template>
                </Column>
                <Column :header="t('projector.address')" style="min-width: 10rem">
                    <template #body="{ data }">
                        <span class="font-mono text-xs">
                            <template v-if="data.connectionType === ProjectorConnectionType.Tcp">
                                {{ data.ipAddress }}:{{ data.tcpPort }}
                            </template>
                            <template v-else>HID[{{ data.hidDeviceIndex }}]</template>
                        </span>
                    </template>
                </Column>
                <Column :header="t('projector.deviceHardwareId')" style="min-width: 7rem">
                    <template #body="{ data }">
                        <span v-if="data.connectionType === ProjectorConnectionType.UsbHid" class="font-mono text-xs">
                            {{ data.deviceHardwareId >= 1 ? data.deviceHardwareId : t('projector.unassigned') }}
                        </span>
                        <span v-else class="text-muted-foreground">—</span>
                    </template>
                </Column>
                <Column :header="t('projector.connectionStatus')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <span :class="connectionStatusClass(data.connectionStatus)">
                            {{ connectionStatusLabel(data.connectionStatus) }}
                        </span>
                    </template>
                </Column>
                <Column :header="t('projector.enabled')" style="min-width: 5rem">
                    <template #body="{ data }">
                        <span :class="data.isEnabled ? 'text-green-600' : 'text-muted-foreground'">
                            {{ data.isEnabled ? '✓' : '✗' }}
                        </span>
                    </template>
                </Column>
                <Column :header="t('common.actions')" style="min-width: 16rem">
                    <template #body="{ data }">
                        <div class="flex flex-wrap gap-1.5">
                            <Button severity="secondary" size="small" outlined @click="goToControl(data.id)">
                                {{ t('projector.control') }}
                            </Button>
                            <Button
                                v-if="data.connectionStatus !== ProjectorConnectionStatus.Connected"
                                severity="success"
                                size="small"
                                outlined
                                @click="handleConnect(data.id)"
                            >
                                {{ t('projector.connect') }}
                            </Button>
                            <Button v-else severity="warn" size="small" outlined @click="handleDisconnect(data.id)">
                                {{ t('projector.disconnect') }}
                            </Button>
                            <Button severity="secondary" size="small" outlined @click="handleEdit(data)">
                                {{ t('common.edit') }}
                            </Button>
                        </div>
                    </template>
                </Column>
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

        <!-- 编辑投影仪对话框 -->
        <Dialog
            v-model:visible="showEditDialog"
            :header="t('projector.editTitle')"
            modal
            draggable
            :style="{ width: '420px' }"
        >
            <div class="flex flex-col gap-3 p-3">
                <label class="flex flex-col gap-1 text-sm">
                    <span>{{ t('projector.name') }}</span>
                    <InputText v-model="editForm.name" size="small" />
                </label>
                <label class="flex flex-col gap-1 text-sm">
                    <span>{{ t('common.description') }}</span>
                    <InputText v-model="editForm.description" size="small" />
                </label>
                <label v-if="isEditingUsbProjector" class="flex flex-col gap-1 text-sm">
                    <span>{{ t('projector.deviceHardwareId') }}</span>
                    <InputNumber
                        v-model="editForm.deviceHardwareId"
                        :min="1"
                        :max="255"
                        :use-grouping="false"
                        show-buttons
                        size="small"
                    />
                    <span class="text-xs text-muted-foreground">{{ t('projector.deviceHardwareIdHint') }}</span>
                </label>
                <label class="flex items-center gap-2 text-sm">
                    <ToggleSwitch v-model="editForm.isEnabled" />
                    <span>{{ t('projector.enabled') }}</span>
                </label>
            </div>
            <template #footer>
                <Button severity="secondary" outlined size="small" @click="showEditDialog = false">
                    {{ t('common.cancel') }}
                </Button>
                <Button :loading="updating" :disabled="!canUpdate" size="small" @click="handleUpdate">
                    {{ updating ? t('common.saving') : t('common.save') }}
                </Button>
            </template>
        </Dialog>
    </div>
</template>
