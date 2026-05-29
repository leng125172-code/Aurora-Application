<script setup lang="ts">
// 状态切换日志页：PrimeVue DataTable + AppCard 重构版（含懒加载分页）
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import DataTable, { type DataTablePageEvent } from 'primevue/datatable'
import Column from 'primevue/column'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import Button from 'primevue/button'
import {
    type DeviceStateLogDto,
    type GetStateLogPagedInput,
    DeviceStatus,
    DeviceStatusLabels,
    StateChangeTrigger,
    StateChangeTriggerLabels,
    getStateLogPagedListAsync,
} from '@/api/device-state'
import { AppCard } from '@/components/primevue'

const { t } = useI18n()

// ─── 分页参数 ────────────────────────────────────────────────────────────────
const pageSize = ref(20)
const first = ref(0)
const totalCount = ref(0)
const items = ref<DeviceStateLogDto[]>([])
const loading = ref(false)

// ─── 筛选条件 ────────────────────────────────────────────────────────────────
const filterIsSuccessful = ref<boolean | null>(null)
const filterTrigger = ref<StateChangeTrigger | null>(null)
const filterStatus = ref<DeviceStatus | null>(null)
const filterStartTime = ref<Date | null>(null)
const filterEndTime = ref<Date | null>(null)

async function loadAsync(): Promise<void> {
    loading.value = true
    try {
        const input: GetStateLogPagedInput = {
            skipCount: first.value,
            maxResultCount: pageSize.value,
            sorting: 'occurredAt DESC',
            isSuccessful: filterIsSuccessful.value,
            trigger: filterTrigger.value,
            status: filterStatus.value,
            startTime: filterStartTime.value ? filterStartTime.value.toISOString() : null,
            endTime: filterEndTime.value ? filterEndTime.value.toISOString() : null,
        }
        const result = await getStateLogPagedListAsync(input)
        items.value = result.items
        totalCount.value = result.totalCount
    } finally {
        loading.value = false
    }
}

function onPage(event: DataTablePageEvent): void {
    first.value = event.first
    pageSize.value = event.rows
    void loadAsync()
}

function onFilterChange(): void {
    first.value = 0
    void loadAsync()
}

function onReset(): void {
    filterIsSuccessful.value = null
    filterTrigger.value = null
    filterStatus.value = null
    filterStartTime.value = null
    filterEndTime.value = null
    onFilterChange()
}

const successOptions: Array<{ value: boolean | null; label: string }> = [
    { value: null, label: t('deviceState.all') },
    { value: true, label: t('deviceState.successful') },
    { value: false, label: t('deviceState.failed') },
]

const triggerOptions: Array<{ value: StateChangeTrigger | null; label: string }> = [
    { value: null, label: t('deviceState.all') },
    ...Object.entries(StateChangeTriggerLabels).map(([k, label]) => ({
        value: Number(k) as StateChangeTrigger,
        label,
    })),
]

const statusOptions: Array<{ value: DeviceStatus | null; label: string }> = [
    { value: null, label: t('deviceState.all') },
    ...Object.entries(DeviceStatusLabels).map(([k, label]) => ({
        value: Number(k) as DeviceStatus,
        label,
    })),
]

onMounted(() => {
    void loadAsync()
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <h1 class="text-2xl font-bold tracking-tight">{{ t('deviceState.stateLog') }}</h1>

        <!-- 筛选栏 -->
        <AppCard :beam-size="80" :beam-duration="8">
            <div class="flex flex-wrap items-center gap-3 p-3">
                <!-- 切换结果 -->
                <div class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.status') }}</span>
                    <Select
                        v-model="filterIsSuccessful"
                        :options="successOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="w-32"
                        @change="onFilterChange"
                    />
                </div>

                <!-- 触发来源 -->
                <div class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.trigger') }}</span>
                    <Select
                        v-model="filterTrigger"
                        :options="triggerOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="w-36"
                        @change="onFilterChange"
                    />
                </div>

                <!-- 目标状态 -->
                <div class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.targetStatus') }}</span>
                    <Select
                        v-model="filterStatus"
                        :options="statusOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="w-36"
                        @change="onFilterChange"
                    />
                </div>

                <!-- 日期范围 -->
                <div class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.startTime') }}</span>
                    <DatePicker
                        v-model="filterStartTime"
                        show-time
                        show-icon
                        icon-display="input"
                        size="small"
                        class="w-52"
                        @date-select="onFilterChange"
                        @clear-click="onFilterChange"
                        show-button-bar
                    />
                </div>
                <div class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.endTime') }}</span>
                    <DatePicker
                        v-model="filterEndTime"
                        show-time
                        show-icon
                        icon-display="input"
                        size="small"
                        class="w-52"
                        @date-select="onFilterChange"
                        @clear-click="onFilterChange"
                        show-button-bar
                    />
                </div>

                <Button severity="secondary" size="small" outlined @click="onReset">
                    {{ t('deviceState.reset') }}
                </Button>
                <span class="ml-auto text-xs text-muted-foreground">
                    {{ t('deviceState.total', { count: totalCount }) }}
                </span>
            </div>
        </AppCard>

        <!-- 表格（PrimeVue DataTable 懒加载分页） -->
        <AppCard :beam="false">
            <DataTable
                :value="items"
                :loading="loading"
                :lazy="true"
                :paginator="true"
                :rows="pageSize"
                :first="first"
                :total-records="totalCount"
                :rows-per-page-options="[10, 20, 50, 100]"
                striped-rows
                size="small"
                data-key="id"
                @page="onPage"
            >
                <template #empty>
                    <div class="py-6 text-center text-muted-foreground">{{ t('deviceState.noData') }}</div>
                </template>
                <template #loading>
                    <div class="py-6 text-center text-muted-foreground">{{ t('deviceState.loading') }}</div>
                </template>

                <Column field="occurredAt" :header="t('deviceState.occurredAt')" style="min-width: 11rem">
                    <template #body="{ data }">
                        <span class="tabular-nums">{{ new Date(data.occurredAt).toLocaleString() }}</span>
                    </template>
                </Column>

                <Column :header="t('deviceState.stateChange')" style="min-width: 12rem">
                    <template #body="{ data }">
                        <span class="text-muted-foreground">
                            {{ data.previousStatus != null ? DeviceStatusLabels[data.previousStatus as DeviceStatus] : '—' }}
                        </span>
                        <span class="mx-1.5 text-muted-foreground">→</span>
                        <span class="font-medium">{{ DeviceStatusLabels[data.newStatus as DeviceStatus] }}</span>
                    </template>
                </Column>

                <Column :header="t('deviceState.trigger')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <span class="text-muted-foreground">
                            {{ StateChangeTriggerLabels[data.trigger as StateChangeTrigger] }}
                        </span>
                    </template>
                </Column>

                <Column field="operatorName" :header="t('deviceState.operator')" style="min-width: 8rem">
                    <template #body="{ data }">{{ data.operatorName ?? '—' }}</template>
                </Column>

                <Column :header="t('deviceState.status')" style="min-width: 6rem">
                    <template #body="{ data }">
                        <span :class="data.isSuccessful ? 'text-green-600' : 'text-destructive'">
                            {{ data.isSuccessful ? t('deviceState.successful') : t('deviceState.failed') }}
                        </span>
                    </template>
                </Column>

                <Column :header="t('deviceState.duration')" style="min-width: 6rem">
                    <template #body="{ data }">
                        <span class="tabular-nums text-muted-foreground">
                            {{ data.durationMs != null ? `${(data.durationMs / 1000).toFixed(1)}s` : '—' }}
                        </span>
                    </template>
                </Column>

                <Column :header="t('deviceState.errorMessage')" style="min-width: 14rem; max-width: 28rem">
                    <template #body="{ data }">
                        <span v-if="data.errorMessage" class="truncate text-destructive" :title="data.errorMessage">
                            {{ data.errorMessage }}
                        </span>
                        <span v-else class="text-muted-foreground">—</span>
                    </template>
                </Column>
            </DataTable>
        </AppCard>
    </div>
</template>
