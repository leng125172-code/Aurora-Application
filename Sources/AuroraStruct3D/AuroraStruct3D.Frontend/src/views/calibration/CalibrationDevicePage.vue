<script setup lang="ts">
/**
 * 标定设备管理页面
 * 功能：新建、编辑、删除标定设备，以及启用/禁用
 */
import { ref, onMounted } from 'vue'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import ConfirmDialog from 'primevue/confirmdialog'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { AppCard } from '@/components/primevue'
import { Plus, Pencil, Trash2, RefreshCw, CheckCircle, XCircle } from '@lucide/vue'
import {
    CalibrationDeviceType,
    type CalibrationDeviceListDto,
    type CreateUpdateCalibrationDeviceDto,
    getCalibrationDeviceListAsync,
    createCalibrationDeviceAsync,
    updateCalibrationDeviceAsync,
    deleteCalibrationDeviceAsync,
    setCalibrationDeviceActiveAsync,
} from '@/api/calibration'
import { useAppToast } from '@/composables/useAppToast'

const toast = useAppToast()
const confirm = useConfirm()

// ===================== 列表状态 =====================

const loading = ref(false)
const items = ref<CalibrationDeviceListDto[]>([])
const total = ref(0)

/** 设备类型选项 */
const deviceTypeOptions = [
    { label: '双相机（无结构光）', value: CalibrationDeviceType.TwoCamZeroLight },
    { label: '三相机（无结构光）', value: CalibrationDeviceType.ThreeCamZeroLight },
    { label: '单相机 + 单投射器', value: CalibrationDeviceType.OneCamOneLight },
    { label: '双相机 + 单投射器', value: CalibrationDeviceType.TwoCamOneLight },
    { label: '三相机 + 单投射器', value: CalibrationDeviceType.ThreeCamOneLight },
]

/** 将枚举值转换为可读标签 */
function deviceTypeLabel(type: CalibrationDeviceType): string {
    return deviceTypeOptions.find((o) => o.value === type)?.label ?? `类型 ${type}`
}

async function loadList(): Promise<void> {
    loading.value = true
    try {
        const result = await getCalibrationDeviceListAsync({ maxResultCount: 100 })
        items.value = result.items
        total.value = result.totalCount
    } catch {
        toast.error('加载标定设备列表失败')
    } finally {
        loading.value = false
    }
}

onMounted(loadList)

// ===================== 新建 / 编辑 =====================

const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const saving = ref(false)

const form = ref<CreateUpdateCalibrationDeviceDto>({
    name: '',
    description: '',
    deviceType: CalibrationDeviceType.TwoCamZeroLight,
})

function openCreate(): void {
    editingId.value = null
    form.value = { name: '', description: '', deviceType: CalibrationDeviceType.TwoCamZeroLight }
    dialogVisible.value = true
}

function openEdit(row: CalibrationDeviceListDto): void {
    editingId.value = row.id
    form.value = { name: row.name, description: row.description ?? '', deviceType: row.deviceType }
    dialogVisible.value = true
}

async function saveDevice(): Promise<void> {
    if (!form.value.name.trim()) {
        toast.warn('设备名称不能为空')
        return
    }
    saving.value = true
    try {
        if (editingId.value) {
            await updateCalibrationDeviceAsync(editingId.value, form.value)
            toast.success('标定设备已更新')
        } else {
            await createCalibrationDeviceAsync(form.value)
            toast.success('标定设备已创建')
        }
        dialogVisible.value = false
        await loadList()
    } catch {
        toast.error('保存失败，请重试')
    } finally {
        saving.value = false
    }
}

// ===================== 删除 =====================

function confirmDelete(row: CalibrationDeviceListDto): void {
    confirm.require({
        message: `确定要删除标定设备「${row.name}」吗？`,
        header: '删除确认',
        icon: 'pi pi-exclamation-triangle',
        accept: async () => {
            try {
                await deleteCalibrationDeviceAsync(row.id)
                toast.success('标定设备已删除')
                await loadList()
            } catch {
                toast.error('删除失败')
            }
        },
    })
}

// ===================== 启用 / 禁用 =====================

async function toggleActive(row: CalibrationDeviceListDto): Promise<void> {
    try {
        await setCalibrationDeviceActiveAsync(row.id, !row.isActive)
        toast.success(row.isActive ? '设备已禁用' : '设备已启用')
        await loadList()
    } catch {
        toast.error('操作失败')
    }
}
</script>

<template>
    <div class="p-4 space-y-4">
        <ConfirmDialog />

        <AppCard>
            <!-- 标题栏 -->
            <template #header>
                <div class="flex items-center justify-between">
                    <span class="text-lg font-semibold">标定设备管理</span>
                    <div class="flex gap-2">
                        <Button
                            size="small"
                            severity="secondary"
                            :loading="loading"
                            @click="loadList"
                        >
                            <template #icon><RefreshCw class="size-4" /></template>
                        </Button>
                        <Button size="small" @click="openCreate">
                            <template #icon><Plus class="size-4" /></template>
                            新建设备
                        </Button>
                    </div>
                </div>
            </template>

            <DataTable
                :value="items"
                :loading="loading"
                :rows="20"
                paginator
                :totalRecords="total"
                striped-rows
                size="small"
            >
                <Column field="name" header="设备名称" />
                <Column header="设备类型">
                    <template #body="{ data }">
                        {{ deviceTypeLabel(data.deviceType) }}
                    </template>
                </Column>
                <Column header="相机 / 电机 / 投射器">
                    <template #body="{ data }">
                        {{ data.cameraBindingCount }} / {{ data.motorBindingCount }} /
                        {{ data.projectorBindingCount }}
                    </template>
                </Column>
                <Column header="状态">
                    <template #body="{ data }">
                        <Tag
                            :value="data.isActive ? '启用' : '禁用'"
                            :severity="data.isActive ? 'success' : 'secondary'"
                        />
                    </template>
                </Column>
                <Column header="创建时间">
                    <template #body="{ data }">
                        {{ new Date(data.creationTime).toLocaleString('zh-CN') }}
                    </template>
                </Column>
                <Column header="操作" style="width: 220px">
                    <template #body="{ data }">
                        <div class="flex gap-1">
                            <Button size="small" severity="secondary" @click="openEdit(data)">
                                <Pencil class="size-3.5" />
                            </Button>
                            <Button
                                size="small"
                                :severity="data.isActive ? 'warn' : 'success'"
                                @click="toggleActive(data)"
                            >
                                <CheckCircle v-if="!data.isActive" class="size-3.5" />
                                <XCircle v-else class="size-3.5" />
                                {{ data.isActive ? '禁用' : '启用' }}
                            </Button>
                            <Button size="small" severity="danger" @click="confirmDelete(data)">
                                <Trash2 class="size-3.5" />
                            </Button>
                        </div>
                    </template>
                </Column>
            </DataTable>
        </AppCard>

        <!-- 新建 / 编辑 Dialog -->
        <Dialog
            v-model:visible="dialogVisible"
            :header="editingId ? '编辑标定设备' : '新建标定设备'"
            modal
            :style="{ width: '480px' }"
        >
            <div class="space-y-4 py-2">
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">设备名称 *</label>
                    <InputText v-model="form.name" placeholder="请输入设备名称" class="w-full" />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">设备类型 *</label>
                    <Select
                        v-model="form.deviceType"
                        :options="deviceTypeOptions"
                        option-label="label"
                        option-value="value"
                        placeholder="请选择设备类型"
                        class="w-full"
                        :disabled="!!editingId"
                    />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">备注</label>
                    <Textarea
                        v-model="form.description"
                        placeholder="可选：输入设备备注说明"
                        rows="3"
                        class="w-full"
                    />
                </div>
            </div>

            <template #footer>
                <Button severity="secondary" @click="dialogVisible = false">取消</Button>
                <Button :loading="saving" @click="saveDevice">保存</Button>
            </template>
        </Dialog>
    </div>
</template>
