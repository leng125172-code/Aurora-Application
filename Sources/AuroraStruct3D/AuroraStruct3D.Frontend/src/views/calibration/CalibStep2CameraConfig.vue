<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import { AlertCircle, Camera, ChevronDown, ChevronRight, Loader2, RefreshCcw, Save } from '@lucide/vue'
import type { CmosSensorSize } from '@/api/calibration'
import { CameraStatus, type CameraDeviceDto } from '@/api/cameras'

interface CameraParamForm {
    sensorSize: string | null
    lensFocalLength: number | null
    maxAperture: number | null
    minAperture: number | null
    currentAperture: number | null
}

interface CameraHardwareData {
    imageWidthPixels: number | null
    imageHeightPixels: number | null
    exposureTimeMinUs: number | null
    exposureTimeMaxUs: number | null
}

const props = defineProps<{
    step2Loading: boolean
    step2Cameras: CameraDeviceDto[]
    expandedCameraId: string | null
    cameraConnecting: Record<string, boolean>
    cameraForms: Record<string, CameraParamForm>
    cameraHardware: Record<string, CameraHardwareData | null>
    hardwareLoading: Record<string, boolean>
    cameraSaving: Record<string, boolean>
    cmosSensorSizes: CmosSensorSize[]
    getSelectedCmosSize: (code: string | null) => CmosSensorSize | null
    calcPixelSize: (cameraId: string) => number | null
    toggleCameraExpand: (cameraId: string) => void
    readHardwareParams: (cam: CameraDeviceDto) => Promise<void>
    saveCameraParams: (cam: CameraDeviceDto) => Promise<void>
    cameraStatusColor: (status: CameraStatus) => string
    cameraStatusLabel: (status: CameraStatus) => string
}>()

const emit = defineEmits<{
    prev: []
    next: []
}>()

const { t } = useI18n()
</script>

<template>
    <div class="flex flex-col flex-1 min-h-0 overflow-y-auto py-6 px-2">
        <div
            v-if="props.step2Loading"
            class="flex items-center justify-center gap-2 py-16 text-muted-foreground text-sm"
        >
            <Loader2 class="size-4 animate-spin" />
            {{ t('common.loading') }}
        </div>

        <div
            v-else-if="props.step2Cameras.length === 0"
            class="flex flex-col items-center justify-center gap-3 py-16 text-muted-foreground"
        >
            <Camera class="size-10 opacity-25" />
            <p class="text-sm">{{ t('calib.step2NoCamera') }}</p>
        </div>

        <div v-else class="flex flex-col gap-2">
            <div
                v-for="cam in props.step2Cameras"
                :key="cam.id"
                class="border border-border/50 rounded-lg overflow-hidden"
            >
                <Button
                    size="small"
                    text
                    severity="secondary"
                    type="button"
                    class="w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-muted/30 transition-colors"
                    :class="{ 'bg-muted/20': props.expandedCameraId === cam.id }"
                    @click="props.toggleCameraExpand(cam.id)"
                >
                    <Camera
                        :class="['size-4 shrink-0', cam.isEnabled ? 'text-blue-400' : 'text-muted-foreground/40']"
                    />
                    <span class="flex-1 font-medium text-sm">{{ cam.name }}</span>
                    <span v-if="cam.model" class="text-xs text-muted-foreground hidden sm:inline">
                        {{ cam.model }}
                    </span>
                    <span v-if="props.cameraConnecting[cam.id]" class="flex items-center gap-1 text-xs text-amber-400">
                        <Loader2 class="size-3 animate-spin" />
                        {{ t('calib.step2CameraConnecting') }}
                    </span>
                    <span v-else :class="['text-xs', props.cameraStatusColor(cam.status)]">
                        ● {{ props.cameraStatusLabel(cam.status) }}
                    </span>
                    <span v-if="!cam.isEnabled" class="text-xs text-muted-foreground/50 ml-1">
                        {{ t('calib.disabled') }}
                    </span>
                    <ChevronDown
                        v-if="props.expandedCameraId === cam.id"
                        class="size-4 shrink-0 text-muted-foreground"
                    />
                    <ChevronRight v-else class="size-4 shrink-0 text-muted-foreground" />
                </Button>

                <div
                    v-if="props.expandedCameraId === cam.id"
                    class="border-t border-border/40 bg-background/20 px-5 py-4"
                >
                    <div v-if="!cam.isEnabled" class="flex items-center gap-2 text-sm text-muted-foreground py-2">
                        <AlertCircle class="size-4 shrink-0" />
                        {{ t('calib.step2CameraDisabled') }}
                    </div>

                    <div
                        v-else-if="props.cameraConnecting[cam.id]"
                        class="flex items-center gap-2 text-sm text-muted-foreground py-2"
                    >
                        <Loader2 class="size-4 animate-spin" />
                        {{ t('calib.step2CameraConnecting') }}
                    </div>

                    <div v-else class="space-y-5">
                        <div>
                            <h4 class="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3">
                                {{ t('calib.step2CmosSensorSize') }}
                            </h4>
                            <div class="grid grid-cols-1 sm:grid-cols-3 gap-x-6 gap-y-3 text-sm">
                                <div class="sm:col-span-1">
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2CmosSensorSize') }}
                                    </label>
                                    <Select
                                        v-model="props.cameraForms[cam.id].sensorSize"
                                        :options="props.cmosSensorSizes"
                                        :option-label="(item: CmosSensorSize) => t(item.labelKey)"
                                        option-value="code"
                                        :placeholder="t('common.pleaseSelect')"
                                        size="small"
                                        class="w-full !text-xs"
                                        :pt="{
                                            root: { class: '!py-0 !px-2 !h-7 !flex !items-center' },
                                            label: { class: '!text-xs !py-0' },
                                        }"
                                    />
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2SensorWidth') }}
                                    </label>
                                    <div
                                        class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                    >
                                        {{
                                            props.getSelectedCmosSize(props.cameraForms[cam.id]?.sensorSize)?.widthMm ??
                                            '—'
                                        }}
                                    </div>
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2SensorHeight') }}
                                    </label>
                                    <div
                                        class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                    >
                                        {{
                                            props.getSelectedCmosSize(props.cameraForms[cam.id]?.sensorSize)
                                                ?.heightMm ?? '—'
                                        }}
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div>
                            <h4 class="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3">
                                {{ t('calib.step2FocalLength') }}
                            </h4>
                            <div class="grid grid-cols-2 sm:grid-cols-4 gap-x-6 gap-y-3 text-sm">
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2FocalLength') }}
                                    </label>
                                    <InputNumber
                                        v-model="props.cameraForms[cam.id].lensFocalLength"
                                        :min="0"
                                        :max="10000"
                                        :max-fraction-digits="2"
                                        size="small"
                                        class="w-full"
                                        :input-class="'!text-xs !h-7 !py-0'"
                                    />
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2MaxAperture') }}
                                    </label>
                                    <InputNumber
                                        v-model="props.cameraForms[cam.id].maxAperture"
                                        :min="0.7"
                                        :max="64"
                                        :max-fraction-digits="1"
                                        size="small"
                                        class="w-full"
                                        :input-class="'!text-xs !h-7 !py-0'"
                                    />
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2MinAperture') }}
                                    </label>
                                    <InputNumber
                                        v-model="props.cameraForms[cam.id].minAperture"
                                        :min="0.7"
                                        :max="64"
                                        :max-fraction-digits="1"
                                        size="small"
                                        class="w-full"
                                        :input-class="'!text-xs !h-7 !py-0'"
                                    />
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2CurrentAperture') }}
                                    </label>
                                    <InputNumber
                                        v-model="props.cameraForms[cam.id].currentAperture"
                                        :min="0.7"
                                        :max="64"
                                        :max-fraction-digits="1"
                                        size="small"
                                        class="w-full"
                                        :input-class="'!text-xs !h-7 !py-0'"
                                    />
                                </div>
                            </div>
                        </div>

                        <div>
                            <div class="flex items-center justify-between mb-3">
                                <h4 class="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                                    {{ t('calib.step2ImageResolution') }} &amp; {{ t('calib.step2ExposureRange') }}
                                </h4>
                                <Button
                                    text
                                    severity="secondary"
                                    size="small"
                                    :disabled="props.hardwareLoading[cam.id]"
                                    @click="props.readHardwareParams(cam)"
                                >
                                    <Loader2 v-if="props.hardwareLoading[cam.id]" class="size-3 animate-spin mr-1" />
                                    <RefreshCcw v-else class="size-3 mr-1" />
                                    <span class="text-xs">
                                        {{
                                            props.hardwareLoading[cam.id]
                                                ? t('calib.step2ReadingCamera')
                                                : t('calib.step2ReadFromCamera')
                                        }}
                                    </span>
                                </Button>
                            </div>
                            <div class="grid grid-cols-2 sm:grid-cols-3 gap-x-6 gap-y-3 text-sm">
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2ImageWidth') }}
                                    </label>
                                    <div
                                        class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                    >
                                        {{ props.cameraHardware[cam.id]?.imageWidthPixels ?? '—' }}
                                    </div>
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2ImageHeight') }}
                                    </label>
                                    <div
                                        class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                    >
                                        {{ props.cameraHardware[cam.id]?.imageHeightPixels ?? '—' }}
                                    </div>
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2PixelSizeCalc') }}
                                    </label>
                                    <div
                                        class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                    >
                                        {{ props.calcPixelSize(cam.id) ?? '—' }}
                                    </div>
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2ExposureMin') }} (μs)
                                    </label>
                                    <div
                                        class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                    >
                                        {{ props.cameraHardware[cam.id]?.exposureTimeMinUs ?? '—' }}
                                    </div>
                                </div>
                                <div>
                                    <label class="block text-xs text-muted-foreground mb-1">
                                        {{ t('calib.step2ExposureMax') }} (μs)
                                    </label>
                                    <div
                                        class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                                    >
                                        {{ props.cameraHardware[cam.id]?.exposureTimeMaxUs ?? '—' }}
                                    </div>
                                </div>
                            </div>
                        </div>
                        <div class="flex justify-end pt-2">
                            <Button
                                size="small"
                                :disabled="props.cameraSaving[cam.id]"
                                @click="props.saveCameraParams(cam)"
                            >
                                <Loader2 v-if="props.cameraSaving[cam.id]" class="size-3.5 animate-spin mr-1.5" />
                                <Save v-else class="size-3.5 mr-1.5" />
                                {{ props.cameraSaving[cam.id] ? t('calib.step2Saving') : t('calib.step2SaveParam') }}
                            </Button>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <div class="flex justify-between gap-2 px-4 py-3 border-t border-border/40 mt-auto">
        <Button severity="secondary" outlined size="small" @click="emit('prev')">
            {{ t('calib.prevStep') }}
        </Button>
        <Button size="small" @click="emit('next')">
            {{ t('calib.nextStep') }}
        </Button>
    </div>
</template>
