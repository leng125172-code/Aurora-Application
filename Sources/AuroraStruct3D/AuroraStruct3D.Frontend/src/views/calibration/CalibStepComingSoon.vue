<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'

const props = defineProps<{
    step: string
    showNext: boolean
}>()

const emit = defineEmits<{
    prev: []
    next: []
}>()

const { t } = useI18n()
</script>

<template>
    <div class="flex flex-1 flex-col items-center justify-center py-16 text-muted-foreground">
        <span class="text-4xl mb-3 opacity-30">🚧</span>
        <p class="text-sm">{{ t('calib.stepComingSoon') }}</p>
    </div>
    <div class="flex justify-between gap-2 px-4 py-3 border-t border-border/40 mt-auto">
        <Button severity="secondary" outlined size="small" @click="emit('prev')">
            {{ t('calib.prevStep') }}
        </Button>
        <Button v-if="props.showNext" size="small" @click="emit('next')">
            {{ t('calib.nextStep') }}
        </Button>
    </div>
</template>
