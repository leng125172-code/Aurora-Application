<script setup lang="ts">
import { computed } from 'vue'
import Tag from 'primevue/tag'
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

/** 根据状态返回 PrimeVue Tag 的 severity */
const severity = computed<'success' | 'secondary' | 'danger' | 'info'>(() => {
    if (props.status == null) return 'info'
    if (props.status === DeviceStatus.Running) return 'success'
    if (props.status === DeviceStatus.Fault || props.status === DeviceStatus.EmergencyStop) return 'danger'
    if (
        props.status === DeviceStatus.Standby ||
        props.status === DeviceStatus.Paused ||
        props.status === DeviceStatus.Stopped
    )
        return 'secondary'
    return 'info'
})
</script>

<template>
    <Tag :severity="severity" :value="label" class="select-none whitespace-nowrap" />
</template>
