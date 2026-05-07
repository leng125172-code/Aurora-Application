<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'

const router = useRouter()
const { t } = useI18n()
</script>

<template>
    <div class="flex min-h-screen flex-col items-center justify-center gap-4 bg-background px-4 text-center">
        <div class="text-7xl font-bold text-muted-foreground">404</div>
        <div class="text-xl font-semibold">{{ t('error.notFound') }}</div>
        <p class="text-sm text-muted-foreground">{{ t('error.notFoundDesc') }}</p>
        <Button @click="router.push('/')">{{ t('error.backHome') }}</Button>
    </div>
</template>
