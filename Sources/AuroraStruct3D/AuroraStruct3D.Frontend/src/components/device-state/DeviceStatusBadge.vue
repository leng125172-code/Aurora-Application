<script setup lang="ts">
import { computed } from 'vue'
import { Badge } from '@/components/ui/badge'
import { DeviceStatus, DeviceStatusLabels } from '@/api/device-state'

interface Props {
    /** 当前设备状态值 */
    status: DeviceStatus | null | undefined
}

const props = defineProps<Props>()

const label = computed<string>(() => {
    if (props.status == null) return '—'
    return DeviceStatusLabels[props.status] ?? String(props.status)
})

/** 根据状态返回 shadcn Badge 的 variant */
const variant = computed<'default' | 'secondary' | 'destructive' | 'outline'>(() => {
    if (props.status == null) return 'outline'
    if (props.status === DeviceStatus.Running) return 'default'
    if (props.status === DeviceStatus.Fault || props.status === DeviceStatus.EmergencyStop) return 'destructive'
    if (
        props.status === DeviceStatus.Standby ||
        props.status === DeviceStatus.Paused ||
        props.status === DeviceStatus.Stopped
    )
        return 'secondary'
    return 'outline'
})
</script>

<template>
    <Badge :variant="variant" class="select-none whitespace-nowrap">
        {{ label }}
    </Badge>
</template>
