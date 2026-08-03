<script setup lang="ts">
// 故障历史页：PrimeVue DataTable + AppCard 重构版（含懒加载分页）
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import Button from 'primevue/button'
import { RefreshCw } from '@lucide/vue'
import {
    type DeviceFaultDto,
    type GetFaultPagedInput,
    DeviceFaultLevel,
    DeviceFaultSource,
    getFaultPagedListAsync,
} from '@/api/device-state'
import { extractLogTag } from '@/utils/log-tag'
import { AppCard } from '@/components/primevue'

const { t } = useI18n()

// ─── 分页参数 ────────────────────────────────────────────────────────────────
const pageSize = ref(20)
const first = ref(0) // 当前页起始索引
const totalCount = ref(0)
const items = ref<DeviceFaultDto[]>([])
const loading = ref(false)

const currentPage = computed(() => Math.floor(first.value / pageSize.value) + 1)
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize.value)))

function goToPage(page: number): void {
    first.value = (page - 1) * pageSize.value
    void loadAsync()
}

// ─── 筛选条件 ────────────────────────────────────────────────────────────────
const filterFaultLevel = ref<DeviceFaultLevel | null>(null)
const filterSource = ref<DeviceFaultSource | null>(null)
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
            source: filterSource.value,
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

function onFilterChange(): void {
    first.value = 0
    void loadAsync()
}

function onReset(): void {
    filterFaultLevel.value = null
    filterSource.value = null
    filterIsResolved.value = null
    filterStartTime.value = null
    filterEndTime.value = null
    onFilterChange()
}

const faultLevelOptions = computed<Array<{ value: DeviceFaultLevel | null; label: string }>>(() => [
    { value: null, label: t('deviceState.all') },
    { value: DeviceFaultLevel.Warning, label: t('deviceState.faultLevel.warning') },
    { value: DeviceFaultLevel.GeneralFault, label: t('deviceState.faultLevel.generalFault') },
    { value: DeviceFaultLevel.SevereFault, label: t('deviceState.faultLevel.severeFault') },
    { value: DeviceFaultLevel.SafetyFault, label: t('deviceState.faultLevel.safetyFault') },
])

const faultSourceOptions = [
    { value: null, label: '全部来源' },
    { value: DeviceFaultSource.System, label: '系统' },
    { value: DeviceFaultSource.Camera, label: '相机' },
    { value: DeviceFaultSource.Plc, label: 'PLC' },
    { value: DeviceFaultSource.Workflow, label: '工作流' },
]

const faultLevelI18nKeys: Record<DeviceFaultLevel, string> = {
    [DeviceFaultLevel.Warning]: 'deviceState.faultLevel.warning',
    [DeviceFaultLevel.GeneralFault]: 'deviceState.faultLevel.generalFault',
    [DeviceFaultLevel.SevereFault]: 'deviceState.faultLevel.severeFault',
    [DeviceFaultLevel.SafetyFault]: 'deviceState.faultLevel.safetyFault',
}

function faultLevelLabel(level: DeviceFaultLevel): string {
    return t(faultLevelI18nKeys[level] ?? '')
}

const faultSourceLabels: Record<DeviceFaultSource, string> = {
    [DeviceFaultSource.System]: '系统',
    [DeviceFaultSource.Camera]: '相机',
    [DeviceFaultSource.Plc]: 'PLC',
    [DeviceFaultSource.Workflow]: '工作流',
}

function faultSourceLabel(source: DeviceFaultSource): string {
    return faultSourceLabels[source] ?? '系统'
}

function faultAssociation(data: DeviceFaultDto): string {
    const parts = [data.deviceName, data.workflowProjectName, data.workflowName, data.workflowNodeId]
    return parts.filter((value): value is string => Boolean(value)).join(' / ') || '—'
}

const resolvedOptions = computed<Array<{ value: boolean | null; label: string }>>(() => [
    { value: null, label: t('deviceState.all') },
    { value: false, label: t('deviceState.unresolved') },
    { value: true, label: t('deviceState.resolved') },
])

onMounted(() => {
    void loadAsync()
})
</script>

<template>
    <div class="space-y-4">
        <!-- 标题 + 刷新 -->
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('deviceState.faultHistory') }}</h1>
            <Button severity="secondary" outlined size="small" :disabled="loading" @click="void loadAsync()">
                <RefreshCw :class="['size-4', loading && 'animate-spin']" />
            </Button>
        </div>

        <!-- 数据卡（含筛选） -->
        <AppCard :beam="true">
            <!-- 筛选区 -->
            <div class="flex flex-col gap-3 border-b border-border/40 px-3 py-2">
                <div
                    class="grid items-center gap-x-3 gap-y-2"
                    style="grid-template-columns: repeat(auto-fill, 5.5rem 13rem)"
                >
                    <span class="text-sm text-muted-foreground whitespace-nowrap">{{ t('deviceState.level') }}</span>
                    <Select
                        v-model="filterFaultLevel"
                        :options="faultLevelOptions"
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
                        @change="onFilterChange"
                    />
                    <span class="text-sm text-muted-foreground whitespace-nowrap">来源</span>
                    <Select
                        v-model="filterSource"
                        :options="faultSourceOptions"
                        option-label="label"
                        option-value="value"
                        size="small"
                        class="!text-xs w-full"
                        @change="onFilterChange"
                    />
                    <span class="text-sm text-muted-foreground whitespace-nowrap">{{ t('deviceState.status') }}</span>
                    <Select
                        v-model="filterIsResolved"
                        :options="resolvedOptions"
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
                        @change="onFilterChange"
                    />
                    <span class="text-sm text-muted-foreground whitespace-nowrap">
                        {{ t('deviceState.startTime') }}
                    </span>
                    <DatePicker
                        v-model="filterStartTime"
                        show-time
                        showIcon
                        fluid
                        :showOnFocus="false"
                        size="small"
                        @date-select="onFilterChange"
                        @clear-click="onFilterChange"
                        show-button-bar
                    />
                    <span class="text-sm text-muted-foreground whitespace-nowrap">{{ t('deviceState.endTime') }}</span>
                    <DatePicker
                        v-model="filterEndTime"
                        show-time
                        show-icon
                        fluid
                        :showOnFocus="false"
                        size="small"
                        @date-select="onFilterChange"
                        @clear-click="onFilterChange"
                        show-button-bar
                    />
                </div>
                <!-- 操作行 -->
                <div class="flex items-center gap-3">
                    <Button severity="secondary" size="small" outlined @click="onReset">
                        {{ t('deviceState.reset') }}
                    </Button>
                    <span class="ml-auto text-xs text-muted-foreground">
                        {{ t('deviceState.total', { count: totalCount }) }}
                    </span>
                </div>
            </div>
            <DataTable
                :value="items"
                :loading="loading"
                :lazy="true"
                :paginator="false"
                :rows="pageSize"
                :first="first"
                :total-records="totalCount"
                striped-rows
                size="small"
                data-key="id"
                :pt="{ root: { class: 'overflow-hidden' } }"
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
                            {{ faultLevelLabel(data.faultLevel as DeviceFaultLevel) }}
                        </span>
                    </template>
                </Column>

                <Column field="faultCode" :header="t('deviceState.faultCode')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <span class="font-mono text-xs">{{ data.faultCode ?? '—' }}</span>
                    </template>
                </Column>

                <Column header="来源 / 关联" style="min-width: 12rem; max-width: 20rem">
                    <template #body="{ data }">
                        <div class="text-xs">
                            <div class="font-medium">{{ faultSourceLabel(data.source as DeviceFaultSource) }}</div>
                            <div class="truncate text-muted-foreground" :title="faultAssociation(data)">
                                {{ faultAssociation(data) }}
                            </div>
                        </div>
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

                <Column header="次数" style="min-width: 4.5rem">
                    <template #body="{ data }">
                        <span class="tabular-nums">{{ data.occurrenceCount }}</span>
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
    </div>
</template>
