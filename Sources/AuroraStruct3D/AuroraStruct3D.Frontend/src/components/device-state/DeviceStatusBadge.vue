<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Tag from 'primevue/tag'
import { DeviceStatus } from '@/api/device-state'

interface Props {
    /** 当前设备状态值 */
    status: DeviceStatus | null | undefined
}

const props = defineProps<Props>()
const { t } = useI18n()

const label = computed<string>(() => {
    if (props.status == null) return '—'
    const keyMap: Record<DeviceStatus, string> = {
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
    return t(keyMap[props.status] ?? String(props.status))
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
