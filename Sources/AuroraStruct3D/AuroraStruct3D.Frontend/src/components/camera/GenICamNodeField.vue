<script setup lang="ts">
// 动态渲染单个 GenICam 节点：支持 Integer / Float / Enumeration / Boolean / String / Command
// 内联编辑模式，写入成功后会回调 emit('updated')，由父组件决定是否刷新依赖节点。
import { computed, ref, watch } from 'vue'
import type { GenICamNodeDto } from '@/api/cameras'
import { useCameraStore } from '@/stores/cameras'
import { useAppToast } from '@/composables/useAppToast'

const props = defineProps<{
    /** 相机 ID */
    cameraId: string
    /** 节点完整元信息 */
    node: GenICamNodeDto
    /** 当前实时值（来自 store.nodeValuesByName 或节点 currentValue 快照） */
    value: string | null
    /** 是否禁用（如相机未打开） */
    disabled?: boolean
}>()

const emit = defineEmits<{
    /** 节点值已成功写入，父组件可据此触发依赖节点局部刷新 */
    (e: 'updated', node: GenICamNodeDto, newValue: string): void
}>()

const store = useCameraStore()
const toast = useAppToast()

// ─── 节点能力判定 ──────────────────────────────────────────────────────────
const isCommand = computed(() => props.node.nodeType === 'Command')
const isEnum = computed(() => props.node.nodeType === 'Enumeration')
const isBool = computed(() => props.node.nodeType === 'Boolean')
const isInt = computed(() => props.node.nodeType === 'Integer')
const isFloat = computed(() => props.node.nodeType === 'Float')
const isString = computed(() => props.node.nodeType === 'String')

const canWrite = computed(
    () =>
        !props.disabled &&
        !props.node.isLocked &&
        (props.node.access === 'ReadWrite' || props.node.access === 'WriteOnly')
)

const apiDataType = computed<string>(() => {
    if (isFloat.value) return 'float'
    if (isString.value) return 'string'
    return 'int'
})

// ─── 内联编辑 ──────────────────────────────────────────────────────────────
const editing = ref(false)
const editingValue = ref<string>('')
const busy = ref(false)

watch(
    () => props.value,
    () => {
        // Bug 3：<input type="number"> + v-model 会把 ref 内部值 cast 为 number，
        // 强制转 String 保证后续发送给后端是字符串、避免 JSON 反序列化失败。
        if (!editing.value) editingValue.value = String(props.value ?? '')
    },
    { immediate: true }
)

function startEdit() {
    if (!canWrite.value) return
    editingValue.value = String(props.value ?? '')
    editing.value = true
}

function cancelEdit() {
    editing.value = false
    editingValue.value = String(props.value ?? '')
}

async function saveEdit() {
    if (!editingValue.value && !isString.value && !isBool.value) {
        toast.warning('请输入有效值')
        return
    }
    // Bug 3：强制转字符串后再发送，避免 <input type="number"> 让 v-model 变成 number。
    const payload = String(editingValue.value ?? '')
    busy.value = true
    try {
        await store.setGenICamParam(props.cameraId, {
            nodeName: props.node.nodeName,
            dataType: apiDataType.value,
            value: payload,
        })
        editing.value = false
        emit('updated', props.node, payload)
        toast.success(`${props.node.displayName} 已保存`)
    } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e)
        toast.error(`保存失败：${msg}`)
    } finally {
        busy.value = false
    }
}

async function executeCommand() {
    busy.value = true
    try {
        await store.executeGenICamCommand(props.cameraId, props.node.nodeName)
        emit('updated', props.node, '')
        toast.success(`命令 ${props.node.displayName} 已执行`)
    } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e)
        toast.error(`执行失败：${msg}`)
    } finally {
        busy.value = false
    }
}

// ─── 显示格式化 ────────────────────────────────────────────────────────────
const displayValue = computed<string>(() => {
    if (props.value == null) return '—'
    if (isEnum.value) {
        const v = parseInt(props.value, 10)
        const entry = props.node.enumEntries.find((e) => e.value === v)
        return entry?.displayName ?? props.value
    }
    if (isBool.value) {
        return props.value === '1' || props.value === 'true' ? '开启' : '关闭'
    }
    if (props.node.unit) return `${props.value} ${props.node.unit}`
    return props.value
})
</script>

<template>
    <div class="flex min-h-[2rem] items-center gap-2 px-3 py-1 text-xs">
        <!-- 节点标签 -->
        <div class="w-44 shrink-0 truncate text-muted-foreground" :title="node.description || node.displayName">
            {{ node.displayName }}
        </div>

        <!-- 值区域 -->
        <div class="flex flex-1 items-center gap-1 overflow-hidden">
            <!-- Command 节点：仅执行按钮 -->
            <template v-if="isCommand">
                <button
                    :disabled="disabled || busy"
                    class="rounded border px-2 py-0.5 text-xs hover:bg-muted/50 disabled:opacity-40"
                    @click="void executeCommand()"
                >
                    {{ busy ? '执行中…' : '执行' }}
                </button>
            </template>

            <!-- 编辑模式 -->
            <template v-else-if="editing">
                <!-- 枚举 -->
                <select
                    v-if="isEnum"
                    v-model="editingValue"
                    class="flex-1 rounded border px-1 py-0.5 text-xs focus:outline-none"
                >
                    <option
                        v-for="entry in node.enumEntries.filter((e) => e.isAvailable)"
                        :key="entry.value"
                        :value="String(entry.value)"
                    >
                        {{ entry.displayName }}
                    </option>
                </select>
                <!-- 布尔 -->
                <select
                    v-else-if="isBool"
                    v-model="editingValue"
                    class="flex-1 rounded border px-1 py-0.5 text-xs focus:outline-none"
                >
                    <option value="1">开启</option>
                    <option value="0">关闭</option>
                </select>
                <!-- 整数 -->
                <input
                    v-else-if="isInt"
                    v-model="editingValue"
                    type="number"
                    :min="node.intMin"
                    :max="node.intMax"
                    :step="node.intStep || 1"
                    class="w-32 rounded border px-1 py-0.5 text-xs focus:outline-none"
                />
                <!-- 浮点 -->
                <input
                    v-else-if="isFloat"
                    v-model="editingValue"
                    type="number"
                    :min="node.floatMin"
                    :max="node.floatMax"
                    :step="node.floatStep || 0.01"
                    class="w-32 rounded border px-1 py-0.5 text-xs focus:outline-none"
                />
                <!-- 字符串 -->
                <input
                    v-else
                    v-model="editingValue"
                    type="text"
                    class="flex-1 rounded border px-1 py-0.5 text-xs focus:outline-none"
                />
                <button
                    :disabled="busy"
                    class="shrink-0 rounded border px-1.5 py-0.5 text-xs text-green-600 hover:bg-green-50 disabled:opacity-40"
                    @click="void saveEdit()"
                >
                    ✓
                </button>
                <button
                    class="shrink-0 rounded border px-1.5 py-0.5 text-xs text-red-500 hover:bg-red-50"
                    @click="cancelEdit"
                >
                    ✗
                </button>
            </template>

            <!-- 只读显示 -->
            <template v-else>
                <span class="flex-1 truncate font-mono" :title="String(value ?? '')">
                    {{ displayValue }}
                </span>
                <button
                    v-if="canWrite"
                    class="ml-1 shrink-0 rounded border px-1.5 py-0.5 text-xs hover:bg-muted/50"
                    @click="startEdit"
                >
                    编辑
                </button>
            </template>
        </div>

        <!-- 访问模式徽章 -->
        <span
            :class="[
                'shrink-0 rounded px-1 py-0 text-[10px]',
                node.access === 'ReadOnly'
                    ? 'bg-slate-100 text-slate-400'
                    : node.access === 'ReadWrite'
                      ? 'bg-blue-50 text-blue-400'
                      : 'bg-amber-50 text-amber-500',
            ]"
        >
            {{ node.access === 'ReadWrite' ? 'RW' : node.access === 'ReadOnly' ? 'RO' : node.access }}
        </span>
    </div>
</template>
