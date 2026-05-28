<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import {
    type DeviceStateLogDto,
    type GetStateLogPagedInput,
    DeviceStatus,
    DeviceStatusLabels,
    StateChangeTrigger,
    StateChangeTriggerLabels,
    getStateLogPagedListAsync,
} from '@/api/device-state'
import { Card, CardContent } from '@/components/ui/card'
import { BorderBeam } from '@/components/ui/border-beam'
import { Button } from '@/components/ui/button'

const { t } = useI18n()

// ─── 分页参数 ────────────────────────────────────────────────────────────────
const pageSize = ref(20)
const currentPage = ref(1)
const totalCount = ref(0)
const items = ref<DeviceStateLogDto[]>([])
const loading = ref(false)

// ─── 筛选条件 ────────────────────────────────────────────────────────────────
const filterIsSuccessful = ref<boolean | null>(null)
const filterTrigger = ref<StateChangeTrigger | null>(null)
const filterStatus = ref<DeviceStatus | null>(null)
const filterStartTime = ref<string>('')
const filterEndTime = ref<string>('')

// ─── 计算尾页 ────────────────────────────────────────────────────────────────
const isLastPage = computed<boolean>(() => {
    const total = totalCount.value
    return total > 0 && currentPage.value >= Math.ceil(total / pageSize.value)
})

async function loadAsync(): Promise<void> {
    loading.value = true
    try {
        const input: GetStateLogPagedInput = {
            skipCount: (currentPage.value - 1) * pageSize.value,
            maxResultCount: pageSize.value,
            sorting: 'occurredAt DESC',
            isSuccessful: filterIsSuccessful.value,
            trigger: filterTrigger.value,
            status: filterStatus.value,
            startTime: filterStartTime.value || null,
            endTime: filterEndTime.value || null,
        }
        const result = await getStateLogPagedListAsync(input)
        items.value = result.items
        totalCount.value = result.totalCount
    } finally {
        loading.value = false
    }
}

function onPageChange(page: number): void {
    currentPage.value = page
    void loadAsync()
}

function onFilterChange(): void {
    currentPage.value = 1
    void loadAsync()
}

function onReset(): void {
    filterIsSuccessful.value = null
    filterTrigger.value = null
    filterStatus.value = null
    filterStartTime.value = ''
    filterEndTime.value = ''
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
        <Card class="relative">
            <BorderBeam :size="80" :duration="8" />
            <CardContent class="flex flex-wrap items-center gap-3 py-3">
                <!-- 切换结果 -->
                <label class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.status') }}</span>
                    <select
                        v-model="filterIsSuccessful"
                        class="rounded border bg-background px-2 py-1 text-sm"
                        @change="onFilterChange"
                    >
                        <option v-for="opt in successOptions" :key="String(opt.value)" :value="opt.value">
                            {{ opt.label }}
                        </option>
                    </select>
                </label>

                <!-- 触发来源 -->
                <label class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.trigger') }}</span>
                    <select
                        v-model="filterTrigger"
                        class="rounded border bg-background px-2 py-1 text-sm"
                        @change="onFilterChange"
                    >
                        <option v-for="opt in triggerOptions" :key="String(opt.value)" :value="opt.value">
                            {{ opt.label }}
                        </option>
                    </select>
                </label>

                <!-- 目标状态 -->
                <label class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.targetStatus') }}</span>
                    <select
                        v-model="filterStatus"
                        class="rounded border bg-background px-2 py-1 text-sm"
                        @change="onFilterChange"
                    >
                        <option v-for="opt in statusOptions" :key="String(opt.value)" :value="opt.value">
                            {{ opt.label }}
                        </option>
                    </select>
                </label>

                <!-- 日期范围 -->
                <label class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.startTime') }}</span>
                    <input
                        v-model="filterStartTime"
                        type="datetime-local"
                        class="rounded border bg-background px-2 py-1 text-sm"
                        @change="onFilterChange"
                    />
                </label>
                <label class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.endTime') }}</span>
                    <input
                        v-model="filterEndTime"
                        type="datetime-local"
                        class="rounded border bg-background px-2 py-1 text-sm"
                        @change="onFilterChange"
                    />
                </label>

                <Button variant="outline" size="sm" class="text-muted-foreground" @click="onReset">
                    {{ t('deviceState.reset') }}
                </Button>
                <span class="ml-auto text-xs text-muted-foreground">
                    {{ t('deviceState.total', { count: totalCount }) }}
                </span>
            </CardContent>
        </Card>

        <!-- 表格 -->
        <Card class="overflow-auto">
            <CardContent class="p-0">
                <table class="w-full min-w-[800px] text-sm">
                    <thead class="border-b bg-muted/50">
                        <tr>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.occurredAt') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.stateChange') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.trigger') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.operator') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.status') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.duration') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.errorMessage') }}</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="loading" class="h-20">
                            <td colspan="7" class="text-center text-muted-foreground">
                                {{ t('deviceState.loading') }}
                            </td>
                        </tr>
                        <tr v-else-if="items.length === 0" class="h-20">
                            <td colspan="7" class="text-center text-muted-foreground">{{ t('deviceState.noData') }}</td>
                        </tr>
                        <tr v-for="item in items" :key="item.id" class="border-b transition-colors hover:bg-muted/30">
                            <!-- 发生时间 -->
                            <td class="px-3 py-2 tabular-nums">
                                {{ new Date(item.occurredAt).toLocaleString() }}
                            </td>

                            <!-- 状态变化 -->
                            <td class="px-3 py-2">
                                <span class="text-muted-foreground">
                                    {{ item.previousStatus != null ? DeviceStatusLabels[item.previousStatus] : '—' }}
                                </span>
                                <span class="mx-1.5 text-muted-foreground">→</span>
                                <span class="font-medium">{{ DeviceStatusLabels[item.newStatus] }}</span>
                            </td>

                            <!-- 触发来源 -->
                            <td class="px-3 py-2 text-muted-foreground">
                                {{ StateChangeTriggerLabels[item.trigger] }}
                            </td>

                            <!-- 操作者 -->
                            <td class="px-3 py-2">
                                {{ item.operatorName ?? '—' }}
                            </td>

                            <!-- 结果 -->
                            <td class="px-3 py-2">
                                <span :class="item.isSuccessful ? 'text-green-600' : 'text-destructive'">
                                    {{ item.isSuccessful ? t('deviceState.successful') : t('deviceState.failed') }}
                                </span>
                            </td>

                            <!-- 耗时 -->
                            <td class="px-3 py-2 tabular-nums text-muted-foreground">
                                {{ item.durationMs != null ? `${(item.durationMs / 1000).toFixed(1)}s` : '—' }}
                            </td>

                            <!-- 错误信息 -->
                            <td class="max-w-xs px-3 py-2">
                                <span
                                    v-if="item.errorMessage"
                                    class="truncate text-destructive"
                                    :title="item.errorMessage"
                                >
                                    {{ item.errorMessage }}
                                </span>
                                <span v-else class="text-muted-foreground">—</span>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </CardContent>
        </Card>

        <!-- 分页 -->
        <div class="flex items-center justify-end gap-2 text-sm">
            <Button
                variant="outline"
                size="sm"
                :disabled="currentPage <= 1 || loading"
                @click="onPageChange(currentPage - 1)"
            >
                {{ t('deviceState.prevPage') }}
            </Button>
            <span class="text-muted-foreground">
                {{ t('deviceState.pageOf', { current: currentPage, total: Math.ceil(totalCount / pageSize) || 1 }) }}
            </span>
            <Button
                variant="outline"
                size="sm"
                :disabled="isLastPage || loading"
                @click="onPageChange(currentPage + 1)"
            >
                {{ t('deviceState.nextPage') }}
            </Button>
        </div>
    </div>
</template>
