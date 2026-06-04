<script setup lang="ts">
// 状态切换日志页：PrimeVue DataTable + AppCard 重构版（含懒加载分页）
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import Button from 'primevue/button'
import {
    type DeviceStateLogDto,
    type GetStateLogPagedInput,
    DeviceStatus,
    StateChangeTrigger,
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

const currentPage = computed(() => Math.floor(first.value / pageSize.value) + 1)
const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize.value)))

function goToPage(page: number): void {
    first.value = (page - 1) * pageSize.value
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

const deviceStatusI18nKeys: Record<DeviceStatus, string> = {
    [DeviceStatus.Standby]: 'deviceState.deviceStatus.standby',
    [DeviceStatus.Starting]: 'deviceState.deviceStatus.starting',
    [DeviceStatus.Running]: 'deviceState.deviceStatus.running',
    [DeviceStatus.Paused]: 'deviceState.deviceStatus.paused',
    [DeviceStatus.Stopping]: 'deviceState.deviceStatus.stopping',
    [DeviceStatus.Stopped]: 'deviceState.deviceStatus.stopped',
    [DeviceStatus.Resetting]: 'deviceState.deviceStatus.resetting',
    [DeviceStatus.FaultAcknowledging]: 'deviceState.deviceStatus.faultAcknowledging',
    [DeviceStatus.Fault]: 'deviceState.deviceStatus.fault',
    [DeviceStatus.EmergencyStop]: 'deviceState.deviceStatus.emergencyStop',
    [DeviceStatus.Initializing]: 'deviceState.deviceStatus.initializing',
}

const triggerI18nKeys: Record<StateChangeTrigger, string> = {
    [StateChangeTrigger.UserManual]: 'deviceState.changeTrigger.userManual',
    [StateChangeTrigger.SystemAuto]: 'deviceState.changeTrigger.systemAuto',
    [StateChangeTrigger.DeviceFault]: 'deviceState.changeTrigger.deviceFault',
    [StateChangeTrigger.EmergencyButton]: 'deviceState.changeTrigger.emergencyButton',
    [StateChangeTrigger.RemoteCommand]: 'deviceState.changeTrigger.remoteCommand',
    [StateChangeTrigger.Watchdog]: 'deviceState.changeTrigger.watchdog',
    [StateChangeTrigger.SystemInit]: 'deviceState.changeTrigger.systemInit',
}

function deviceStatusLabel(status: DeviceStatus): string {
    return t(deviceStatusI18nKeys[status] ?? '')
}

function triggerLabel(trigger: StateChangeTrigger): string {
    return t(triggerI18nKeys[trigger] ?? '')
}

const successOptions = computed<Array<{ value: boolean | null; label: string }>>(() => [
    { value: null, label: t('deviceState.all') },
    { value: true, label: t('deviceState.successful') },
    { value: false, label: t('deviceState.failed') },
])

const triggerOptions = computed<Array<{ value: StateChangeTrigger | null; label: string }>>(() => [
    { value: null, label: t('deviceState.all') },
    { value: StateChangeTrigger.UserManual, label: triggerLabel(StateChangeTrigger.UserManual) },
    { value: StateChangeTrigger.SystemAuto, label: triggerLabel(StateChangeTrigger.SystemAuto) },
    { value: StateChangeTrigger.DeviceFault, label: triggerLabel(StateChangeTrigger.DeviceFault) },
    { value: StateChangeTrigger.EmergencyButton, label: triggerLabel(StateChangeTrigger.EmergencyButton) },
    { value: StateChangeTrigger.RemoteCommand, label: triggerLabel(StateChangeTrigger.RemoteCommand) },
    { value: StateChangeTrigger.Watchdog, label: triggerLabel(StateChangeTrigger.Watchdog) },
    { value: StateChangeTrigger.SystemInit, label: triggerLabel(StateChangeTrigger.SystemInit) },
])

const statusOptions = computed<Array<{ value: DeviceStatus | null; label: string }>>(() => [
    { value: null, label: t('deviceState.all') },
    { value: DeviceStatus.Standby, label: deviceStatusLabel(DeviceStatus.Standby) },
    { value: DeviceStatus.Starting, label: deviceStatusLabel(DeviceStatus.Starting) },
    { value: DeviceStatus.Running, label: deviceStatusLabel(DeviceStatus.Running) },
    { value: DeviceStatus.Paused, label: deviceStatusLabel(DeviceStatus.Paused) },
    { value: DeviceStatus.Stopping, label: deviceStatusLabel(DeviceStatus.Stopping) },
    { value: DeviceStatus.Stopped, label: deviceStatusLabel(DeviceStatus.Stopped) },
    { value: DeviceStatus.Resetting, label: deviceStatusLabel(DeviceStatus.Resetting) },
    { value: DeviceStatus.FaultAcknowledging, label: deviceStatusLabel(DeviceStatus.FaultAcknowledging) },
    { value: DeviceStatus.Fault, label: deviceStatusLabel(DeviceStatus.Fault) },
    { value: DeviceStatus.EmergencyStop, label: deviceStatusLabel(DeviceStatus.EmergencyStop) },
    { value: DeviceStatus.Initializing, label: deviceStatusLabel(DeviceStatus.Initializing) },
])

onMounted(() => {
    void loadAsync()
})
</script>

<template>
    <div class="flex flex-col gap-4">
        <h1 class="text-2xl font-bold tracking-tight">{{ t('deviceState.stateLog') }}</h1>

        <!-- 数据卡（含筛选） -->
        <AppCard :beam="true">
            <!-- 筛选区 -->
            <div class="flex flex-col gap-3 border-b border-border/40 px-3 py-2">
                <!-- repeat(auto-fill, 固定标签列 固定输入列)：基于容器实际宽度动态换行，所有行列对齐 -->
                <div
                    class="grid items-center gap-x-3 gap-y-2"
                    style="grid-template-columns: repeat(auto-fill, 5.5rem 13rem)"
                >
                    <span class="text-sm text-muted-foreground whitespace-nowrap">{{ t('deviceState.status') }}</span>
                    <Select
                        v-model="filterIsSuccessful"
                        :options="successOptions"
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
                    <span class="text-sm text-muted-foreground whitespace-nowrap">{{ t('deviceState.trigger') }}</span>
                    <Select
                        v-model="filterTrigger"
                        :options="triggerOptions"
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
                        {{ t('deviceState.targetStatus') }}
                    </span>
                    <Select
                        v-model="filterStatus"
                        :options="statusOptions"
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
                        show-icon
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
            <!-- 表格（PrimeVue DataTable 懒加载分页） -->
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

                <Column :header="t('deviceState.stateChange')" style="min-width: 12rem">
                    <template #body="{ data }">
                        <span class="text-muted-foreground">
                            {{
                                data.previousStatus != null
                                    ? deviceStatusLabel(data.previousStatus as DeviceStatus)
                                    : '—'
                            }}
                        </span>
                        <span class="mx-1.5 text-muted-foreground">→</span>
                        <span class="font-medium">{{ deviceStatusLabel(data.newStatus as DeviceStatus) }}</span>
                    </template>
                </Column>

                <Column :header="t('deviceState.trigger')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <span class="text-muted-foreground">
                            {{ triggerLabel(data.trigger as StateChangeTrigger) }}
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
