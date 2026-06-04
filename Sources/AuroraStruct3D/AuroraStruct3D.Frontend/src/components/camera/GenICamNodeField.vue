<script setup lang="ts">
// 动态渲染单个 GenICam 节点：支持 Integer / Float / Enumeration / Boolean / String / Command
// 内联编辑模式，写入成功后会回调 emit('updated')，由父组件决定是否刷新依赖节点。
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { GenICamNodeDto } from '@/api/cameras'
import { useCameraStore } from '@/stores/cameras'
import { useAppToast } from '@/composables/useAppToast'
import Select from 'primevue/select'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'

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
const { t } = useI18n()

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
        toast.warning(t('camera.nodePleaseInputValid'))
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
        toast.success(t('camera.nodeSaveSuccess', { name: props.node.displayName }))
    } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e)
        toast.error(t('camera.nodeSaveFailed', { msg }))
    } finally {
        busy.value = false
    }
}

async function executeCommand() {
    busy.value = true
    try {
        await store.executeGenICamCommand(props.cameraId, props.node.nodeName)
        emit('updated', props.node, '')
        toast.success(t('camera.nodeExecuteSuccess', { name: props.node.displayName }))
    } catch (e: unknown) {
        const msg = e instanceof Error ? e.message : String(e)
        toast.error(t('camera.nodeExecuteFailed', { msg }))
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
        return props.value === '1' || props.value === 'true' ? t('camera.nodeBoolOn') : t('camera.nodeBoolOff')
    }
    if (props.node.unit) return `${props.value} ${props.node.unit}`
    return props.value
})

/** 枚举可选项（每项增加 valueStr 字符串用于 Select option-value） */
const enumOptions = computed(() =>
    props.node.enumEntries.filter((e) => e.isAvailable).map((e) => ({ ...e, valueStr: String(e.value) }))
)
/** 布尔可选项 */
const boolOptions = computed(() => [
    { label: t('camera.nodeBoolOn'), value: '1' },
    { label: t('camera.nodeBoolOff'), value: '0' },
])
/** InputNumber 双向绑定桥（PrimeVue InputNumber 使用 number | null，editingValue 保持 string） */
const editingValueNum = computed<number | null>({
    get: () => (editingValue.value !== '' ? Number(editingValue.value) : null),
    set: (v: number | null) => {
        editingValue.value = v != null ? String(v) : ''
    },
})
</script>

<template>
    <!-- 外层：flex-wrap 响应式；宽时单行，窄时 key+badge 一行、值换行对齐 -->
    <div class="flex min-h-[2rem] flex-wrap items-center gap-x-2 gap-y-0.5 px-3 py-1 text-xs">
        <!-- 节点标签 + 访问模式徽章（始终在同一行） -->
        <div class="flex shrink-0 items-center gap-1.5" style="min-width: 11rem; max-width: 11rem">
            <div class="flex-1 truncate text-muted-foreground" :title="node.description || node.displayName">
                {{ node.displayName }}
            </div>
            <span
                :class="[
                    'shrink-0 rounded px-1 py-0 text-[10px]',
                    node.access === 'ReadOnly'
                        ? 'bg-slate-500/20 text-slate-400'
                        : node.access === 'ReadWrite'
                          ? 'bg-blue-500/20 text-blue-400'
                          : 'bg-amber-500/20 text-amber-400',
                ]"
            >
                {{ node.access === 'ReadWrite' ? 'RW' : node.access === 'ReadOnly' ? 'RO' : node.access }}
            </span>
        </div>

        <!-- 值区域：flex-1 占满剩余空间，换行时与标签列左对齐 -->
        <div class="flex min-w-0 flex-1 items-center gap-1 overflow-hidden" style="min-width: 8rem">
            <!-- Command 节点：仅执行按钮 -->
            <template v-if="isCommand">
                <Button
                    :disabled="disabled || busy"
                    size="small"
                    severity="secondary"
                    outlined
                    class="!text-xs !h-7"
                    @click="void executeCommand()"
                >
                    {{ busy ? t('camera.nodeExecuting') : t('camera.nodeExecute') }}
                </Button>
            </template>

            <!-- 编辑模式 -->
            <template v-else-if="editing">
                <!-- 枚举 Select -->
                <Select
                    v-if="isEnum"
                    v-model="editingValue"
                    :options="enumOptions"
                    option-label="displayName"
                    option-value="valueStr"
                    size="small"
                    class="flex-1 min-w-0"
                    :pt="{
                        root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                        label: {
                            class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                        },
                        dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                    }"
                />
                <!-- 布尔 Select -->
                <Select
                    v-else-if="isBool"
                    v-model="editingValue"
                    :options="boolOptions"
                    option-label="label"
                    option-value="value"
                    size="small"
                    class="w-24"
                    :pt="{
                        root: { class: '!h-7 !py-0' },
                        label: { class: '!text-xs !py-0 !leading-none' },
                    }"
                />
                <!-- 整数 InputNumber -->
                <InputNumber
                    v-else-if="isInt"
                    v-model="editingValueNum"
                    :min="node.intMin ?? undefined"
                    :max="node.intMax ?? undefined"
                    :step="node.intStep ?? 1"
                    :max-fraction-digits="0"
                    size="small"
                    class="w-32"
                    :input-class="'!text-xs !h-7 !py-0 !px-2'"
                />
                <!-- 浮点 InputNumber -->
                <InputNumber
                    v-else-if="isFloat"
                    v-model="editingValueNum"
                    :min="node.floatMin ?? undefined"
                    :max="node.floatMax ?? undefined"
                    :step="node.floatStep ?? 0.01"
                    :max-fraction-digits="6"
                    size="small"
                    class="w-32"
                    :input-class="'!text-xs !h-7 !py-0 !px-2'"
                />
                <!-- 字符串 InputText -->
                <InputText v-else v-model="editingValue" size="small" class="flex-1 !text-xs !h-7 !py-0" />
                <!-- 确认 -->
                <Button
                    severity="success"
                    size="small"
                    :disabled="busy"
                    class="!px-2 !py-0 !h-7"
                    @click="void saveEdit()"
                >
                    ✓
                </Button>
                <!-- 取消 -->
                <Button severity="danger" size="small" class="!px-2 !py-0 !h-7" @click="cancelEdit">✗</Button>
            </template>

            <!-- 只读显示 -->
            <template v-else>
                <span class="flex-1 truncate font-mono" :title="String(value ?? '')">
                    {{ displayValue }}
                </span>
                <Button
                    v-if="canWrite"
                    text
                    severity="secondary"
                    size="small"
                    class="ml-1 shrink-0 !text-xs"
                    @click="startEdit"
                >
                    {{ t('common.edit') }}
                </Button>
            </template>
        </div>
    </div>
</template>
