<script setup lang="ts">
/**
 * 标定工程管理页面
 * 功能：新建、删除标定工程，并跳转至向导
 */
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
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
import { Plus, Wand2, Trash2, RefreshCw } from '@lucide/vue'
import {
    CalibrationProjectStatus,
    type CalibrationProjectListDto,
    type CalibrationDeviceListDto,
    type CreateCalibrationProjectDto,
    getCalibrationProjectListAsync,
    createCalibrationProjectAsync,
    deleteCalibrationProjectAsync,
    getCalibrationDeviceListAsync,
} from '@/api/calibration'
import { useAppToast } from '@/composables/useAppToast'

const router = useRouter()
const toast = useAppToast()
const confirm = useConfirm()

// ===================== 列表状态 =====================

const loading = ref(false)
const items = ref<CalibrationProjectListDto[]>([])
const total = ref(0)

/** 状态标签映射 */
const statusLabelMap: Record<CalibrationProjectStatus, { label: string; severity: string }> = {
    [CalibrationProjectStatus.Draft]: { label: '草稿', severity: 'secondary' },
    [CalibrationProjectStatus.Configuring]: { label: '配置中', severity: 'info' },
    [CalibrationProjectStatus.Capturing]: { label: '采集中', severity: 'info' },
    [CalibrationProjectStatus.Computing]: { label: '计算中', severity: 'warn' },
    [CalibrationProjectStatus.Validating]: { label: '验证中', severity: 'warn' },
    [CalibrationProjectStatus.Completed]: { label: '已完成', severity: 'success' },
    [CalibrationProjectStatus.Failed]: { label: '失败', severity: 'danger' },
    [CalibrationProjectStatus.Archived]: { label: '已归档', severity: 'secondary' },
}

function statusInfo(status: CalibrationProjectStatus) {
    return statusLabelMap[status] ?? { label: `状态 ${status}`, severity: 'secondary' }
}

async function loadList(): Promise<void> {
    loading.value = true
    try {
        const result = await getCalibrationProjectListAsync({ maxResultCount: 100 })
        items.value = result.items
        total.value = result.totalCount
    } catch {
        toast.error('加载标定工程列表失败')
    } finally {
        loading.value = false
    }
}

onMounted(loadList)

// ===================== 新建工程 =====================

const dialogVisible = ref(false)
const saving = ref(false)
const deviceOptions = ref<CalibrationDeviceListDto[]>([])
const deviceOptionsLoading = ref(false)

const form = ref<CreateCalibrationProjectDto>({
    name: '',
    calibrationDeviceId: '',
    description: '',
})

async function openCreate(): Promise<void> {
    form.value = { name: '', calibrationDeviceId: '', description: '' }
    dialogVisible.value = true
    deviceOptionsLoading.value = true
    try {
        const result = await getCalibrationDeviceListAsync({ isActive: true, maxResultCount: 100 })
        deviceOptions.value = result.items
    } catch {
        toast.error('加载标定设备列表失败')
    } finally {
        deviceOptionsLoading.value = false
    }
}

async function createProject(): Promise<void> {
    if (!form.value.name.trim()) {
        toast.warn('工程名称不能为空')
        return
    }
    if (!form.value.calibrationDeviceId) {
        toast.warn('请选择标定设备')
        return
    }
    saving.value = true
    try {
        const project = await createCalibrationProjectAsync(form.value)
        toast.success('标定工程已创建，即将进入向导')
        dialogVisible.value = false
        await router.push({ name: 'CalibrationWizard', params: { id: project.id } })
    } catch {
        toast.error('创建工程失败')
    } finally {
        saving.value = false
    }
}

// ===================== 打开向导 =====================

function openWizard(row: CalibrationProjectListDto): void {
    router.push({ name: 'CalibrationWizard', params: { id: row.id } })
}

// ===================== 删除 =====================

function confirmDelete(row: CalibrationProjectListDto): void {
    confirm.require({
        message: `确定要删除标定工程「${row.name}」吗？此操作不可逆。`,
        header: '删除确认',
        icon: 'pi pi-exclamation-triangle',
        accept: async () => {
            try {
                await deleteCalibrationProjectAsync(row.id)
                toast.success('标定工程已删除')
                await loadList()
            } catch {
                toast.error('删除失败')
            }
        },
    })
}
</script>

<template>
    <div class="p-4 space-y-4">
        <ConfirmDialog />

        <AppCard>
            <template #header>
                <div class="flex items-center justify-between">
                    <span class="text-lg font-semibold">标定工程管理</span>
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
                            新建工程
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
                <Column field="name" header="工程名称" />
                <Column header="状态">
                    <template #body="{ data }">
                        <Tag
                            :value="statusInfo(data.status).label"
                            :severity="statusInfo(data.status).severity"
                        />
                    </template>
                </Column>
                <Column header="采集进度">
                    <template #body="{ data }">
                        {{ data.acceptedFrameCount }} / {{ data.targetCaptureCount || '未配置' }}
                    </template>
                </Column>
                <Column header="创建时间">
                    <template #body="{ data }">
                        {{ new Date(data.creationTime).toLocaleString('zh-CN') }}
                    </template>
                </Column>
                <Column header="操作" style="width: 180px">
                    <template #body="{ data }">
                        <div class="flex gap-1">
                            <Button size="small" severity="secondary" @click="openWizard(data)">
                                <Wand2 class="size-3.5 mr-1" />
                                打开向导
                            </Button>
                            <Button size="small" severity="danger" @click="confirmDelete(data)">
                                <Trash2 class="size-3.5" />
                            </Button>
                        </div>
                    </template>
                </Column>
            </DataTable>
        </AppCard>

        <!-- 新建工程 Dialog -->
        <Dialog
            v-model:visible="dialogVisible"
            header="新建标定工程"
            modal
            :style="{ width: '480px' }"
        >
            <div class="space-y-4 py-2">
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">工程名称 *</label>
                    <InputText v-model="form.name" placeholder="请输入工程名称" class="w-full" />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">标定设备 *</label>
                    <Select
                        v-model="form.calibrationDeviceId"
                        :options="deviceOptions"
                        option-label="name"
                        option-value="id"
                        placeholder="请选择标定设备"
                        class="w-full"
                        :loading="deviceOptionsLoading"
                    />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">备注</label>
                    <Textarea
                        v-model="form.description"
                        placeholder="可选：输入工程备注"
                        rows="3"
                        class="w-full"
                    />
                </div>
            </div>

            <template #footer>
                <Button severity="secondary" @click="dialogVisible = false">取消</Button>
                <Button :loading="saving" @click="createProject">创建并进入向导</Button>
            </template>
        </Dialog>
    </div>
</template>
