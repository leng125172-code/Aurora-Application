<script setup lang="ts">
/**
 * 项目级卡片封装
 *
 * 视觉契约：
 *  - 基于 PrimeVue <Card>，外层 wrapper 承载 Inspira BorderBeam 光束装饰
 *  - 默认开启 BorderBeam（可通过 :beam="false" 关闭，例如嵌套卡片场景）
 *  - BorderBeam 参数通过 beamSize / beamDuration / beamDelay 暴露，便于多卡错峰
 *  - 保留原 Aurora 视觉：玻璃态背景 + 圆角 + 边框光束
 *
 * 槽位与 PrimeVue Card 一致：header / title / subtitle / content / footer
 * 业务侧未提供 content 槽时，将默认 slot 内容渲染到 content 区域，
 * 便于"只想要个外层壳"的简单场景。
 */
import { useAttrs, computed, type HTMLAttributes } from 'vue'
import PrimeCard from 'primevue/card'
import { BorderBeam } from '@/components/ui/border-beam'

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
    beam: true,
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
    <!-- 外层 wrapper：承载 BorderBeam 并锁定圆角与 overflow，使光束沿边框运行 -->
    <div :class="['relative rounded-xl overflow-hidden', props.class]" v-bind="restAttrs">
        <BorderBeam
            v-if="beam"
            :size="beamSize"
            :duration="beamDuration"
            :delay="beamDelay"
            :color-from="beamColorFrom"
            :color-to="beamColorTo"
        />
        <!-- PrimeVue Card：透传 pt 让根容器贴合玻璃态背景；body 取消默认 padding，由业务侧 #content 控制 -->
        <PrimeCard
            :pt="{
                root: { class: 'bg-card/40 backdrop-blur border-0 shadow-sm rounded-xl' },
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
    </div>
</template>
