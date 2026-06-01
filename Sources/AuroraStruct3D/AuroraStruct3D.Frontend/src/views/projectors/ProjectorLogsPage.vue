<script setup lang="ts">
// 投影机操作记录页：独立页面，从投影机管理页拆分
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import DatePicker from 'primevue/datepicker'
import { useProjectorStore } from '@/stores/projectors'
import { type ProjectorOperationLogDto, getProjectorLogs } from '@/api/projectors'
import { AppCard } from '@/components/primevue'

const { t } = useI18n()
const store = useProjectorStore()

// ─── 操作类型标签 ─────────────────────────────────────────────────────────
const projectorOpTypeKeys: Record<number, string> = {
    0: 'projector.opType.connect',
    1: 'projector.opType.disconnect',
    2: 'projector.opType.ledOn',
    3: 'projector.opType.ledOff',
    4: 'projector.opType.setLight',
    5: 'projector.opType.setDisplayMode',
    6: 'projector.opType.setColor',
    7: 'projector.opType.triggerOnce',
    8: 'projector.opType.sendRawCommand',
    9: 'projector.opType.queryStatus',
    10: 'projector.opType.setFlip',
    11: 'projector.opType.setTriggerMode',
    12: 'projector.opType.setBootImage',
    13: 'projector.opType.checkerboard',
    14: 'projector.opType.setRgb',
    15: 'projector.opType.softReset',
    16: 'projector.opType.saveParams',
    17: 'projector.opType.readRegister',
    18: 'projector.opType.writeRegister',
}

function projectorOpLabel(type: number): string {
    const key = projectorOpTypeKeys[type]
    return key ? t(key) : `#${type}`
}

// ─── 分页与筛选 ────────────────────────────────────────────────────────────
const logSelectedId = ref<string | null>(null)
const logItems = ref<ProjectorOperationLogDto[]>([])
const logLoading = ref(false)
const logFirst = ref(0)
const logTotalCount = ref(0)
const logPageSize = ref(20)
const logFailedOnly = ref(false)
const logStartTime = ref<Date | null>(null)
const logEndTime = ref<Date | null>(null)
const logCurrentPage = computed(() => Math.floor(logFirst.value / logPageSize.value) + 1)
const logTotalPages = computed(() => Math.max(1, Math.ceil(logTotalCount.value / logPageSize.value)))

function goToLogPage(page: number): void {
    logFirst.value = (page - 1) * logPageSize.value
    void loadLogs()
}

async function loadLogs(): Promise<void> {
    if (!logSelectedId.value) {
        logItems.value = []
        logTotalCount.value = 0
        return
    }
    logLoading.value = true
    try {
        const result = await getProjectorLogs({
            projectorDeviceId: logSelectedId.value,
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

function onFilterChange(): void {
    logFirst.value = 0
    void loadLogs()
}

onMounted(() => {
    void store.fetchList()
})
</script>

<template>
    <div class="flex flex-col gap-4">
        <h1 class="text-2xl font-bold tracking-tight">{{ t('projector.logsTitle') }}</h1>

        <AppCard :beam="false">
            <!-- 筛选栏 -->
            <div class="flex flex-wrap items-center gap-3 border-b border-border/40 px-3 py-2">
                <Select
                    v-model="logSelectedId"
                    :options="store.projectors"
                    option-label="name"
                    option-value="id"
                    :placeholder="t('projector.selectProjector')"
                    size="small"
                    class="!text-xs w-[10rem]"
                    :pt="{
                        root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                        label: { class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full' },
                        dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                    }"
                    @change="onFilterChange"
                />
                <label class="flex cursor-pointer items-center gap-1.5 text-sm">
                    <input v-model="logFailedOnly" type="checkbox" class="accent-primary" @change="onFilterChange" />
                    {{ t('common.failedOnly') }}
                </label>
                <div class="flex items-center gap-1.5">
                    <span class="text-sm text-muted-foreground whitespace-nowrap">
                        {{ t('deviceState.startTime') }}
                    </span>
                    <DatePicker
                        v-model="logStartTime"
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
                <div class="flex items-center gap-1.5">
                    <span class="text-sm text-muted-foreground whitespace-nowrap">
                        {{ t('deviceState.endTime') }}
                    </span>
                    <DatePicker
                        v-model="logEndTime"
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
                <Button
                    severity="secondary"
                    size="small"
                    outlined
                    class="ml-auto !h-7 !px-2 !py-0 !text-xs"
                    :disabled="!logSelectedId"
                    @click="void loadLogs()"
                >
                    {{ t('projector.refresh') }}
                </Button>
            </div>

            <!-- 数据表 -->
            <DataTable :value="logItems" :loading="logLoading" striped-rows size="small" data-key="id">
                <template #empty>
                    <div class="py-6 text-center text-sm text-muted-foreground">
                        {{ logSelectedId ? t('projector.noLogs') : t('projector.selectProjectorHint') }}
                    </div>
                </template>
                <Column field="occurredAt" :header="t('camera.logTime')" style="min-width: 11rem">
                    <template #body="{ data }">
                        <span class="tabular-nums text-xs">{{ new Date(data.occurredAt).toLocaleString() }}</span>
                    </template>
                </Column>
                <Column field="operationType" :header="t('camera.logOperation')" style="min-width: 7rem">
                    <template #body="{ data }">
                        <span class="text-xs">{{ projectorOpLabel(data.operationType) }}</span>
                    </template>
                </Column>
                <Column field="isSuccess" :header="t('camera.logResult')" style="min-width: 5rem">
                    <template #body="{ data }">
                        <Tag
                            :severity="data.isSuccess ? 'success' : 'danger'"
                            :value="data.isSuccess ? t('camera.logSuccess') : t('camera.logFailed')"
                            class="!text-xs"
                        />
                    </template>
                </Column>
                <Column field="roundTripMs" :header="t('camera.logDuration')" style="min-width: 5rem">
                    <template #body="{ data }">
                        <span class="tabular-nums text-xs">{{ data.roundTripMs }} ms</span>
                    </template>
                </Column>
                <Column :header="t('camera.logDetail')" style="min-width: 12rem; max-width: 28rem">
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

            <!-- 自定义分页 -->
            <div
                v-if="logTotalCount > logPageSize"
                class="flex items-center justify-between px-4 py-3 border-t border-border/50 text-sm"
            >
                <span class="text-muted-foreground text-xs">
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
