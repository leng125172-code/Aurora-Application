<script setup lang="ts">
import { computed } from 'vue'
import { AlertTriangle, ShieldAlert } from '@lucide/vue'
import { type DeviceFaultDto, DeviceFaultLevel, DeviceFaultLevelLabels } from '@/api/device-state'

interface Props {
    /** 当前活跃故障（null 表示无故障，不显示横幅） */
    fault: DeviceFaultDto | null | undefined
}

const props = defineProps<Props>()

/** 根据故障等级返回样式 */
const bannerClass = computed<string>(() => {
    const level = props.fault?.faultLevel
    if (level === DeviceFaultLevel.SafetyFault || level === DeviceFaultLevel.SevereFault) {
        return 'bg-destructive/90 text-destructive-foreground'
    }
    if (level === DeviceFaultLevel.GeneralFault) {
        return 'bg-orange-500/90 text-white'
    }
    return 'bg-yellow-400/90 text-yellow-900'
})

const levelLabel = computed<string>(() => {
    const level = props.fault?.faultLevel
    if (level == null) return ''
    return DeviceFaultLevelLabels[level] ?? ''
})

const Icon = computed(() => {
    const level = props.fault?.faultLevel
    if (level === DeviceFaultLevel.SafetyFault || level === DeviceFaultLevel.SevereFault) {
        return ShieldAlert
    }
    return AlertTriangle
})
</script>

<template>
    <div v-if="fault" :class="[bannerClass, 'flex items-center gap-2 px-4 py-1.5 text-sm font-medium']">
        <component :is="Icon" class="size-4 shrink-0" />
        <span class="font-semibold">{{ levelLabel }}</span>
        <span v-if="fault.faultCode" class="opacity-80">[{{ fault.faultCode }}]</span>
        <span class="flex-1 truncate">{{ fault.faultMessage ?? '设备故障' }}</span>
        <span class="shrink-0 text-xs opacity-70">
            {{ new Date(fault.occurredAt).toLocaleTimeString() }}
        </span>
    </div>
</template>
