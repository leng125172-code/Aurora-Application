<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Select from 'primevue/select'
import { DeviceRunMode, switchModeAsync, type DeviceStateDto } from '@/api/device-state'
import { useAuthStore } from '@/stores/auth'
import { useAppToast } from '@/composables/useAppToast'

interface Props {
    /** 当前设备状态快照（用于判断是否可切换） */
    deviceState: DeviceStateDto | null | undefined
    /** 是否禁用（外部额外控制） */
    disabled?: boolean
}

const props = defineProps<Props>()

const { t } = useI18n()
const auth = useAuthStore()
const toast = useAppToast()

/** 仅在已登录且 canSwitchMode 时允许操作 */
const isDisabled = computed<boolean>(() => {
    return props.disabled === true || !auth.isAuthenticated || !props.deviceState?.canSwitchMode
})

/** 当前选中的模式（字符串形式，供 Select 绑定） */
const currentMode = computed<string>(() => {
    return String(props.deviceState?.runMode ?? DeviceRunMode.Online)
})

/** 模式选项列表（响应当前语言） */
const modeOptions = computed(() => [
    { value: String(DeviceRunMode.Online), label: t('deviceState.runMode.online') },
    { value: String(DeviceRunMode.Auto), label: t('deviceState.runMode.auto') },
    { value: String(DeviceRunMode.Manual), label: t('deviceState.runMode.manual') },
    { value: String(DeviceRunMode.Maintenance), label: t('deviceState.runMode.maintenance') },
])

async function handleChange(value: string): Promise<void> {
    const newMode = Number(value) as DeviceRunMode
    if (newMode === props.deviceState?.runMode) return

    const modeLabel = modeOptions.value.find((o) => o.value === value)?.label ?? value
    try {
        await switchModeAsync({ newMode })
        toast.success(t('deviceState.switchedToMode', { mode: modeLabel }))
    } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : t('deviceState.switchModeFailed')
        toast.error(msg)
    }
}
</script>

<template>
    <Select
        :model-value="currentMode"
        :options="modeOptions"
        option-label="label"
        option-value="value"
        :disabled="isDisabled"
        :placeholder="t('deviceState.runMode.online')"
        size="small"
        class="!text-xs !w-[9rem]"
        :pt="{
            root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
            label: { class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full' },
            dropdown: { class: '!w-6 !flex !items-center !justify-center' },
        }"
        @update:model-value="(v) => handleChange(v as string)"
    />
</template>
