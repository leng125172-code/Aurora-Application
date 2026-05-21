<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { Select, SelectTrigger, SelectValue, SelectContent, SelectItem } from '@/components/ui/select'
import { DeviceRunMode, DeviceRunModeLabels, switchModeAsync, type DeviceStateDto } from '@/api/device-state'
import { useAuthStore } from '@/stores/auth'

interface Props {
    /** 当前设备状态快照（用于判断是否可切换） */
    deviceState: DeviceStateDto | null | undefined
    /** 是否禁用（外部额外控制） */
    disabled?: boolean
}

const props = defineProps<Props>()

const auth = useAuthStore()
const { t } = useI18n()

/** 仅在已登录且 canSwitchMode 时允许操作 */
const isDisabled = computed<boolean>(() => {
    return props.disabled === true || !auth.isAuthenticated || !props.deviceState?.canSwitchMode
})

/** 当前选中的模式（字符串形式，供 Select 绑定） */
const currentMode = computed<string>(() => {
    return String(props.deviceState?.runMode ?? DeviceRunMode.Online)
})

const modeOptions = Object.entries(DeviceRunModeLabels).map(([value, label]) => ({
    value,
    label,
}))

async function handleChange(value: string): Promise<void> {
    const newMode = Number(value) as DeviceRunMode
    if (newMode === props.deviceState?.runMode) return

    try {
        await switchModeAsync({ newMode })
        toast.success(`已切换到「${DeviceRunModeLabels[newMode]}」模式`)
    } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : '切换模式失败'
        toast.error(msg)
    }
}
</script>

<template>
    <Select :model-value="currentMode" :disabled="isDisabled" @update:model-value="handleChange">
        <SelectTrigger class="h-7 w-24 text-xs">
            <SelectValue placeholder="模式" />
        </SelectTrigger>
        <SelectContent>
            <SelectItem v-for="opt in modeOptions" :key="opt.value" :value="opt.value">
                {{ opt.label }}
            </SelectItem>
        </SelectContent>
    </Select>
</template>
