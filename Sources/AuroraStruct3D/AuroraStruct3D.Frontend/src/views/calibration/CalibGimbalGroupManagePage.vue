<script setup lang="ts">
/**
 * 标定结构管理页面（云台组）
 * 布局参考三维数模管理（ProductModelManagePage）
 */
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import ConfirmDialog from 'primevue/confirmdialog'
import Textarea from 'primevue/textarea'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import { AppCard } from '@/components/primevue'
import { RefreshCw, Plus, Search, Pencil, Trash2 } from '@lucide/vue'
import { useAppToast } from '@/composables/useAppToast'
import { showErrorToastOnce } from '@/api/client'
import {
    getCalibGimbalGroupListAsync,
    createCalibGimbalGroupAsync,
    updateCalibGimbalGroupAsync,
    deleteCalibGimbalGroupAsync,
    type CalibGimbalGroupDto,
    type GetCalibGimbalGroupListInput,
    type CreateUpdateCalibGimbalGroupInput,
} from '@/api/calibration'

const { t } = useI18n()
const toast = useAppToast()
const confirm = useConfirm()

// ===================== 列表状态 =====================

const loading = ref(false)
const items = ref<CalibGimbalGroupDto[]>([])
const total = ref(0)
const skipCount = ref(0)
const maxResultCount = ref(20)

const filterText = ref('')
const filterIsEnabled = ref<boolean | null>(null)
const filterStartTime = ref<Date | null>(null)
const filterEndTime = ref<Date | null>(null)

const enabledOptions = computed(() => [
    { label: t('calib.enabled'), value: true },
    { label: t('calib.disabled'), value: false },
])

function formatTime(iso: string | null): string {
    if (!iso) return '—'
    return new Date(iso).toLocaleString()
}

async function loadList(): Promise<void> {
    loading.value = true
    try {
        const input: GetCalibGimbalGroupListInput = {
            filter: filterText.value || null,
            isEnabled: filterIsEnabled.value,
            startTime: filterStartTime.value ? filterStartTime.value.toISOString() : null,
            endTime: filterEndTime.value ? filterEndTime.value.toISOString() : null,
            skipCount: skipCount.value,
            maxResultCount: maxResultCount.value,
            sorting: 'CreationTime DESC',
        }
        const result = await getCalibGimbalGroupListAsync(input)
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
    filterIsEnabled.value = null
    filterStartTime.value = null
    filterEndTime.value = null
    skipCount.value = 0
    loadList()
}

const totalPages = computed(() => Math.ceil(total.value / maxResultCount.value))
const currentPage = computed(() => Math.floor(skipCount.value / maxResultCount.value) + 1)

function goToPage(page: number): void {
    skipCount.value = (page - 1) * maxResultCount.value
    loadList()
}

onMounted(() => loadList())

// ===================== 新增/编辑对话框 =====================

const showDialog = ref(false)
const isEdit = ref(false)
const editingId = ref<string | null>(null)
const saving = ref(false)

const form = ref<CreateUpdateCalibGimbalGroupInput>({
    name: '',
    description: null,
    maxSpeed: 0,
    acceleration: 0,
    accelerationTime: 0,
    decelerationTime: 0,
    isEnabled: true,
})

function openCreate(): void {
    isEdit.value = false
    editingId.value = null
    form.value = {
        name: '',
        description: null,
        maxSpeed: 0,
        acceleration: 0,
        accelerationTime: 0,
        decelerationTime: 0,
        isEnabled: true,
    }
    showDialog.value = true
}

function openEdit(item: CalibGimbalGroupDto): void {
    isEdit.value = true
    editingId.value = item.id
    form.value = {
        name: item.name,
        description: item.description,
        maxSpeed: item.maxSpeed,
        acceleration: item.acceleration,
        accelerationTime: item.accelerationTime,
        decelerationTime: item.decelerationTime,
        isEnabled: item.isEnabled,
    }
    showDialog.value = true
}

async function handleSave(): Promise<void> {
    if (!form.value.name.trim()) {
        toast.warn(t('common.requiredFields'))
        return
    }
    saving.value = true
    try {
        const payload: CreateUpdateCalibGimbalGroupInput = {
            ...form.value,
            name: form.value.name.trim(),
            description: form.value.description || null,
        }
        if (isEdit.value && editingId.value) {
            await updateCalibGimbalGroupAsync(editingId.value, payload)
            toast.success(t('calib.editSuccess'))
        } else {
            await createCalibGimbalGroupAsync(payload)
            toast.success(t('calib.createSuccess'))
        }
        showDialog.value = false
        await loadList()
    } catch (e) {
        showErrorToastOnce(e)
    } finally {
        saving.value = false
    }
}

// ===================== 删除 =====================

const deleting = ref<string | null>(null)

function openDeleteConfirm(item: CalibGimbalGroupDto): void {
    confirm.require({
        message: t('calib.confirmDelete', { name: item.name }),
        header: t('common.confirmDeleteTitle'),
        icon: 'pi pi-exclamation-triangle',
        acceptProps: { severity: 'danger', size: 'small', label: t('common.delete') },
        rejectProps: { severity: 'secondary', outlined: true, size: 'small', label: t('common.cancel') },
        accept: async () => {
            deleting.value = item.id
            try {
                await deleteCalibGimbalGroupAsync(item.id)
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
    <div class="flex h-full flex-col gap-4 p-4">
        <!-- 页面标题 -->
        <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.calibGimbalGroupManage') }}</h1>

        <!-- 主内容卡片 -->
        <AppCard :beam="true">
            <!-- 操作按钮区 -->
            <div class="flex items-center gap-2 border-b border-border/40 px-4 py-3">
                <Button severity="secondary" outlined size="small" :disabled="loading" @click="loadList">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button size="small" @click="openCreate">
                    <Plus class="mr-1 size-4" />
                    {{ t('calib.addGimbalGroup') }}
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
                    <!-- 启用状态 -->
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('calib.colEnabled') }}</span>
                    <Select
                        v-model="filterIsEnabled"
                        :options="enabledOptions"
                        option-label="label"
                        option-value="value"
                        :placeholder="t('calib.colEnabled')"
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

            <!-- 数据表格 -->
            <DataTable
                :value="items"
                :loading="loading"
                :lazy="true"
                :paginator="false"
                striped-rows
                size="small"
                data-key="id"
                :pt="{ root: { class: 'overflow-hidden' } }"
            >
                <template #empty>
                    <div class="py-6 text-center text-muted-foreground">{{ t('common.noData') }}</div>
                </template>
                <template #loading>
                    <div class="py-6 text-center text-muted-foreground">{{ t('common.loading') }}</div>
                </template>

                <!-- 名称 -->
                <Column :header="t('calib.colName')" style="min-width: 10rem; max-width: 16rem">
                    <template #body="{ data }">
                        <div class="truncate" :title="data.name">{{ data.name }}</div>
                    </template>
                </Column>

                <!-- 最大速度 -->
                <Column :header="t('calib.colMaxSpeed')" style="min-width: 6rem">
                    <template #body="{ data }">{{ data.maxSpeed }}</template>
                </Column>

                <!-- 加速度 -->
                <Column :header="t('calib.colAcceleration')" style="min-width: 6rem">
                    <template #body="{ data }">{{ data.acceleration }}</template>
                </Column>

                <!-- 加速时间 -->
                <Column :header="t('calib.colAccelerationTime')" style="min-width: 8rem">
                    <template #body="{ data }">{{ data.accelerationTime }} ms</template>
                </Column>

                <!-- 减速时间 -->
                <Column :header="t('calib.colDecelerationTime')" style="min-width: 8rem">
                    <template #body="{ data }">{{ data.decelerationTime }} ms</template>
                </Column>

                <!-- 启用状态 -->
                <Column :header="t('calib.colEnabled')" style="min-width: 6rem">
                    <template #body="{ data }">
                        <Tag
                            :severity="data.isEnabled ? 'success' : 'secondary'"
                            :value="data.isEnabled ? t('calib.enabled') : t('calib.disabled')"
                        />
                    </template>
                </Column>

                <!-- 创建时间 -->
                <Column :header="t('calib.colCreationTime')" style="min-width: 11rem">
                    <template #body="{ data }">
                        <span class="tabular-nums">{{ formatTime(data.creationTime) }}</span>
                    </template>
                </Column>

                <!-- 操作 -->
                <Column :header="t('common.actions')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <div class="flex justify-end gap-1">
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
        </AppCard>

        <!-- 全局确认对话框 -->
        <ConfirmDialog />

        <!-- 新增/编辑对话框 -->
        <Dialog
            v-model:visible="showDialog"
            modal
            :header="isEdit ? t('calib.editGimbalGroup') : t('calib.addGimbalGroup')"
            :style="{ width: '520px', maxWidth: '95vw' }"
        >
            <div class="flex flex-col gap-4 py-2">
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">
                        {{ t('calib.colName') }}
                        <span class="text-destructive">*</span>
                    </label>
                    <InputText v-model="form.name" :placeholder="t('calib.namePlaceholder')" />
                </div>
                <div class="grid grid-cols-2 gap-4">
                    <div class="flex flex-col gap-1">
                        <label class="text-sm font-medium">{{ t('calib.colMaxSpeed') }}</label>
                        <InputNumber v-model="form.maxSpeed" :min="0" :max-fraction-digits="3" class="w-full" />
                    </div>
                    <div class="flex flex-col gap-1">
                        <label class="text-sm font-medium">{{ t('calib.colAcceleration') }}</label>
                        <InputNumber v-model="form.acceleration" :min="0" :max-fraction-digits="3" class="w-full" />
                    </div>
                    <div class="flex flex-col gap-1">
                        <label class="text-sm font-medium">{{ t('calib.colAccelerationTime') }} (ms)</label>
                        <InputNumber v-model="form.accelerationTime" :min="0" :max-fraction-digits="1" class="w-full" />
                    </div>
                    <div class="flex flex-col gap-1">
                        <label class="text-sm font-medium">{{ t('calib.colDecelerationTime') }} (ms)</label>
                        <InputNumber v-model="form.decelerationTime" :min="0" :max-fraction-digits="1" class="w-full" />
                    </div>
                </div>
                <div class="flex flex-col gap-1">
                    <label class="text-sm font-medium">{{ t('common.description') }}</label>
                    <Textarea
                        v-model="form.description"
                        :placeholder="t('calib.descriptionPlaceholder')"
                        rows="3"
                        auto-resize
                    />
                </div>
                <div class="flex items-center gap-2">
                    <label class="text-sm font-medium">{{ t('calib.colEnabled') }}</label>
                    <ToggleSwitch v-model="form.isEnabled" />
                </div>
            </div>
            <template #footer>
                <Button severity="secondary" outlined size="small" @click="showDialog = false">
                    {{ t('common.cancel') }}
                </Button>
                <Button size="small" :loading="saving" @click="handleSave">{{ t('common.save') }}</Button>
            </template>
        </Dialog>
    </div>
</template>
