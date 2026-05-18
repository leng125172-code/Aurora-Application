<script setup lang="ts">
import { RouterView } from 'vue-router'
import { Toaster } from '@/components/ui/sonner'
import { AuroraBackground } from '@/components/ui/aurora-background'
</script>

<template>
    <AuroraBackground class="h-screen w-screen overflow-hidden">
        <RouterView />
    </AuroraBackground>
    <Toaster position="top-right" rich-colors close-button />
</template>
