<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import ToggleSwitch from 'primevue/toggleswitch'
import { Search } from '@lucide/vue'
import { AppCard } from '@/components/primevue'
import {
    ProductModelOperationType,
    type ProductModelOperationLogDto,
    getProductModelLogsAsync,
} from '@/api/product-models'

const { t } = useI18n()

const logItems = ref<ProductModelOperationLogDto[]>([])
const logLoading = ref(false)
const logFirst = ref(0)
const logTotalCount = ref(0)
const logPageSize = ref(20)
const logFilter = ref('')
const logOperationType = ref<ProductModelOperationType | null>(null)
const logFailedOnly = ref(false)
const logStartTime = ref<Date | null>(null)
const logEndTime = ref<Date | null>(null)

const logCurrentPage = computed(() => Math.floor(logFirst.value / logPageSize.value) + 1)
const logTotalPages = computed(() => Math.max(1, Math.ceil(logTotalCount.value / logPageSize.value)))

const operationTypeOptions = computed(() => [
    { label: t('productModel.opType.upload'), value: ProductModelOperationType.Upload },
    { label: t('productModel.opType.rename'), value: ProductModelOperationType.Rename },
    { label: t('productModel.opType.delete'), value: ProductModelOperationType.Delete },
    { label: t('productModel.opType.retryConversion'), value: ProductModelOperationType.RetryConversion },
    { label: t('productModel.opType.download'), value: ProductModelOperationType.Download },
    {
        label: t('productModel.opType.cleanUpOrphanedRecords'),
        value: ProductModelOperationType.CleanUpOrphanedRecords,
    },
])

const productModelOpTypeKeys: Record<number, string> = {
    0: 'productModel.opType.upload',
    1: 'productModel.opType.rename',
    2: 'productModel.opType.delete',
    3: 'productModel.opType.retryConversion',
    4: 'productModel.opType.download',
    5: 'productModel.opType.cleanUpOrphanedRecords',
}

function productModelOpLabel(type: number): string {
    const key = productModelOpTypeKeys[type]
    return key ? t(key) : `#${type}`
}

async function loadLogs(): Promise<void> {
    logLoading.value = true
    try {
        const result = await getProductModelLogsAsync({
            filter: logFilter.value || undefined,
            operationType: logOperationType.value,
            isFailedOnly: logFailedOnly.value || undefined,
            startTime: logStartTime.value ? logStartTime.value.toISOString() : undefined,
            endTime: logEndTime.value ? logEndTime.value.toISOString() : undefined,
            skipCount: logFirst.value,
            maxResultCount: logPageSize.value,
        })
        logItems.value = result.items
        logTotalCount.value = result.totalCount
    } finally {
        logLoading.value = false
    }
}

function handleSearch(): void {
    logFirst.value = 0
    void loadLogs()
}

function handleReset(): void {
    logFilter.value = ''
    logOperationType.value = null
    logFailedOnly.value = false
    logStartTime.value = null
    logEndTime.value = null
    logFirst.value = 0
    void loadLogs()
}

function goToLogPage(page: number): void {
    logFirst.value = (page - 1) * logPageSize.value
    void loadLogs()
}

onMounted(() => {
    void loadLogs()
})
</script>

<template>
    <div class="flex flex-col gap-4">
        <h1 class="text-2xl font-bold tracking-tight">{{ t('productModel.logsTitle') }}</h1>

        <AppCard :beam="false">
            <div class="flex flex-col gap-3 border-b border-border/40 px-3 py-2">
                <div
                    class="grid items-center gap-x-3 gap-y-2"
                    style="grid-template-columns: repeat(auto-fill, 5.5rem 13rem)"
                >
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('productModel.name') }}</span>
                    <InputText
                        v-model="logFilter"
                        size="small"
                        class="!text-xs w-full"
                        :placeholder="t('productModel.logsSearchPlaceholder')"
                        @keydown.enter="handleSearch"
                    />
                    <span class="whitespace-nowrap text-sm text-muted-foreground">
                        {{ t('productModel.logOperation') }}
                    </span>
                    <Select
                        v-model="logOperationType"
                        :options="operationTypeOptions"
                        option-label="label"
                        option-value="value"
                        :placeholder="t('productModel.operationPlaceholder')"
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
                    <span class="whitespace-nowrap text-sm text-muted-foreground">
                        {{ t('productModel.startDate') }}
                    </span>
                    <DatePicker
                        v-model="logStartTime"
                        show-time
                        show-icon
                        fluid
                        :showOnFocus="false"
                        size="small"
                        show-button-bar
                        @date-select="handleSearch"
                        @clear-click="handleSearch"
                    />
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('productModel.endDate') }}</span>
                    <DatePicker
                        v-model="logEndTime"
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
                <div class="flex items-center gap-3">
                    <label class="flex items-center gap-2 text-sm text-muted-foreground">
                        <ToggleSwitch v-model="logFailedOnly" @change="handleSearch" />
                        {{ t('common.failedOnly') }}
                    </label>
                    <Button severity="secondary" outlined size="small" @click="handleSearch">
                        <Search class="mr-1 size-4" />
                        {{ t('common.search') }}
                    </Button>
                    <Button text severity="secondary" size="small" @click="handleReset">
                        {{ t('common.reset') }}
                    </Button>
                    <span class="ml-auto text-xs text-muted-foreground">
                        {{ t('management.totalRecords', { total: logTotalCount }) }}
                    </span>
                </div>
            </div>

            <DataTable :value="logItems" :loading="logLoading" striped-rows size="small" data-key="id">
                <template #empty>
                    <div class="py-6 text-center text-sm text-muted-foreground">
                        {{ t('productModel.noLogs') }}
                    </div>
                </template>
                <Column field="occurredAt" :header="t('productModel.logTime')" style="min-width: 11rem">
                    <template #body="{ data }">
                        <span class="tabular-nums text-xs">{{ new Date(data.occurredAt).toLocaleString() }}</span>
                    </template>
                </Column>
                <Column
                    field="modelName"
                    :header="t('productModel.logModel')"
                    style="min-width: 10rem; max-width: 16rem"
                >
                    <template #body="{ data }">
                        <div class="truncate text-xs" :title="data.modelName">{{ data.modelName }}</div>
                    </template>
                </Column>
                <Column
                    field="originalFileName"
                    :header="t('productModel.logFile')"
                    style="min-width: 10rem; max-width: 16rem"
                >
                    <template #body="{ data }">
                        <div class="truncate text-xs text-muted-foreground" :title="data.originalFileName ?? ''">
                            {{ data.originalFileName ?? '—' }}
                        </div>
                    </template>
                </Column>
                <Column field="operationType" :header="t('productModel.logOperation')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <span class="text-xs">{{ productModelOpLabel(data.operationType) }}</span>
                    </template>
                </Column>
                <Column field="isSuccess" :header="t('productModel.logResult')" style="min-width: 5rem">
                    <template #body="{ data }">
                        <Tag
                            :severity="data.isSuccess ? 'success' : 'danger'"
                            :value="data.isSuccess ? t('productModel.logSuccess') : t('productModel.logFailed')"
                            class="!text-xs"
                        />
                    </template>
                </Column>
                <Column field="durationMs" :header="t('productModel.logDuration')" style="min-width: 6rem">
                    <template #body="{ data }">
                        <span class="tabular-nums text-xs">
                            {{ data.durationMs >= 0 ? `${data.durationMs} ms` : '—' }}
                        </span>
                    </template>
                </Column>
                <Column :header="t('productModel.logDetail')" style="min-width: 14rem; max-width: 28rem">
                    <template #body="{ data }">
                        <span
                            v-if="data.errorMessage"
                            class="truncate text-xs text-destructive"
                            :title="data.errorMessage"
                        >
                            {{ data.errorMessage }}
                        </span>
                        <span
                            v-else-if="data.parameterSummary"
                            class="truncate text-xs text-muted-foreground"
                            :title="data.parameterSummary"
                        >
                            {{ data.parameterSummary }}
                        </span>
                        <span v-else class="text-xs text-muted-foreground">—</span>
                    </template>
                </Column>
            </DataTable>

            <div
                v-if="logTotalCount > logPageSize"
                class="flex items-center justify-between border-t border-border/50 px-4 py-3 text-sm"
            >
                <span class="text-xs text-muted-foreground">
                    {{ t('management.totalRecords', { total: logTotalCount }) }}
                    &nbsp;·&nbsp;
                    {{
                        t('management.pageRange', {
                            from: (logCurrentPage - 1) * logPageSize + 1,
                            to: Math.min(logCurrentPage * logPageSize, logTotalCount),
                        })
                    }}
                </span>
                <div class="flex items-center gap-1">
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="logCurrentPage <= 1 || logLoading"
                        @click="goToLogPage(1)"
                    >
                        «
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="logCurrentPage <= 1 || logLoading"
                        @click="goToLogPage(logCurrentPage - 1)"
                    >
                        ‹
                    </Button>
                    <span class="px-3 text-muted-foreground">{{ logCurrentPage }} / {{ logTotalPages }}</span>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="logCurrentPage >= logTotalPages || logLoading"
                        @click="goToLogPage(logCurrentPage + 1)"
                    >
                        ›
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="logCurrentPage >= logTotalPages || logLoading"
                        @click="goToLogPage(logTotalPages)"
                    >
                        »
                    </Button>
                </div>
            </div>
        </AppCard>
    </div>
</template>
