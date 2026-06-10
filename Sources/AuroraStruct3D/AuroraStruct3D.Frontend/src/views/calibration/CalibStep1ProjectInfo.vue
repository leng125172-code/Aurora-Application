<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import { CalibDeviceType, DeviceSeries, type CalibProjectDto } from '@/api/calibration'

const props = defineProps<{
    loading: boolean
    project: CalibProjectDto | null
    deviceSeriesLabel: (series: DeviceSeries) => string
    deviceTypeLabel: (type: CalibDeviceType) => string
}>()

const emit = defineEmits<{
    next: []
}>()

const { t } = useI18n()
</script>

<template>
    <div class="flex flex-col flex-1 min-h-0 overflow-y-auto py-6 p-4">
        <div v-if="props.loading" class="flex items-center justify-center py-12 text-muted-foreground">
            {{ t('common.loading') }}
        </div>
        <div v-else-if="props.project" class="flex flex-col flex-1 min-h-0">
            <div class="flex items-baseline gap-6 pb-2 border-b border-border/50">
                <span class="w-52 shrink-0 text-base font-semibold">
                    {{ t('calib.projectInfo') }}
                </span>
                <span class="flex-1 text-base font-semibold">
                    {{ t('calib.deviceLayout') }}
                </span>
            </div>

            <div class="flex gap-6 flex-1 pt-5">
                <div class="w-52 shrink-0 flex flex-col gap-3">
                    <dl class="flex flex-col gap-3 text-sm">
                        <div>
                            <dt class="text-muted-foreground mb-0.5">
                                {{ t('calib.colName') }}
                            </dt>
                            <dd class="font-medium break-all">{{ props.project.name }}</dd>
                        </div>
                        <div>
                            <dt class="text-muted-foreground mb-0.5">
                                {{ t('calib.colDeviceSeries') }}
                            </dt>
                            <dd class="font-medium">
                                {{ props.deviceSeriesLabel(props.project.deviceSeries) }}
                            </dd>
                        </div>
                        <div>
                            <dt class="text-muted-foreground mb-0.5">
                                {{ t('calib.colDeviceType') }}
                            </dt>
                            <dd class="font-medium">
                                {{ props.deviceTypeLabel(props.project.deviceType) }}
                            </dd>
                        </div>
                        <div>
                            <dt class="text-muted-foreground mb-0.5">
                                {{ t('calib.colCameraCount2') }}
                            </dt>
                            <dd class="font-medium">{{ props.project.cameraCount }} {{ t('calib.cameraUnit') }}</dd>
                        </div>
                        <div>
                            <dt class="text-muted-foreground mb-0.5">
                                {{ t('calib.colProjectorCount') }}
                            </dt>
                            <dd class="font-medium">
                                {{ props.project.projectorCount }} {{ t('calib.projectorUnit') }}
                            </dd>
                        </div>
                    </dl>
                </div>

                <div class="flex-1 flex items-center justify-center min-w-0 min-h-0 self-stretch">
                    <svg
                        v-if="props.project.deviceType === CalibDeviceType.TwoCamera0Light"
                        viewBox="0 0 400 220"
                        class="w-full max-h-72 object-contain"
                        aria-label="2目0光设备布局"
                    >
                        <line
                            x1="100"
                            y1="110"
                            x2="300"
                            y2="110"
                            stroke="currentColor"
                            stroke-width="1.5"
                            stroke-dasharray="6,4"
                            opacity="0.4"
                        />
                        <rect
                            x="60"
                            y="70"
                            width="80"
                            height="80"
                            rx="8"
                            fill="none"
                            stroke="#60a5fa"
                            stroke-width="2"
                        />
                        <circle
                            cx="100"
                            cy="110"
                            r="16"
                            fill="#60a5fa"
                            fill-opacity="0.2"
                            stroke="#60a5fa"
                            stroke-width="1.5"
                        />
                        <circle cx="100" cy="110" r="6" fill="#60a5fa" />
                        <text x="100" y="175" text-anchor="middle" font-size="12" fill="currentColor" opacity="0.8">
                            主相机（左）
                        </text>
                        <rect
                            x="260"
                            y="70"
                            width="80"
                            height="80"
                            rx="8"
                            fill="none"
                            stroke="#34d399"
                            stroke-width="2"
                        />
                        <circle
                            cx="300"
                            cy="110"
                            r="16"
                            fill="#34d399"
                            fill-opacity="0.2"
                            stroke="#34d399"
                            stroke-width="1.5"
                        />
                        <circle cx="300" cy="110" r="6" fill="#34d399" />
                        <text x="300" y="175" text-anchor="middle" font-size="12" fill="currentColor" opacity="0.8">
                            从相机（右）
                        </text>
                        <text x="200" y="105" text-anchor="middle" font-size="10" fill="currentColor" opacity="0.5">
                            基线
                        </text>
                    </svg>

                    <svg
                        v-else-if="props.project.deviceType === CalibDeviceType.OneCamera1Light"
                        viewBox="0 0 400 220"
                        class="w-full max-h-72 object-contain"
                        aria-label="1目1光设备布局"
                    >
                        <polygon
                            points="200,60 220,130 180,130"
                            fill="#f59e0b"
                            fill-opacity="0.25"
                            stroke="#f59e0b"
                            stroke-width="2"
                        />
                        <rect
                            x="180"
                            y="38"
                            width="40"
                            height="30"
                            rx="5"
                            fill="none"
                            stroke="#f59e0b"
                            stroke-width="2"
                        />
                        <text x="200" y="160" text-anchor="middle" font-size="12" fill="currentColor" opacity="0.8">
                            主结构光（中心）
                        </text>
                        <line
                            x1="100"
                            y1="80"
                            x2="180"
                            y2="80"
                            stroke="currentColor"
                            stroke-width="1.5"
                            stroke-dasharray="5,4"
                            opacity="0.4"
                        />
                        <rect
                            x="36"
                            y="50"
                            width="80"
                            height="72"
                            rx="8"
                            fill="none"
                            stroke="#60a5fa"
                            stroke-width="2"
                        />
                        <circle
                            cx="76"
                            cy="86"
                            r="16"
                            fill="#60a5fa"
                            fill-opacity="0.2"
                            stroke="#60a5fa"
                            stroke-width="1.5"
                        />
                        <circle cx="76" cy="86" r="6" fill="#60a5fa" />
                        <text x="76" y="145" text-anchor="middle" font-size="12" fill="currentColor" opacity="0.8">
                            主相机
                        </text>
                    </svg>

                    <svg
                        v-else-if="props.project.deviceType === CalibDeviceType.TwoCamera1Light"
                        viewBox="0 0 400 220"
                        class="w-full max-h-72 object-contain"
                        aria-label="2目1光设备布局"
                    >
                        <line
                            x1="80"
                            y1="110"
                            x2="320"
                            y2="110"
                            stroke="currentColor"
                            stroke-width="1.5"
                            stroke-dasharray="6,4"
                            opacity="0.35"
                        />
                        <rect
                            x="36"
                            y="70"
                            width="80"
                            height="80"
                            rx="8"
                            fill="none"
                            stroke="#60a5fa"
                            stroke-width="2"
                        />
                        <circle
                            cx="76"
                            cy="110"
                            r="15"
                            fill="#60a5fa"
                            fill-opacity="0.2"
                            stroke="#60a5fa"
                            stroke-width="1.5"
                        />
                        <circle cx="76" cy="110" r="5.5" fill="#60a5fa" />
                        <text x="76" y="175" text-anchor="middle" font-size="11" fill="currentColor" opacity="0.8">
                            主相机（左）
                        </text>
                        <polygon
                            points="200,72 222,128 178,128"
                            fill="#f59e0b"
                            fill-opacity="0.25"
                            stroke="#f59e0b"
                            stroke-width="2"
                        />
                        <rect
                            x="178"
                            y="50"
                            width="44"
                            height="30"
                            rx="5"
                            fill="none"
                            stroke="#f59e0b"
                            stroke-width="2"
                        />
                        <text x="200" y="155" text-anchor="middle" font-size="11" fill="currentColor" opacity="0.8">
                            主结构光（中心）
                        </text>
                        <rect
                            x="284"
                            y="70"
                            width="80"
                            height="80"
                            rx="8"
                            fill="none"
                            stroke="#34d399"
                            stroke-width="2"
                        />
                        <circle
                            cx="324"
                            cy="110"
                            r="15"
                            fill="#34d399"
                            fill-opacity="0.2"
                            stroke="#34d399"
                            stroke-width="1.5"
                        />
                        <circle cx="324" cy="110" r="5.5" fill="#34d399" />
                        <text x="324" y="175" text-anchor="middle" font-size="11" fill="currentColor" opacity="0.8">
                            从相机（右）
                        </text>
                    </svg>
                </div>
            </div>
        </div>

        <div class="flex justify-end gap-2 px-4 py-3 border-t border-border/40 mt-auto">
            <Button size="small" :disabled="!props.project" @click="emit('next')">
                {{ t('calib.nextStep') }}
            </Button>
        </div>
    </div>
</template>
