<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { RouterView } from 'vue-router'
import { Toaster } from '@/components/ui/sonner'
import { AuroraBackground } from '@/components/ui/aurora-background'
import { useDeviceStateStore } from '@/stores/deviceState'

const deviceStateStore = useDeviceStateStore()

// 应用启动时连接设备状态 Hub（匿名，无 token 也可连接）
onMounted(() => {
    void deviceStateStore.connectAsync()
})

// 应用卸载时断开 Hub 连接
onUnmounted(() => {
    void deviceStateStore.disconnectAsync()
})
</script>

<template>
    <AuroraBackground class="h-screen w-screen overflow-hidden">
        <RouterView />
    </AuroraBackground>
    <Toaster position="top-right" rich-colors close-button />
</template>
