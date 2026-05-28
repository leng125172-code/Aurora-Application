<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import {
    type DeviceFaultDto,
    type GetFaultPagedInput,
    DeviceFaultLevel,
    DeviceFaultLevelLabels,
    getFaultPagedListAsync,
} from '@/api/device-state'
import { extractLogTag } from '@/utils/log-tag'
import { Card, CardContent } from '@/components/ui/card'
import { BorderBeam } from '@/components/ui/border-beam'
import { Button } from '@/components/ui/button'

const { t } = useI18n()

// ─── 分页参数 ────────────────────────────────────────────────────────────────
const pageSize = ref(20)
const currentPage = ref(1)
const totalCount = ref(0)
const items = ref<DeviceFaultDto[]>([])
const loading = ref(false)

// ─── 筛选条件 ────────────────────────────────────────────────────────────────
const filterFaultLevel = ref<DeviceFaultLevel | null>(null)
const filterIsResolved = ref<boolean | null>(null)
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
        const input: GetFaultPagedInput = {
            skipCount: (currentPage.value - 1) * pageSize.value,
            maxResultCount: pageSize.value,
            sorting: 'occurredAt DESC',
            faultLevel: filterFaultLevel.value,
            isResolved: filterIsResolved.value,
            startTime: filterStartTime.value || null,
            endTime: filterEndTime.value || null,
        }
        const result = await getFaultPagedListAsync(input)
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
    filterFaultLevel.value = null
    filterIsResolved.value = null
    filterStartTime.value = ''
    filterEndTime.value = ''
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
        <Card class="relative">
            <BorderBeam :size="80" :duration="8" />
            <CardContent class="flex flex-wrap items-center gap-3 py-3">
                <!-- 故障等级 -->
                <label class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.level') }}</span>
                    <select
                        v-model="filterFaultLevel"
                        class="rounded border bg-background px-2 py-1 text-sm"
                        @change="onFilterChange"
                    >
                        <option v-for="opt in faultLevelOptions" :key="String(opt.value)" :value="opt.value">
                            {{ opt.label }}
                        </option>
                    </select>
                </label>

                <!-- 是否解决 -->
                <label class="flex items-center gap-1.5 text-sm">
                    <span class="text-muted-foreground">{{ t('deviceState.status') }}</span>
                    <select
                        v-model="filterIsResolved"
                        class="rounded border bg-background px-2 py-1 text-sm"
                        @change="onFilterChange"
                    >
                        <option v-for="opt in resolvedOptions" :key="String(opt.value)" :value="opt.value">
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
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.level') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.faultCode') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.message') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.status') }}</th>
                            <th class="px-3 py-2 text-left font-medium">{{ t('deviceState.duration') }}</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="loading" class="h-20">
                            <td colspan="6" class="text-center text-muted-foreground">
                                {{ t('deviceState.loading') }}
                            </td>
                        </tr>
                        <tr v-else-if="items.length === 0" class="h-20">
                            <td colspan="6" class="text-center text-muted-foreground">{{ t('deviceState.noData') }}</td>
                        </tr>
                        <tr v-for="item in items" :key="item.id" class="border-b transition-colors hover:bg-muted/30">
                            <!-- 发生时间 -->
                            <td class="px-3 py-2 tabular-nums">
                                {{ new Date(item.occurredAt).toLocaleString() }}
                            </td>

                            <!-- 故障等级 -->
                            <td class="px-3 py-2">
                                <span
                                    :class="{
                                        'text-yellow-600': item.faultLevel === DeviceFaultLevel.Warning,
                                        'text-orange-500': item.faultLevel === DeviceFaultLevel.GeneralFault,
                                        'text-destructive':
                                            item.faultLevel === DeviceFaultLevel.SevereFault ||
                                            item.faultLevel === DeviceFaultLevel.SafetyFault,
                                    }"
                                >
                                    {{ DeviceFaultLevelLabels[item.faultLevel] }}
                                </span>
                            </td>

                            <!-- 故障码 -->
                            <td class="px-3 py-2 font-mono text-xs">
                                {{ item.faultCode ?? '—' }}
                            </td>

                            <!-- 故障信息（带日志标签颜色渲染） -->
                            <td class="max-w-xs px-3 py-2">
                                <template v-if="item.faultMessage">
                                    <template v-if="extractLogTag(item.faultMessage)">
                                        <span
                                            class="mr-1.5 inline-block rounded px-1 py-0.5 font-mono text-xs font-semibold text-white"
                                            :style="{
                                                backgroundColor: extractLogTag(item.faultMessage)!.color,
                                            }"
                                        >
                                            {{ extractLogTag(item.faultMessage)!.tag }}
                                        </span>
                                        <span :title="item.faultMessage" class="truncate">
                                            {{ extractLogTag(item.faultMessage)!.rest }}
                                        </span>
                                    </template>
                                    <span v-else :title="item.faultMessage" class="truncate">
                                        {{ item.faultMessage }}
                                    </span>
                                </template>
                                <span v-else class="text-muted-foreground">—</span>
                            </td>

                            <!-- 是否解决 -->
                            <td class="px-3 py-2">
                                <span :class="item.isResolved ? 'text-green-600' : 'text-muted-foreground'">
                                    {{ item.isResolved ? t('deviceState.resolved') : t('deviceState.unresolved') }}
                                </span>
                            </td>

                            <!-- 持续时长 -->
                            <td class="px-3 py-2 tabular-nums text-muted-foreground">
                                {{ item.durationMs != null ? `${(item.durationMs / 1000).toFixed(1)}s` : '—' }}
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
