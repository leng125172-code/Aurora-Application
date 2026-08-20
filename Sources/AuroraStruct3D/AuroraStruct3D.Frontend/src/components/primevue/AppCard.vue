<script setup lang="ts">
/**
 * 项目级卡片封装
 *
 * 视觉契约：
 *  - 基于 PrimeVue <Card>，外层 wrapper 承载 Inspira BorderBeam 光束装饰
 *  - 工业控制台默认关闭装饰光束；品牌展示卡可通过 :beam="true" 开启
 *  - BorderBeam 参数通过 beamSize / beamDuration / beamDelay 暴露，便于多卡错峰
 *  - 保留原 Aurora 视觉：玻璃态背景 + 圆角 + 边框光束
 *
 * 槽位与 PrimeVue Card 一致：header / title / subtitle / content / footer
 * 业务侧未提供 content 槽时，将默认 slot 内容渲染到 content 区域，
 * 便于"只想要个外层壳"的简单场景。
 */
import { useAttrs, computed, type HTMLAttributes } from 'vue'
import PrimeCard from 'primevue/card'
import BorderBeam from '@/components/ui/border-beam/BorderBeam.vue'

interface AppCardProps {
    /** 是否显示 BorderBeam 光束动画（默认 true，保持 Aurora 视觉一致性） */
    beam?: boolean
    /** BorderBeam size 参数（光束矩形宽度） */
    beamSize?: number
    /** BorderBeam duration 参数 */
    beamDuration?: number
    /** BorderBeam delay 参数（多卡错峰时使用） */
    beamDelay?: number
    /** BorderBeam colorFrom 参数 */
    beamColorFrom?: string
    /** BorderBeam colorTo 参数 */
    beamColorTo?: string
    /** 外层 wrapper 自定义 class（如尺寸、布局），等价于 Card 在 shadcn 时代的 class 用法 */
    class?: HTMLAttributes['class']
}

const props = withDefaults(defineProps<AppCardProps>(), {
    beam: false,
    beamSize: 120,
    beamDuration: 10,
    beamDelay: 0,
    beamColorFrom: '#ffaa40',
    beamColorTo: '#9c40ff',
})

// 透传 attrs（class 显式提取到根 wrapper，避免被 PrimeVue Card 重复消费）
const attrs = useAttrs()

const restAttrs = computed(() => {
    const { class: _omit, ...rest } = attrs as Record<string, unknown> & { class?: unknown }
    return rest
})
</script>

<template>
    <!-- 外层 wrapper：overflow-hidden 阻止 BorderBeam::after 的布局溢出触发滚动条；clip-path 保留以裁切视觉边界 -->
    <div
        data-testid="app-card"
        :class="['relative min-w-0 overflow-hidden rounded-xl', props.class]"
        style="clip-path: inset(0 round 0.75rem)"
        v-bind="restAttrs"
    >
        <!-- PrimeVue Card：承载边框、玻璃态背景；自身 overflow-hidden 裁切 Card 内容到圆角范围 -->
        <PrimeCard
            :pt="{
                root: { class: 'bg-card/90 backdrop-blur border border-border shadow-sm rounded-xl overflow-hidden' },
                body: { class: '!p-0' },
                caption: { class: 'p-4 pb-0' },
                content: { class: 'p-4' },
                footer: { class: 'p-4 pt-0' },
            }"
        >
            <template v-if="$slots.header" #header>
                <slot name="header" />
            </template>
            <template v-if="$slots.title" #title>
                <slot name="title" />
            </template>
            <template v-if="$slots.subtitle" #subtitle>
                <slot name="subtitle" />
            </template>
            <template #content>
                <slot name="content">
                    <slot />
                </slot>
            </template>
            <template v-if="$slots.footer" #footer>
                <slot name="footer" />
            </template>
        </PrimeCard>
        <!-- BorderBeam 放在 PrimeCard 之后，z 轴在上方，光束才能覆盖并显示在边框位置 -->
        <BorderBeam
            v-if="beam"
            :size="beamSize"
            :duration="beamDuration"
            :delay="beamDelay"
            :color-from="beamColorFrom"
            :color-to="beamColorTo"
        />
    </div>
</template>
