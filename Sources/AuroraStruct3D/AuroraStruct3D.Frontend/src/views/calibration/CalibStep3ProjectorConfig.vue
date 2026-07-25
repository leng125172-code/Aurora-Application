<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import { AlertCircle, Loader2, Save } from '@lucide/vue'
import type { ProjectorDeviceDto } from '@/api/projectors'

interface FringeImageData {
    index: number
    label: string
    pixels: Uint8Array
}

const props = defineProps<{
    step3Loading: boolean
    step3Projectors: ProjectorDeviceDto[]
    selectedProjectorId: string | null
    projectorWidthPixels: number | null
    projectorPixelMode: string | null
    projectorReading: boolean
    fringeType: 'bw' | 'wb'
    projectorHeightInput: number
    fringe3PeriodCount: number
    fringe3ImageCount: number
    fringe3PhaseShift: number
    horizontalPaddingPosition: 'start' | 'end'
    fringe3PeriodError: string | null
    fringe3PhaseError: string | null
    fringe3CanGenerate: boolean
    generatingFringe: boolean
    generatedFringeImages: FringeImageData[]
    selectedFringeImageIdx: number
    downloadingFringe: boolean
    fringeDownloadProgress: number
    fetchProjectorResolution: () => Promise<void>
    generateFringeImages: () => Promise<void>
    triggerFringeDownload: () => Promise<void>
    saveConfig: () => Promise<void>
}>()

const emit = defineEmits<{
    prev: []
    next: []
    'update:selectedProjectorId': [value: string | null]
    'update:fringeType': [value: 'bw' | 'wb']
    'update:projectorHeightInput': [value: number]
    'update:fringe3PeriodCount': [value: number]
    'update:fringe3ImageCount': [value: number]
    'update:fringe3PhaseShift': [value: number]
    'update:horizontalPaddingPosition': [value: 'start' | 'end']
    'update:selectedFringeImageIdx': [value: number]
}>()

const { t } = useI18n()

const previewCanvasRef = ref<HTMLCanvasElement | null>(null)

const selectedProjectorIdModel = computed({
    get: () => props.selectedProjectorId,
    set: (value: string | null) => emit('update:selectedProjectorId', value),
})

const fringeTypeModel = computed({
    get: () => props.fringeType,
    set: (value: 'bw' | 'wb') => emit('update:fringeType', value),
})

const projectorHeightInputModel = computed({
    get: () => props.projectorHeightInput,
    set: (value: number | null) => emit('update:projectorHeightInput', value ?? 0),
})

const fringe3PeriodCountModel = computed({
    get: () => props.fringe3PeriodCount,
    set: (value: number | null) => emit('update:fringe3PeriodCount', value ?? 0),
})

const fringe3ImageCountModel = computed({
    get: () => props.fringe3ImageCount,
    set: (value: number | null) => emit('update:fringe3ImageCount', value ?? 0),
})

const fringe3PhaseShiftModel = computed({
    get: () => props.fringe3PhaseShift,
    set: (value: number | null) => emit('update:fringe3PhaseShift', value ?? 0),
})

const horizontalPaddingPositionModel = computed({
    get: () => props.horizontalPaddingPosition,
    set: (value: 'start' | 'end') => emit('update:horizontalPaddingPosition', value),
})

const horizontalPaddingOptions = computed(() => [
    { value: 'end' as const, label: t('calib.step3HorizontalPaddingEnd') },
    { value: 'start' as const, label: t('calib.step3HorizontalPaddingStart') },
])

const selectedFringeImageIdxModel = computed({
    get: () => props.selectedFringeImageIdx,
    set: (value: number) => emit('update:selectedFringeImageIdx', value),
})

function renderFringePreview(): void {
    const canvas = previewCanvasRef.value
    if (!canvas) return

    const img = props.generatedFringeImages[props.selectedFringeImageIdx]
    const ctx = canvas.getContext('2d')
    if (!ctx || !img) {
        ctx?.clearRect(0, 0, canvas.width, canvas.height)
        return
    }

    const pixels = img.pixels
    const width = canvas.width
    const height = canvas.height
    const imageData = ctx.createImageData(width, height)
    const data = imageData.data

    const isHorizontalFrame = img.index % 2 === 0
    if (!isHorizontalFrame) {
        for (let x = 0; x < width; x++) {
            const gray = pixels[Math.min(x, pixels.length - 1)]
            for (let y = 0; y < height; y++) {
                const index = (y * width + x) * 4
                data[index] = gray
                data[index + 1] = gray
                data[index + 2] = gray
                data[index + 3] = 255
            }
        }
    } else {
        for (let y = 0; y < height; y++) {
            const gray = pixels[Math.min(y, pixels.length - 1)]
            const rowBase = y * width * 4
            for (let x = 0; x < width; x++) {
                const index = rowBase + x * 4
                data[index] = gray
                data[index + 1] = gray
                data[index + 2] = gray
                data[index + 3] = 255
            }
        }
    }

    ctx.putImageData(imageData, 0, 0)
}

watch(
    () => [
        props.selectedFringeImageIdx,
        props.generatedFringeImages,
        props.projectorWidthPixels,
        props.projectorHeightInput,
    ],
    () => {
        void nextTick(() => renderFringePreview())
    },
    { deep: true }
)
</script>

<template>
    <div v-if="props.step3Loading" class="flex flex-1 items-center justify-center">
        <Loader2 class="size-6 animate-spin text-muted-foreground" />
    </div>
    <template v-else>
        <div class="flex flex-1 min-h-0 overflow-hidden">
            <div class="w-72 shrink-0 border-r border-border/40 flex flex-col overflow-y-auto p-4 gap-4">
                <div>
                    <label class="block text-xs text-muted-foreground mb-1.5">
                        {{ t('calib.step3ProjectorSelect') }}
                    </label>
                    <div
                        v-if="props.step3Projectors.length === 0"
                        class="flex items-center gap-1.5 text-xs text-amber-400"
                    >
                        <AlertCircle class="size-3.5 shrink-0" />
                        {{ t('calib.step3NoProjector') }}
                    </div>
                    <Select
                        v-else
                        v-model="selectedProjectorIdModel"
                        :options="props.step3Projectors"
                        option-label="name"
                        option-value="id"
                        size="small"
                        class="w-full !text-xs"
                        :pt="{
                            root: { class: '!py-0 !px-2 !h-7 !flex !items-center' },
                            label: { class: '!text-xs !py-0' },
                        }"
                    />
                </div>

                <div class="rounded-lg border border-border/40 bg-muted/10 p-3 flex flex-col gap-3">
                    <h3 class="text-xs font-semibold text-foreground/80">
                        {{ t('calib.step3FringeSettings') }}
                    </h3>

                    <div>
                        <label class="block text-xs text-muted-foreground mb-1">
                            {{ t('calib.step3FringeMode') }}
                        </label>
                        <div
                            class="h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                        >
                            1-2-1-2（{{ t('calib.step3FringeModeH') }} / {{ t('calib.step3FringeModeV') }}）
                        </div>
                    </div>

                    <div>
                        <label class="block text-xs text-muted-foreground mb-1">
                            {{ t('calib.step3FringeType') }}
                        </label>
                        <div class="flex gap-1.5">
                            <Button
                                :severity="fringeTypeModel === 'bw' ? 'primary' : 'secondary'"
                                size="small"
                                class="!text-xs flex-1"
                                @click="fringeTypeModel = 'bw'"
                            >
                                {{ t('calib.step3FringeTypeBW') }}
                            </Button>
                            <Button
                                :severity="fringeTypeModel === 'wb' ? 'primary' : 'secondary'"
                                size="small"
                                class="!text-xs flex-1"
                                @click="fringeTypeModel = 'wb'"
                            >
                                {{ t('calib.step3FringeTypeWB') }}
                            </Button>
                        </div>
                    </div>

                    <div>
                        <label class="block text-xs text-muted-foreground mb-1">
                            {{ t('calib.step3WidthPixels') }}
                        </label>
                        <div class="flex gap-1.5 items-center">
                            <div
                                class="flex-1 h-7 flex items-center text-xs px-2 rounded border border-border/40 bg-muted/20 text-muted-foreground"
                            >
                                {{ props.projectorWidthPixels != null ? `${props.projectorWidthPixels} px` : '—' }}
                            </div>
                            <Button
                                severity="secondary"
                                outlined
                                size="small"
                                :disabled="!props.selectedProjectorId || props.projectorReading"
                                class="!text-xs shrink-0"
                                @click="void props.fetchProjectorResolution()"
                            >
                                <Loader2 v-if="props.projectorReading" class="size-3 animate-spin mr-1" />
                                {{
                                    props.projectorReading ? t('calib.step3Reading') : t('calib.step3GetFromProjector')
                                }}
                            </Button>
                        </div>
                        <div v-if="props.projectorPixelMode" class="text-[10px] text-muted-foreground mt-1">
                            {{ t('calib.step3PixelMode') }}: {{ props.projectorPixelMode }}
                        </div>
                    </div>

                    <div>
                        <label class="block text-xs text-muted-foreground mb-1">
                            {{ t('calib.step3HeightPixels') }}
                        </label>
                        <InputNumber
                            v-model="projectorHeightInputModel"
                            :min="1"
                            :max="10000"
                            :max-fraction-digits="0"
                            size="small"
                            class="w-full"
                            :input-class="'!text-xs !h-7 !py-0'"
                        />
                    </div>

                    <div>
                        <label class="block text-xs text-muted-foreground mb-1">
                            {{ t('calib.step3PeriodCount') }}
                        </label>
                        <InputNumber
                            v-model="fringe3PeriodCountModel"
                            :min="1"
                            :max="1000"
                            :max-fraction-digits="0"
                            size="small"
                            class="w-full"
                            :input-class="'!text-xs !h-7 !py-0'"
                        />
                        <p v-if="props.fringe3PeriodError" class="text-[10px] text-red-400 mt-1">
                            {{ props.fringe3PeriodError }}
                        </p>
                    </div>

                    <div>
                        <label class="block text-xs text-muted-foreground mb-1">
                            {{ t('calib.step3ImageCount') }}
                        </label>
                        <InputNumber
                            v-model="fringe3ImageCountModel"
                            :min="1"
                            :max="128"
                            :max-fraction-digits="0"
                            size="small"
                            class="w-full"
                            :input-class="'!text-xs !h-7 !py-0'"
                        />
                    </div>

                    <div>
                        <label class="block text-xs text-muted-foreground mb-1">
                            {{ t('calib.step3PhaseShift') }}
                        </label>
                        <InputNumber
                            v-model="fringe3PhaseShiftModel"
                            :min="1"
                            :max="props.fringe3PeriodCount - 1"
                            :max-fraction-digits="0"
                            size="small"
                            class="w-full"
                            :input-class="'!text-xs !h-7 !py-0'"
                        />
                        <p v-if="props.fringe3PhaseError" class="text-[10px] text-red-400 mt-1">
                            {{ props.fringe3PhaseError }}
                        </p>
                    </div>

                    <div>
                        <label class="block text-xs text-muted-foreground mb-1">
                            {{ t('calib.step3HorizontalPaddingPosition') }}
                        </label>
                        <Select
                            v-model="horizontalPaddingPositionModel"
                            :options="horizontalPaddingOptions"
                            option-label="label"
                            option-value="value"
                            size="small"
                            class="w-full !text-xs"
                            :pt="{
                                root: { class: '!py-0 !px-2 !h-7 !flex !items-center' },
                                label: { class: '!text-xs !py-0' },
                            }"
                        />
                        <p class="text-[10px] text-muted-foreground mt-1">
                            {{ t('calib.step3HorizontalPaddingHint') }}
                        </p>
                    </div>
                </div>

                <div class="flex gap-2">
                    <Button
                        size="small"
                        :disabled="!props.selectedProjectorId"
                        class="!text-xs"
                        @click="void props.saveConfig()"
                    >
                        <Save class="size-3 mr-1.5" />
                        {{ t('calib.step3SaveConfig') }}
                    </Button>
                    <Button
                        size="small"
                        :disabled="!props.fringe3CanGenerate || props.generatingFringe"
                        class="flex-1 !text-xs"
                        @click="props.generateFringeImages()"
                    >
                        <Loader2 v-if="props.generatingFringe" class="size-3 animate-spin mr-1.5" />
                        {{ t('calib.step3GenerateImages') }}
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="
                            !props.generatedFringeImages.length ||
                            !props.projectorWidthPixels ||
                            props.downloadingFringe
                        "
                        class="flex-1 !text-xs"
                        @click="void props.triggerFringeDownload()"
                    >
                        <Loader2 v-if="props.downloadingFringe" class="size-3 animate-spin mr-1.5" />
                        {{
                            props.downloadingFringe
                                ? `${t('calib.step3Downloading')} ${props.fringeDownloadProgress}%`
                                : t('calib.step3DownloadImages')
                        }}
                    </Button>
                </div>
            </div>

            <div class="flex flex-1 min-w-0 min-h-0 overflow-hidden">
                <div class="w-28 shrink-0 border-r border-border/40 overflow-y-auto p-2 flex flex-col gap-1">
                    <div class="text-[10px] text-muted-foreground px-1 mb-1">
                        {{ t('calib.step3ImageList') }}
                    </div>
                    <div
                        v-if="props.generatedFringeImages.length === 0"
                        class="text-[10px] text-muted-foreground/60 text-center py-4"
                    >
                        {{ t('calib.step3NoImages') }}
                    </div>
                    <button
                        v-for="img in props.generatedFringeImages"
                        :key="img.index"
                        :class="[
                            'rounded px-2 py-1 text-left text-xs transition-colors',
                            props.selectedFringeImageIdx === img.index
                                ? 'bg-primary/10 text-primary'
                                : 'text-muted-foreground hover:bg-muted/20',
                        ]"
                        @click="selectedFringeImageIdxModel = img.index"
                    >
                        {{ img.label }}
                    </button>
                </div>

                <div class="flex flex-1 flex-col min-w-0 min-h-0 p-4">
                    <div class="text-[10px] text-muted-foreground mb-2">
                        {{ t('calib.step3ImagePreview') }}
                    </div>
                    <div
                        class="flex-1 min-h-0 flex items-center justify-center rounded border border-border/40 bg-black/20"
                    >
                        <canvas
                            v-if="props.generatedFringeImages.length > 0"
                            ref="previewCanvasRef"
                            :width="props.projectorWidthPixels ?? 512"
                            :height="props.projectorHeightInput || 512"
                            class="max-w-full max-h-full"
                            style="image-rendering: pixelated; object-fit: contain"
                        />
                        <div v-else class="text-xs text-muted-foreground/50">
                            {{ t('calib.step3NoImages') }}
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
</template>
