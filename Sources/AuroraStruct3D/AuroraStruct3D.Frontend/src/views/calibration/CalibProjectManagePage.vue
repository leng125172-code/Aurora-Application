<script setup lang="ts">
/**
 * 设备标定管理页面
 * 布局完全参考三维数模管理（ProductModelManagePage）
 */
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import ConfirmDialog from 'primevue/confirmdialog'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import BorderBeam from '@/components/ui/border-beam/BorderBeam.vue'
import { RefreshCw, Plus, Search, Pencil, Trash2 } from '@lucide/vue'
import { useAppToast } from '@/composables/useAppToast'
import { showErrorToastOnce } from '@/api/client'
import {
    CalibDeviceType,
    CalibStatus,
    getCalibProjectListAsync,
    createCalibProjectAsync,
    updateCalibProjectAsync,
    deleteCalibProjectAsync,
    type CalibProjectDto,
    type GetCalibProjectListInput,
    type CreateCalibProjectInput,
    type UpdateCalibProjectInput,
} from '@/api/calibration'

const { t } = useI18n()
const toast = useAppToast()
const confirm = useConfirm()
const router = useRouter()

// ===================== 列表状态 =====================

const loading = ref(false)
const items = ref<CalibProjectDto[]>([])
const total = ref(0)
const skipCount = ref(0)
const maxResultCount = ref(20)

// 筛选条件
const filterText = ref('')
const filterDeviceType = ref<CalibDeviceType | null>(null)
const filterCalibStatus = ref<CalibStatus | null>(null)
const filterStartTime = ref<Date | null>(null)
const filterEndTime = ref<Date | null>(null)

/** 设备类型选项（几目几光） */
const deviceTypeOptions = computed(() => [
    { label: t('calib.type2C0L'), value: CalibDeviceType.TwoCamera0Light },
    { label: t('calib.type3C0L'), value: CalibDeviceType.ThreeCamera0Light },
    { label: t('calib.type1C1L'), value: CalibDeviceType.OneCamera1Light },
    { label: t('calib.type2C1L'), value: CalibDeviceType.TwoCamera1Light },
    { label: t('calib.type3C1L'), value: CalibDeviceType.ThreeCamera1Light },
])

/** 标定状态选项 */
const calibStatusOptions = computed(() => [
    { label: t('calib.statusInitializing'), value: CalibStatus.Initializing },
    { label: t('calib.statusMotorParam'), value: CalibStatus.MotorParamConfig },
    { label: t('calib.statusMotorConstraint'), value: CalibStatus.MotorConstraintConfig },
    { label: t('calib.statusGimbal'), value: CalibStatus.GimbalConfig },
    { label: t('calib.statusCameraParam'), value: CalibStatus.CameraParamConfig },
    { label: t('calib.statusProjectorParam'), value: CalibStatus.ProjectorParamConfig },
    { label: t('calib.statusDeviceBinding'), value: CalibStatus.DeviceBinding },
    { label: t('calib.statusCompleted'), value: CalibStatus.Completed },
])

function deviceTypeLabel(type: CalibDeviceType): string {
    return deviceTypeOptions.value.find((o) => o.value === type)?.label ?? String(type)
}

function calibStatusVariant(status: CalibStatus): 'success' | 'secondary' {
    return status === CalibStatus.Completed ? 'success' : 'secondary'
}

function calibStatusLabel(status: CalibStatus): string {
    return calibStatusOptions.value.find((o) => o.value === status)?.label ?? String(status)
}

async function loadList(): Promise<void> {
    loading.value = true
    try {
        const input: GetCalibProjectListInput = {
            filter: filterText.value || null,
            deviceType: filterDeviceType.value,
            calibStatus: filterCalibStatus.value,
            startTime: filterStartTime.value ? filterStartTime.value.toISOString() : null,
            endTime: filterEndTime.value ? filterEndTime.value.toISOString() : null,
            skipCount: skipCount.value,
            maxResultCount: maxResultCount.value,
            sorting: 'CreationTime DESC',
        }
        const result = await getCalibProjectListAsync(input)
        items.value = result.items
        total.value = result.totalCount
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        loading.value = false
    }
}

function handleSearch(): void {
    skipCount.value = 0
    loadList()
}

function handleReset(): void {
    filterText.value = ''
    filterDeviceType.value = null
    filterCalibStatus.value = null
    filterStartTime.value = null
    filterEndTime.value = null
    skipCount.value = 0
    loadList()
}

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / maxResultCount.value)))
const currentPage = computed(() => Math.floor(skipCount.value / maxResultCount.value) + 1)

function goToPage(page: number): void {
    skipCount.value = (page - 1) * maxResultCount.value
    loadList()
}

// 自动展开有描述的行
const expandedRows = ref<Record<string, boolean>>({})
watch(
    items,
    (list) => {
        const map: Record<string, boolean> = {}
        for (const item of list) {
            if (item.description) map[item.id] = true
        }
        expandedRows.value = map
    },
    { immediate: true }
)

function goToCalibWizard(item: CalibProjectDto): void {
    void router.push({ name: 'CalibWizard', params: { id: item.id } })
}

onMounted(() => loadList())

// ===================== 新增对话框 =====================

const showCreate = ref(false)
const creating = ref(false)

const createForm = ref<CreateCalibProjectInput>({
    name: '',
    description: null,
    deviceType: CalibDeviceType.TwoCamera0Light,
})

function openCreate(): void {
    createForm.value = { name: '', description: null, deviceType: CalibDeviceType.TwoCamera0Light }
    showCreate.value = true
}

async function handleCreate(): Promise<void> {
    if (!createForm.value.name.trim()) {
        toast.warn(t('common.requiredFields'))
        return
    }
    creating.value = true
    try {
        await createCalibProjectAsync({
            name: createForm.value.name.trim(),
            description: createForm.value.description || null,
            deviceType: createForm.value.deviceType,
        })
        toast.success(t('calib.createSuccess'))
        showCreate.value = false
        await loadList()
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        creating.value = false
    }
}

// ===================== 编辑对话框 =====================

const showEdit = ref(false)
const editing = ref(false)
const editingId = ref<string | null>(null)
const editDeviceTypeName = ref('')

const editForm = ref<UpdateCalibProjectInput>({
    name: '',
    description: null,
})

function openEdit(item: CalibProjectDto): void {
    editingId.value = item.id
    editDeviceTypeName.value = deviceTypeLabel(item.deviceType)
    editForm.value = { name: item.name, description: item.description }
    showEdit.value = true
}

async function handleEdit(): Promise<void> {
    if (!editingId.value || !editForm.value.name.trim()) {
        toast.warn(t('common.requiredFields'))
        return
    }
    editing.value = true
    try {
        await updateCalibProjectAsync(editingId.value, {
            name: editForm.value.name.trim(),
            description: editForm.value.description || null,
        })
        toast.success(t('calib.editSuccess'))
        showEdit.value = false
        await loadList()
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        editing.value = false
    }
}

// ===================== 删除 =====================

const deleting = ref<string | null>(null)

function openDeleteConfirm(item: CalibProjectDto): void {
    confirm.require({
        message: t('calib.confirmDelete', { name: item.name }),
        header: t('common.confirmDeleteTitle'),
        icon: 'pi pi-exclamation-triangle',
        acceptProps: { severity: 'danger', size: 'small', label: t('common.delete') },
        rejectProps: { severity: 'secondary', outlined: true, size: 'small', label: t('common.cancel') },
        accept: async () => {
            deleting.value = item.id
            try {
                await deleteCalibProjectAsync(item.id)
                toast.success(t('calib.deleteSuccess'))
                await loadList()
            } catch (e) {
                showErrorToastOnce(e)
            } finally {
                deleting.value = null
            }
        },
    })
}
</script>

<template>
    <div class="flex h-full flex-col gap-4">
        <!-- 页面标题 -->
        <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.calibProjectManage') }}</h1>

        <!-- 主内容卡片：直接用原生 div 复现 AppCard 样式，并支持自动撞满高度 -->
        <div
            class="relative flex-1 min-h-0 flex flex-col overflow-hidden rounded-xl bg-card/40 backdrop-blur border border-border shadow-sm"
            style="clip-path: inset(0 round 0.75rem)"
        >
            <BorderBeam :size="120" :duration="10" />
            <!-- 操作按鈕区 -->
            <div class="flex items-center gap-2 border-b border-border/40 px-4 py-3">
                <Button severity="secondary" outlined size="small" :disabled="loading" @click="loadList">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button size="small" @click="openCreate">
                    <Plus class="mr-1 size-4" />
                    {{ t('calib.addProject') }}
                </Button>
            </div>

            <!-- 筛选区 -->
            <div class="flex flex-col gap-3 border-b border-border/40 px-3 py-2">
                <div
                    class="grid items-center gap-x-3 gap-y-2"
                    style="grid-template-columns: repeat(auto-fill, 5.5rem 13rem)"
                >
                    <!-- 名称 -->
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('calib.colName') }}</span>
                    <InputText
                        v-model="filterText"
                        size="small"
                        class="!text-xs w-full"
                        :placeholder="t('calib.filterName')"
                        @keydown.enter="handleSearch"
                    />
                    <!-- 设备类型（几目几光） -->
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('calib.filterType') }}</span>
                    <Select
                        v-model="filterDeviceType"
                        :options="deviceTypeOptions"
                        option-label="label"
                        option-value="value"
                        :placeholder="t('calib.filterType')"
                        size="small"
                        class="!text-xs w-full"
                        :pt="{
                            root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                            label: {
                                class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                            },
                            dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                        }"
                        @change="handleSearch"
                    />
                    <!-- 标定状态 -->
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('calib.colCalibStatus') }}</span>
                    <Select
                        v-model="filterCalibStatus"
                        :options="calibStatusOptions"
                        option-label="label"
                        option-value="value"
                        :placeholder="t('calib.colCalibStatus')"
                        size="small"
                        class="!text-xs w-full"
                        :pt="{
                            root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                            label: {
                                class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                            },
                            dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                        }"
                        @change="handleSearch"
                    />
                    <!-- 开始日期 -->
                    <span class="whitespace-nowrap text-sm text-muted-foreground">
                        {{ t('calib.filterStartDate') }}
                    </span>
                    <DatePicker
                        v-model="filterStartTime"
                        show-time
                        show-icon
                        fluid
                        :showOnFocus="false"
                        size="small"
                        show-button-bar
                        @date-select="handleSearch"
                        @clear-click="handleSearch"
                    />
                    <!-- 结束日期 -->
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('calib.filterEndDate') }}</span>
                    <DatePicker
                        v-model="filterEndTime"
                        show-time
                        show-icon
                        fluid
                        :showOnFocus="false"
                        size="small"
                        show-button-bar
                        @date-select="handleSearch"
                        @clear-click="handleSearch"
                    />
                </div>
                <!-- 操作行 -->
                <div class="flex items-center gap-3">
                    <Button severity="secondary" outlined size="small" @click="handleSearch">
                        <Search class="mr-1 size-4" />
                        {{ t('common.search') }}
                    </Button>
                    <Button text severity="secondary" size="small" @click="handleReset">
                        {{ t('common.reset') }}
                    </Button>
                    <span class="ml-auto text-xs text-muted-foreground">
                        {{ t('management.totalRecords', { total }) }}
                    </span>
                </div>
            </div>

            <!-- 数据表格：外套一层 flex-1 min-h-0 容器使其占满剩余空间 -->
            <div class="flex-1 min-h-0 overflow-hidden">
                <DataTable
                    v-model:expandedRows="expandedRows"
                    :value="items"
                    :loading="loading"
                    :lazy="true"
                    :paginator="false"
                    striped-rows
                    size="small"
                    data-key="id"
                    scroll-height="flex"
                    scrollable
                    :pt="{ root: { class: 'overflow-auto' } }"
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
                        <div class="py-6 text-center text-muted-foreground">{{ t('common.noData') }}</div>
                    </template>
                    <template #loading>
                        <div class="py-6 text-center text-muted-foreground">{{ t('common.loading') }}</div>
                    </template>

                    <!-- 项目名称 -->
                    <Column :header="t('calib.colName')" style="min-width: 10rem; max-width: 18rem">
                        <template #body="{ data }">
                            <div class="truncate" :title="data.name">{{ data.name }}</div>
                        </template>
                    </Column>

                    <!-- 设备类型 -->
                    <Column :header="t('calib.colDeviceType')" style="min-width: 7rem">
                        <template #body="{ data }">
                            <Tag severity="info" :value="deviceTypeLabel(data.deviceType)" size="small" />
                        </template>
                    </Column>

                    <!-- 相机/结构光数量 -->
                    <Column :header="t('calib.colCameraCount')" style="min-width: 8rem">
                        <template #body="{ data }">
                            {{ data.cameraCount }} {{ t('calib.cameraUnit') }}
                            <span v-if="data.projectorCount > 0">
                                / {{ data.projectorCount }} {{ t('calib.projectorUnit') }}
                            </span>
                        </template>
                    </Column>

                    <!-- 标定状态 -->
                    <Column :header="t('calib.colCalibStatus')" style="min-width: 8rem">
                        <template #body="{ data }">
                            <Tag
                                :severity="calibStatusVariant(data.calibStatus)"
                                :value="calibStatusLabel(data.calibStatus)"
                                size="small"
                            />
                        </template>
                    </Column>

                    <!-- 创建时间 -->
                    <Column :header="t('calib.colCreationTime')" style="min-width: 11rem">
                        <template #body="{ data }">
                            <span class="tabular-nums">{{ new Date(data.creationTime).toLocaleString() }}</span>
                        </template>
                    </Column>

                    <!-- 操作 -->
                    <Column :header="t('common.actions')" style="min-width: 14rem">
                        <template #body="{ data }">
                            <div class="flex items-center gap-1.5">
                                <Button size="small" @click="goToCalibWizard(data)">
                                    {{ t('calib.calibrate') }}
                                </Button>
                                <Button
                                    text
                                    severity="secondary"
                                    size="small"
                                    :title="t('common.edit')"
                                    @click="openEdit(data)"
                                >
                                    <Pencil class="size-4" />
                                </Button>
                                <Button
                                    text
                                    severity="secondary"
                                    size="small"
                                    :title="t('common.delete')"
                                    :disabled="deleting === data.id"
                                    @click="openDeleteConfirm(data)"
                                >
                                    <Trash2 class="size-4 text-destructive" />
                                </Button>
                            </div>
                        </template>
                    </Column>
                </DataTable>
            </div>
            <!-- end DataTable wrapper -->

            <!-- 自定义分页控件 -->
            <div
                v-if="total > maxResultCount"
                class="flex items-center justify-between border-t border-border/50 px-4 py-3 text-sm"
            >
                <span class="text-xs text-muted-foreground">
                    {{ t('management.totalRecords', { total }) }}
                    &nbsp;·&nbsp;
                    {{
                        t('management.pageRange', {
                            from: (currentPage - 1) * maxResultCount + 1,
                            to: Math.min(currentPage * maxResultCount, total),
                        })
                    }}
                </span>
                <div class="flex items-center gap-1">
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage <= 1 || loading"
                        @click="goToPage(1)"
                    >
                        «
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage <= 1 || loading"
                        @click="goToPage(currentPage - 1)"
                    >
                        ‹
                    </Button>
                    <span class="px-3 text-muted-foreground">{{ currentPage }} / {{ totalPages }}</span>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage >= totalPages || loading"
                        @click="goToPage(currentPage + 1)"
                    >
                        ›
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage >= totalPages || loading"
                        @click="goToPage(totalPages)"
                    >
                        »
                    </Button>
                </div>
            </div>
        </div>
        <!-- end main card -->

        <!-- 全局确认对话框 -->
        <ConfirmDialog />

        <!-- 新增对话框 -->
        <Dialog
            v-model:visible="showCreate"
            modal
            :header="t('calib.addProject')"
            :style="{ width: '480px', maxWidth: '95vw' }"
        >
            <div class="flex flex-col gap-4 py-2">
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">
                        {{ t('calib.colDeviceType') }}
                        <span class="text-destructive">*</span>
                    </label>
                    <Select
                        v-model="createForm.deviceType"
                        :options="deviceTypeOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="!text-xs w-full"
                        :pt="{
                            root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                            label: {
                                class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                            },
                            dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                        }"
                    />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">
                        {{ t('calib.colName') }}
                        <span class="text-destructive">*</span>
                    </label>
                    <InputText v-model="createForm.name" size="small" :placeholder="t('calib.namePlaceholder')" />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">{{ t('common.description') }}</label>
                    <Textarea
                        size="small"
                        v-model="createForm.description"
                        :placeholder="t('calib.descriptionPlaceholder')"
                        rows="3"
                        auto-resize
                    />
                </div>
            </div>
            <template #footer>
                <Button severity="secondary" outlined size="small" @click="showCreate = false">
                    {{ t('common.cancel') }}
                </Button>
                <Button size="small" :loading="creating" @click="handleCreate">
                    {{ t('common.confirm') }}
                </Button>
            </template>
        </Dialog>

        <!-- 编辑对话框 -->
        <Dialog
            v-model:visible="showEdit"
            modal
            :header="t('calib.editProject')"
            :style="{ width: '480px', maxWidth: '95vw' }"
        >
            <div class="flex flex-col gap-4 py-2">
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">{{ t('calib.colDeviceType') }}</label>
                    <InputText :value="editDeviceTypeName" size="small" disabled class="opacity-60" />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">
                        {{ t('calib.colName') }}
                        <span class="text-destructive">*</span>
                    </label>
                    <InputText v-model="editForm.name" size="small" :placeholder="t('calib.namePlaceholder')" />
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">{{ t('common.description') }}</label>
                    <Textarea
                        v-model="editForm.description"
                        size="small"
                        :placeholder="t('calib.descriptionPlaceholder')"
                        rows="3"
                        auto-resize
                    />
                </div>
            </div>
            <template #footer>
                <Button severity="secondary" outlined size="small" @click="showEdit = false">
                    {{ t('common.cancel') }}
                </Button>
                <Button size="small" :loading="editing" @click="handleEdit">
                    {{ t('common.save') }}
                </Button>
            </template>
        </Dialog>
    </div>
</template>
