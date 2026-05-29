<script setup lang="ts">
// 故障历史页：PrimeVue DataTable + AppCard 重构版（含懒加载分页）
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import DataTable, { type DataTablePageEvent } from 'primevue/datatable'
import Column from 'primevue/column'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import Button from 'primevue/button'
import {
    type DeviceFaultDto,
    type GetFaultPagedInput,
    DeviceFaultLevel,
    DeviceFaultLevelLabels,
    getFaultPagedListAsync,
} from '@/api/device-state'
import { extractLogTag } from '@/utils/log-tag'
import { AppCard } from '@/components/primevue'

const { t } = useI18n()

// ─── 分页参数 ────────────────────────────────────────────────────────────────
const pageSize = ref(20)
const first = ref(0) // 当前页起始索引（DataTable 需要）
const totalCount = ref(0)
const items = ref<DeviceFaultDto[]>([])
const loading = ref(false)

// ─── 筛选条件 ────────────────────────────────────────────────────────────────
const filterFaultLevel = ref<DeviceFaultLevel | null>(null)
const filterIsResolved = ref<boolean | null>(null)
const filterStartTime = ref<Date | null>(null)
const filterEndTime = ref<Date | null>(null)

async function loadAsync(): Promise<void> {
    loading.value = true
    try {
        const input: GetFaultPagedInput = {
            skipCount: first.value,
            maxResultCount: pageSize.value,
            sorting: 'occurredAt DESC',
            faultLevel: filterFaultLevel.value,
            isResolved: filterIsResolved.value,
            startTime: filterStartTime.value ? filterStartTime.value.toISOString() : null,
            endTime: filterEndTime.value ? filterEndTime.value.toISOString() : null,
        }
        const result = await getFaultPagedListAsync(input)
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
    filterFaultLevel.value = null
    filterIsResolved.value = null
    filterStartTime.value = null
    filterEndTime.value = null
    onFilterChange()
}

const faultLevelOptions: Array<{ value: DeviceFaultLevel | null; label: string }> = [
    { value: null, label: t('deviceState.all') },
    { value: DeviceFaultLevel.Warning, label: DeviceFaultLevelLabels[DeviceFaultLevel.Warning] },
    { value: DeviceFaultLevel.GeneralFault, label: DeviceFaultLevelLabels[DeviceFaultLevel.GeneralFault] },
    { value: DeviceFaultLevel.SevereFault, label: DeviceFaultLevelLabels[DeviceFaultLevel.SevereFault] },
    { value: DeviceFaultLevel.SafetyFault, label: DeviceFaultLevelLabels[DeviceFaultLevel.SafetyFault] },
]

const resolvedOptions: Array<{ value: boolean | null; label: string }> = [
    { value: null, label: t('deviceState.all') },
    { value: false, label: t('deviceState.unresolved') },
    { value: true, label: t('deviceState.resolved') },
]

onMounted(() => {
    void loadAsync()
})
</script>

<template>
    <div class="flex flex-col gap-4 p-4">
        <h1 class="text-2xl font-bold tracking-tight">{{ t('deviceState.faultHistory') }}</h1>

        <!-- 筛选栏 -->
        <AppCard :beam-size="80" :beam-duration="8">
            <div class="flex flex-wrap items-center gap-3 p-3">
                <!-- 故障等级 -->
                <div class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.level') }}</span>
                    <Select
                        v-model="filterFaultLevel"
                        :options="faultLevelOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="w-36"
                        @change="onFilterChange"
                    />
                </div>

                <!-- 是否解决 -->
                <div class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.status') }}</span>
                    <Select
                        v-model="filterIsResolved"
                        :options="resolvedOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="w-32"
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

                <Column :header="t('deviceState.level')" style="min-width: 6rem">
                    <template #body="{ data }">
                        <span
                            :class="{
                                'text-yellow-600': data.faultLevel === DeviceFaultLevel.Warning,
                                'text-orange-500': data.faultLevel === DeviceFaultLevel.GeneralFault,
                                'text-destructive':
                                    data.faultLevel === DeviceFaultLevel.SevereFault ||
                                    data.faultLevel === DeviceFaultLevel.SafetyFault,
                            }"
                        >
                            {{ DeviceFaultLevelLabels[data.faultLevel as DeviceFaultLevel] }}
                        </span>
                    </template>
                </Column>

                <Column field="faultCode" :header="t('deviceState.faultCode')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <span class="font-mono text-xs">{{ data.faultCode ?? '—' }}</span>
                    </template>
                </Column>

                <Column :header="t('deviceState.message')" style="min-width: 16rem; max-width: 28rem">
                    <template #body="{ data }">
                        <template v-if="data.faultMessage">
                            <template v-if="extractLogTag(data.faultMessage)">
                                <span
                                    class="mr-1.5 inline-block rounded px-1 py-0.5 font-mono text-xs font-semibold text-white"
                                    :style="{ backgroundColor: extractLogTag(data.faultMessage)!.color }"
                                >
                                    {{ extractLogTag(data.faultMessage)!.tag }}
                                </span>
                                <span :title="data.faultMessage" class="truncate">
                                    {{ extractLogTag(data.faultMessage)!.rest }}
                                </span>
                            </template>
                            <span v-else :title="data.faultMessage" class="truncate">{{ data.faultMessage }}</span>
                        </template>
                        <span v-else class="text-muted-foreground">—</span>
                    </template>
                </Column>

                <Column :header="t('deviceState.status')" style="min-width: 6rem">
                    <template #body="{ data }">
                        <span :class="data.isResolved ? 'text-green-600' : 'text-muted-foreground'">
                            {{ data.isResolved ? t('deviceState.resolved') : t('deviceState.unresolved') }}
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
            </DataTable>
        </AppCard>
    </div>
</template>
