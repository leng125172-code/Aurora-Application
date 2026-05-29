<script setup lang="ts">
// 光机管理页：PrimeVue DataTable + Dialog + Button 重构版
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import ToggleSwitch from 'primevue/toggleswitch'
import { useProjectorStore } from '@/stores/projectors'
import {
    type UpdateProjectorDeviceDto,
    type ProjectorDeviceDto,
    ProjectorConnectionType,
    ProjectorConnectionStatus,
    ProjectorLedStatus,
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
        toast.success(t('projector.updateSuccess'))
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
            <h1 class="text-lg font-semibold">{{ t('projector.title') }}</h1>
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
        <DataTable
            :value="store.projectors"
            :loading="store.loading"
            data-key="id"
            size="small"
            striped-rows
            class="rounded-lg border"
        >
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
            <Column :header="t('projector.connectionStatus')" style="min-width: 8rem">
                <template #body="{ data }">
                    <span :class="connectionStatusClass(data.connectionStatus)">{{ data.connectionStatusText }}</span>
                </template>
            </Column>
            <Column :header="t('projector.led')" style="min-width: 6rem">
                <template #body="{ data }">
                    <span :class="ledStatusClass(data.ledStatus)">{{ data.ledStatusText }}</span>
                </template>
            </Column>
            <Column :header="t('projector.firmware')" style="min-width: 7rem">
                <template #body="{ data }">
                    <span class="text-xs text-muted-foreground">{{ data.firmwareVersion ?? '—' }}</span>
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

        <!-- 编辑投影机对话框 -->
        <Dialog
            v-model:visible="showEditDialog"
            :header="t('projector.editTitle')"
            modal
            :style="{ width: '420px' }"
            :draggable="false"
        >
            <div class="flex flex-col gap-3">
                <label class="flex flex-col gap-1 text-sm">
                    <span>{{ t('projector.name') }}</span>
                    <InputText v-model="editForm.name" size="small" />
                </label>
                <label class="flex flex-col gap-1 text-sm">
                    <span>{{ t('common.description') }}</span>
                    <InputText v-model="editForm.description" size="small" />
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
                <Button :loading="updating" size="small" @click="handleUpdate">
                    {{ updating ? t('common.saving') : t('common.save') }}
                </Button>
            </template>
        </Dialog>
    </div>
</template>
