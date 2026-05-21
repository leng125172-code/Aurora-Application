<script setup lang="ts">
import ThemeToggle from '@/components/ThemeToggle.vue'
import LangSwitcher from '@/components/LangSwitcher.vue'
import DeviceStatusBadge from '@/components/device-state/DeviceStatusBadge.vue'
import FaultBanner from '@/components/device-state/FaultBanner.vue'
import { useDeviceStateStore } from '@/stores/deviceState'

const deviceStateStore = useDeviceStateStore()
</script>

<template>
    <div class="flex flex-col">
        <!-- 第一层：Logo + 状态徽章 + 工具栏 -->
        <header class="flex h-14 items-center justify-between border-b bg-card/40 px-4 backdrop-blur">
            <div class="flex items-center gap-3">
                <span class="text-base font-semibold">AuroraStruct3D</span>
                <DeviceStatusBadge :status="deviceStateStore.state?.status" />
            </div>
            <div class="flex items-center gap-2">
                <LangSwitcher />
                <ThemeToggle />
            </div>
        </header>
        <!-- 第二层：故障横幅（有故障时才显示） -->
        <FaultBanner :fault="deviceStateStore.currentFault" />
    </div>
</template>
