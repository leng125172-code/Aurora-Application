<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import * as monaco from 'monaco-editor'
import * as signalR from '@microsoft/signalr'
import { VueFlow, useVueFlow, type Edge, type Node } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import {
    completions,
    continueDebug,
    debugAndRunSource,
    debugSource,
    diagnostics,
    definition,
    formatSource,
    hover,
    getWorkflowNodePalette,
    getBreakpoints,
    getDebugSessions,
    getDebugResult,
    getDebugStatus,
    getPerformance,
    getStack,
    getTrace,
    getWorkflowSource,
    listProjects,
    listWorkflows,
    patchGraph,
    pauseDebug,
    references,
    semanticTokens,
    mapPosition,
    renameSymbol,
    previewWorkflowMigration,
    startWorkflowMigration,
    retryWorkflowMigration,
    rollbackWorkflowMigration,
    runTo,
    saveWorkflowSource,
    setBreakpoints,
    signatureHelp,
    step,
    stepInto,
    stepOut,
    stepOver,
    stopDebug,
    watch as evaluateWatch,
    type DebugStatus,
    type DebugExecutionResult,
    type IdeDiagnostic,
    type ProjectBrief,
    type WorkflowBrief,
    type WorkflowGraph,
    type WorkflowSource,
    type WorkflowNodeDefinition,
    type WorkflowMigrationBatch,
    type WorkflowMigrationItem,
} from '@/api/workflow-ide'
import { useAuthStore } from '@/stores/auth'
import { Bug, CircleStop, Code2, GitBranch, Play, Save, StepForward, WandSparkles } from '@lucide/vue'
import WorkflowBlobPreview from '@/components/workflow/WorkflowBlobPreview.vue'
import WorkflowJsonTree from '@/components/workflow/WorkflowJsonTree.vue'
import {
    classifyWorkflowResult,
    workflowResultFileName,
    type WorkflowResultPresentation,
} from '@/utils/workflow-result'
import { useAppToast } from '@/composables/useAppToast'
import { useAppConfirm } from '@/composables/useAppConfirm'
import {
    applyProjectToDevice,
    deleteWorkflowPlcTrigger,
    getProjectApplicationStatus,
    getProjectDeploymentHistory,
    getProjectTasks,
    getWorkflow as getRuntimeWorkflow,
    getWorkflowPlcHandshake,
    getWorkflowPlcHandshakeStatus,
    getWorkflowPlcTriggers,
    resetWorkflowPlcHandshake,
    rollbackProjectDeployment,
    saveWorkflowPlcHandshake,
    saveWorkflowPlcTrigger,
    type ProjectDeployment,
    type ProjectApplicationStatus,
    type ProjectTaskBatch,
    type SaveWorkflowPlcHandshakeConfig,
    type WorkflowPlcHandshakeStatus,
    type SaveWorkflowPlcTrigger,
    type WorkflowPlcTrigger,
} from '@/api/workflow'
import {
    browsePlcTree,
    getPlcTags,
    getPlcs,
    readPlcTags,
    writePlcTag,
    PlcTagAccess,
    PlcTagDataType,
    type PlcBrowseTreeNode,
    type PlcDeviceDto,
    type PlcTagDto,
    type PlcTagValueDto,
} from '@/api/plcs'

self.MonacoEnvironment = {
    getWorker: () => new Worker(new URL('../../workers/monaco-editor.worker.ts', import.meta.url), { type: 'module' }),
}

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const toast = useAppToast()
const confirmAction = useAppConfirm()
const projectId = ref(String(route.query.projectId ?? ''))
const projectFilter = ref('')
const projects = ref<ProjectBrief[]>([])
const workflowId = ref(String(route.query.workflowId ?? ''))
const workflows = ref<WorkflowBrief[]>([])
const source = ref<WorkflowSource | null>(null)
const documentVersion = ref(0)
const activeView = ref<'split' | 'graph' | 'source' | 'json'>('split')
const problems = ref<IdeDiagnostic[]>([])
const selectedNodeId = ref<string>()
const selectedPort = ref<{ name: string; direction: string }>()
const dirty = ref(false)
const saving = ref(false)
const migrationBusy = ref(false)
const migrationItems = ref<WorkflowMigrationItem[]>([])
const migrationBatch = ref<WorkflowMigrationBatch>()
const migrationPanelOpen = ref(false)
const debugStatus = ref<DebugStatus>()
const debugResults = ref<DebugExecutionResult[]>([])
const previewResult = ref<DebugExecutionResult>()
const executionId = ref('')
const debugActivity = ref<'idle' | 'starting' | 'running' | 'stopped'>('idle')
const breakpointNodes = ref<string[]>([])
const completedNodeIds = ref<string[]>([])
const selectedHasBreakpoint = computed(
    () => !!selectedNodeId.value && breakpointNodes.value.includes(selectedNodeId.value)
)
const effectiveDebugState = computed(() => {
    if (debugActivity.value === 'starting') return 'starting'
    if (debugActivity.value === 'running') return 'running'
    if (debugActivity.value === 'stopped') return 'stopped'
    return debugStatus.value?.debugState ?? (executionId.value ? 'paused' : 'not-started')
})
const debugStateInfo = computed(() => {
    const states: Record<string, { label: string; className: string }> = {
        'not-started': { label: '未调试', className: 'debug-state-idle' },
        starting: { label: '正在启动', className: 'debug-state-running' },
        ready: { label: '已就绪（首节点前）', className: 'debug-state-paused' },
        running: { label: '正在执行', className: 'debug-state-running' },
        paused: { label: '已暂停', className: 'debug-state-paused' },
        completed: { label: '执行完成', className: 'debug-state-completed' },
        faulted: { label: '执行失败', className: 'debug-state-faulted' },
        stopped: { label: '已停止', className: 'debug-state-stopped' },
    }
    return states[effectiveDebugState.value] ?? states['not-started']!
})
const canContinueDebug = computed(
    () =>
        !!executionId.value &&
        debugActivity.value === 'idle' &&
        (debugStatus.value?.canContinue ?? !debugStatus.value?.isTerminal)
)
const canStepDebug = computed(
    () =>
        !!executionId.value &&
        debugActivity.value === 'idle' &&
        (debugStatus.value?.canStep ?? !debugStatus.value?.isTerminal)
)
const canPauseDebug = computed(() => !!executionId.value && effectiveDebugState.value === 'running')
const canStopDebug = computed(
    () => !!executionId.value && (debugStatus.value?.canStop ?? !debugStatus.value?.isTerminal)
)
const watchExpressions = ref<string[]>(['retstatus.status'])
const watchResults = ref<Array<Record<string, unknown>>>([])
const traceResults = ref<Array<Record<string, unknown>>>([])
const stackResults = ref<Array<Record<string, unknown>>>([])
const performanceResults = ref<Array<Record<string, unknown>>>([])
const bottomTab = ref<'problems' | 'variables' | 'watch' | 'trace' | 'performance' | 'results' | 'output'>('problems')
const output = ref<string[]>([])
const statusReceivedAt = ref(Date.now())
const clockNow = ref(Date.now())
const displayDurationMs = computed(
    () =>
        (debugStatus.value?.durationMs ?? 0) +
        (effectiveDebugState.value === 'running' ? Math.max(0, clockNow.value - statusReceivedAt.value) : 0)
)
const displayCurrentNodeDurationMs = computed(
    () =>
        (debugStatus.value?.currentNodeDurationMs ?? 0) +
        (effectiveDebugState.value === 'running' ? Math.max(0, clockNow.value - statusReceivedAt.value) : 0)
)
const primaryDebugLabel = computed(() => (executionId.value && !debugStatus.value?.isTerminal ? '继续' : '调试运行'))
const canPrimaryDebug = computed(
    () =>
        debugActivity.value !== 'starting' &&
        debugActivity.value !== 'running' &&
        (!executionId.value || !!debugStatus.value?.isTerminal || (debugStatus.value?.canContinue ?? false))
)
const editorHost = ref<HTMLElement>()
const jsonText = ref('')
const plcTriggerDrawerOpen = ref(false)
const taskDrawerOpen = ref(false)
const operatorDocsOpen = ref(false)
const operatorDocsLoading = ref(false)
const operatorDocsSearch = ref('')
const operatorDefinitions = ref<WorkflowNodeDefinition[]>([])
const selectedOperator = ref<WorkflowNodeDefinition>()
const filteredOperatorDefinitions = computed(() => {
    const keyword = operatorDocsSearch.value.trim().toLocaleLowerCase()
    return operatorDefinitions.value.filter(
        (x) =>
            x.nodeType === 'Operator' &&
            (!keyword ||
                x.displayName.toLocaleLowerCase().includes(keyword) ||
                (x.description ?? '').toLocaleLowerCase().includes(keyword))
    )
})

async function openOperatorDocs(): Promise<void> {
    operatorDocsOpen.value = true
    operatorDocsLoading.value = true
    try {
        const palette = await getWorkflowNodePalette()
        operatorDefinitions.value = palette.categories.flatMap((x) => x.nodes)
        const visible = filteredOperatorDefinitions.value
        if (!selectedOperator.value || !visible.some((x) => x.id === selectedOperator.value?.id))
            selectedOperator.value = visible[0]
    } catch (error) {
        toast.error(`加载算子说明失败：${String(error)}`)
    } finally {
        operatorDocsLoading.value = false
    }
}

function metadataText(value: unknown): string {
    if (value === undefined || value === null) return ''
    return typeof value === 'string' ? value : JSON.stringify(value)
}
const taskLoading = ref(false)
const taskSaving = ref(false)
const taskConfig = ref<ProjectTaskBatch>()
const deployment = ref<ProjectDeployment>()
const applicationStatus = ref<ProjectApplicationStatus>()
const deploymentHistory = ref<ProjectDeployment[]>([])
const versionHistoryOpen = ref(false)
const taskConfigBaseline = ref('')
const taskConfigDirty = computed(
    () => !!taskConfig.value && JSON.stringify(taskConfig.value) !== taskConfigBaseline.value
)
const handshakeStatus = ref<WorkflowPlcHandshakeStatus>()
const resultOutputs = ref<string[]>([])
const handshakeNodeTree = ref<PlcBrowseTreeNode[]>([])
const handshakeTreeLoading = ref(false)
const handshakeTreeTruncated = ref(false)
const handshakeForm = ref<SaveWorkflowPlcHandshakeConfig>({
    projectId: '',
    plcDeviceId: '',
    captureRequestAddress: '',
    requestIdAddress: '',
    resultAckAddress: '',
    resultAckIdAddress: '',
    heartbeatAddress: '',
    deviceStatusAddress: '',
    taskStatusAddress: '',
    canCaptureAddress: '',
    captureAckAddress: '',
    ackRequestIdAddress: '',
    resultValidAddress: '',
    resultRequestIdAddress: '',
    resultCodeAddress: '',
    errorCodeAddress: '',
    isEnabled: false,
})
const plcTriggerLoading = ref(false)
const plcTriggerSaving = ref(false)
const plcTriggers = ref<WorkflowPlcTrigger[]>([])
const plcDevices = ref<PlcDeviceDto[]>([])
const plcTags = ref<PlcTagDto[]>([])
const editingPlcTriggerId = ref<string>()
const plcTriggerForm = ref({ plcDeviceId: '', plcTagId: '', value: '', tolerance: 0, isEnabled: true })
const plcTriggerDebugValue = ref<PlcTagValueDto>()
const plcTriggerDebugging = ref(false)
const selectedPlcTag = computed(() => plcTags.value.find((tag) => tag.id === plcTriggerForm.value.plcTagId))
const selectedPlcCanRead = computed(
    () => !!selectedPlcTag.value && (selectedPlcTag.value.access & PlcTagAccess.Read) !== 0
)
const selectedPlcCanWrite = computed(
    () => !!selectedPlcTag.value && (selectedPlcTag.value.access & PlcTagAccess.Write) !== 0
)
const selectedPlcIsFloat = computed(
    () =>
        selectedPlcTag.value?.dataType === PlcTagDataType.Float ||
        selectedPlcTag.value?.dataType === PlcTagDataType.Double
)
const selectedPlcIsBoolean = computed(() => selectedPlcTag.value?.dataType === PlcTagDataType.Boolean)
const selectedPlcIsDate = computed(() => selectedPlcTag.value?.dataType === PlcTagDataType.DateTime)
const selectedPlcIsNumeric = computed(
    () =>
        selectedPlcTag.value !== undefined &&
        [
            PlcTagDataType.SByte,
            PlcTagDataType.Byte,
            PlcTagDataType.Int16,
            PlcTagDataType.UInt16,
            PlcTagDataType.Int32,
            PlcTagDataType.UInt32,
            PlcTagDataType.Int64,
            PlcTagDataType.UInt64,
            PlcTagDataType.Float,
            PlcTagDataType.Double,
        ].includes(selectedPlcTag.value.dataType)
)
const handshakeFields = [
    ['captureRequestAddress', '拍照请求 CaptureRequest', 'BOOL；PLC 写 1，平台受理后 PLC 自己清零'],
    ['requestIdAddress', '请求号 RequestId', 'DINT；PLC 写入递增请求号'],
    ['resultAckAddress', '结果确认 ResultAck', 'BOOL；PLC 收到结果后写 1，并由 PLC 清零'],
    ['resultAckIdAddress', '结果确认号 ResultAckId', 'DINT；PLC 回写已接收的请求号'],
    ['heartbeatAddress', '心跳 Heartbeat', 'DINT；平台周期写入'],
    ['deviceStatusAddress', '设备状态 DeviceStatus', 'DINT；平台写入整体状态'],
    ['taskStatusAddress', '任务状态 TaskStatus', 'DINT；平台写入任务阶段'],
    ['canCaptureAddress', '允许拍照 CanCapture', 'BOOL；平台写入是否可接收请求'],
    ['captureAckAddress', '拍照受理 CaptureAck', 'BOOL；平台确认请求'],
    ['ackRequestIdAddress', '受理请求号 AckRequestId', 'DINT；平台回写请求号'],
    ['resultValidAddress', '结果有效 ResultValid', 'BOOL；平台发布结果后置 1'],
    ['resultRequestIdAddress', '结果请求号 ResultRequestId', 'DINT；平台写入结果对应请求号'],
    ['resultCodeAddress', '结果码 ResultCode', 'DINT；平台写入 OK/NG/Error'],
    ['errorCodeAddress', '错误码 ErrorCode', 'DINT；平台写入错误原因'],
] as const
const handshakeVariableOptions = computed(() => {
    const result: Array<{ address: string; label: string }> = []
    const visit = (nodes: PlcBrowseTreeNode[], depth: number) => {
        for (const node of nodes) {
            if (node.nodeClass.toLowerCase() === 'variable') {
                result.push({
                    address: node.address,
                    label: `${'　'.repeat(depth)}${node.displayName || node.browseName} · ${node.address}`,
                })
            }
            visit(node.children || [], depth + 1)
        }
    }
    visit(handshakeNodeTree.value, 0)
    return result
})
let editor: monaco.editor.IStandaloneCodeEditor | undefined
let validationTimer: ReturnType<typeof setTimeout> | undefined
let cursorMapTimer: ReturnType<typeof setTimeout> | undefined
let clockTimer: ReturnType<typeof setInterval> | undefined
let debugPollTimer: ReturnType<typeof setInterval> | undefined
let debugConnection: signalR.HubConnection | undefined
let joinedExecutionId = ''
let lastTerminalMessageKey = ''
let lastDebugStatusTimestamp = 0
let lastDebugStateVersion = 0
let resultExecutionId = ''
let resultRequest: Promise<void> | undefined
let resultRetryTimer: ReturnType<typeof setTimeout> | undefined
let resultFailureLoggedExecutionId = ''
const disposables: monaco.IDisposable[] = []

const { fitView, setCenter } = useVueFlow()
const graphNodes = computed<Node[]>(() =>
    (source.value?.graphData.nodes ?? []).map((node, index) => ({
        id: node.id,
        position: { x: node.x ?? (index % 4) * 220, y: node.y ?? Math.floor(index / 4) * 140 },
        label: node.text?.value || node.type,
        data: { type: node.type, selectedPort: selectedNodeId.value === node.id ? selectedPort.value : undefined },
        class:
            debugStatus.value?.faultNodeId === node.id
                ? 'workflow-node-faulted'
                : debugStatus.value?.currentNodeId === node.id
                  ? effectiveDebugState.value === 'running'
                      ? 'workflow-node-running'
                      : 'workflow-node-paused'
                  : completedNodeIds.value.includes(node.id)
                    ? 'workflow-node-completed'
                    : breakpointNodes.value.includes(node.id)
                      ? 'workflow-node-breakpoint'
                      : selectedNodeId.value === node.id && selectedPort.value
                        ? 'workflow-node-port-selected'
                        : '',
    }))
)
const graphEdges = computed<Edge[]>(() =>
    (source.value?.graphData.edges ?? [])
        .filter((edge) => edge.sourceNodeId && edge.targetNodeId)
        .map((edge) => ({
            id: edge.id,
            source: edge.sourceNodeId!,
            target: edge.targetNodeId!,
        }))
)
const filteredProjects = computed(() => {
    const keyword = projectFilter.value.trim().toLocaleLowerCase()
    if (!keyword) return projects.value
    return projects.value.filter((project) =>
        `${project.projectCode} ${project.name}`.toLocaleLowerCase().includes(keyword)
    )
})

function ideInput(offset = 0) {
    return {
        sourceCode: source.value?.sourceCode ?? '',
        documentVersion: documentVersion.value,
        offset,
        workflowId: source.value?.workflowId,
        projectId: projectId.value || undefined,
    }
}

function resetPlcTriggerForm() {
    editingPlcTriggerId.value = undefined
    plcTriggerForm.value = { plcDeviceId: '', plcTagId: '', value: '', tolerance: 0, isEnabled: true }
    plcTags.value = []
}

function emptyHandshake(): SaveWorkflowPlcHandshakeConfig {
    return {
        projectId: projectId.value,
        plcDeviceId: '',
        captureRequestAddress: '',
        requestIdAddress: '',
        resultAckAddress: '',
        resultAckIdAddress: '',
        heartbeatAddress: '',
        deviceStatusAddress: '',
        taskStatusAddress: '',
        canCaptureAddress: '',
        captureAckAddress: '',
        ackRequestIdAddress: '',
        resultValidAddress: '',
        resultRequestIdAddress: '',
        resultCodeAddress: '',
        errorCodeAddress: '',
        isEnabled: false,
    }
}

async function loadHandshakeNodeTree() {
    handshakeNodeTree.value = []
    handshakeTreeTruncated.value = false
    if (!handshakeForm.value.plcDeviceId) return
    try {
        handshakeTreeLoading.value = true
        const result = await browsePlcTree(handshakeForm.value.plcDeviceId)
        handshakeNodeTree.value = result.items
        handshakeTreeTruncated.value = result.truncated
    } catch (error) {
        toast.error(`加载 OPC UA 节点树失败：${String(error)}`)
    } finally {
        handshakeTreeLoading.value = false
    }
}

async function onHandshakeDeviceChange() {
    for (const [field] of handshakeFields) handshakeForm.value[field] = ''
    await loadHandshakeNodeTree()
}

async function loadResultOutputs() {
    resultOutputs.value = []
    const id = taskConfig.value?.resultWorkflowId
    if (!id) return
    try {
        const workflow = await getRuntimeWorkflow(id)
        const parsed = JSON.parse(workflow.outputVariables || '[]')
        resultOutputs.value = Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string') : []
        if (!resultOutputs.value.includes(taskConfig.value?.resultVariableName || ''))
            taskConfig.value!.resultVariableName = undefined
    } catch (error) {
        toast.error(`加载结果变量失败：${String(error)}`)
    }
}

async function openTaskDrawer() {
    if (!projectId.value) return
    taskDrawerOpen.value = true
    taskLoading.value = true
    try {
        const [tasks, devices, status] = await Promise.all([
            getProjectTasks(projectId.value),
            getPlcs(),
            getWorkflowPlcHandshakeStatus(projectId.value),
        ])
        taskConfig.value = tasks
        taskConfigBaseline.value = JSON.stringify(tasks)
        plcDevices.value = devices.filter((x) => x.isEnabled)
        handshakeStatus.value = status
        try {
            applicationStatus.value = await getProjectApplicationStatus(projectId.value)
            deployment.value = applicationStatus.value.activeDeployment ?? undefined
        } catch {
            deployment.value = undefined
        }
        if (status.isConfigured) {
            const config = await getWorkflowPlcHandshake(projectId.value)
            const { id: _id, ...saveInput } = config
            handshakeForm.value = saveInput
            await loadHandshakeNodeTree()
        } else {
            handshakeForm.value = emptyHandshake()
        }
        await loadResultOutputs()
    } catch (error) {
        toast.error(`加载任务配置失败：${String(error)}`)
    } finally {
        taskLoading.value = false
    }
}

async function saveTaskConfig() {
    if (!taskConfig.value) return
    try {
        taskSaving.value = true
        if (dirty.value && !(await save())) return
        if (!applicationStatus.value) applicationStatus.value = await getProjectApplicationStatus(projectId.value)
        taskConfig.value.items.forEach((x, index) => {
            x.orderNo = index
        })
        const result = await applyProjectToDevice(
            projectId.value,
            taskConfig.value,
            applicationStatus.value.activeDeployment?.id
        )
        deployment.value = result.deployment
        taskConfigBaseline.value = JSON.stringify(taskConfig.value)
        applicationStatus.value = await getProjectApplicationStatus(projectId.value)
        toast.success(
            result.unchanged
                ? `任务配置已生效 R${result.deployment.revision}。`
                : `任务配置已保存并生效 R${result.deployment.revision}。`
        )
    } catch (error) {
        toast.error(`保存并生效任务配置失败：${String(error)}`)
    } finally {
        taskSaving.value = false
    }
}

function moveTask(index: number, offset: number) {
    if (!taskConfig.value) return
    const target = index + offset
    if (target < 0 || target >= taskConfig.value.items.length) return
    const [item] = taskConfig.value.items.splice(index, 1)
    taskConfig.value.items.splice(target, 0, item!)
}

async function openVersionHistory() {
    if (!projectId.value) return
    versionHistoryOpen.value = true
    try {
        deploymentHistory.value = (await getProjectDeploymentHistory(projectId.value, 0, 100)).items
    } catch (error) {
        toast.error(`加载版本历史失败：${String(error)}`)
    }
}

async function rollbackToVersion(item: ProjectDeployment) {
    if (!(await confirmAction({ message: `确认将设备回滚到 R${item.revision}？` }))) return
    try {
        deployment.value = await rollbackProjectDeployment(item.id)
        applicationStatus.value = await getProjectApplicationStatus(projectId.value)
        deploymentHistory.value = (await getProjectDeploymentHistory(projectId.value, 0, 100)).items
        toast.success(`已回滚并应用 R${item.revision}。`)
    } catch (error) {
        toast.error(`回滚失败：${String(error)}`)
    }
}

async function saveHandshake() {
    if (!projectId.value) return
    try {
        taskSaving.value = true
        handshakeForm.value.projectId = projectId.value
        await saveWorkflowPlcHandshake(projectId.value, handshakeForm.value)
        handshakeStatus.value = await getWorkflowPlcHandshakeStatus(projectId.value)
        toast.success('PLC 握手配置已保存。')
    } catch (error) {
        toast.error(`保存 PLC 握手配置失败：${String(error)}`)
    } finally {
        taskSaving.value = false
    }
}

async function resetHandshake() {
    if (!projectId.value || !(await confirmAction({ message: '确认复位当前 PLC 握手状态？' }))) return
    try {
        handshakeStatus.value = await resetWorkflowPlcHandshake(projectId.value)
        toast.success('握手状态已复位。')
    } catch (error) {
        toast.error(`复位失败：${String(error)}`)
    }
}

async function openPlcTriggerDrawer() {
    if (!projectId.value) return
    plcTriggerDrawerOpen.value = true
    plcTriggerLoading.value = true
    try {
        const [triggers, devices] = await Promise.all([getWorkflowPlcTriggers(projectId.value), getPlcs()])
        plcTriggers.value = triggers
        plcDevices.value = devices.filter((device) => device.isEnabled)
    } catch (error) {
        toast.error(`加载 PLC 触发配置失败：${String(error)}`)
    } finally {
        plcTriggerLoading.value = false
    }
}

async function loadPlcTags() {
    plcTriggerForm.value.plcTagId = ''
    plcTriggerDebugValue.value = undefined
    plcTags.value = []
    if (!plcTriggerForm.value.plcDeviceId) return
    try {
        plcTags.value = (await getPlcTags(plcTriggerForm.value.plcDeviceId)).filter((tag) => tag.isEnabled)
    } catch (error) {
        toast.error(`加载 PLC 点位失败：${String(error)}`)
    }
}

function editPlcTrigger(trigger: WorkflowPlcTrigger) {
    plcTriggerDebugValue.value = undefined
    editingPlcTriggerId.value = trigger.id
    plcTriggerForm.value = {
        plcDeviceId: trigger.plcDeviceId,
        plcTagId: trigger.plcTagId,
        value: '',
        tolerance: trigger.tolerance,
        isEnabled: trigger.isEnabled,
    }
    void getPlcTags(trigger.plcDeviceId)
        .then((tags) => {
            plcTags.value = tags.filter((tag) => tag.isEnabled || tag.id === trigger.plcTagId)
            try {
                const parsed = JSON.parse(trigger.expectedValueJson)
                plcTriggerForm.value.value = typeof parsed === 'string' ? parsed : String(parsed)
            } catch {
                plcTriggerForm.value.value = trigger.expectedValueJson
            }
        })
        .catch((error) => toast.error(`加载 PLC 点位失败：${String(error)}`))
}

function expectedValueJson() {
    if (selectedPlcIsBoolean.value) return JSON.stringify(plcTriggerForm.value.value === 'true')
    if (selectedPlcIsNumeric.value) {
        const value = Number(plcTriggerForm.value.value)
        if (!Number.isFinite(value)) throw new Error('请输入有效数值。')
        return JSON.stringify(value)
    }
    if (selectedPlcIsDate.value) {
        const date = new Date(plcTriggerForm.value.value)
        if (Number.isNaN(date.getTime())) throw new Error('请输入有效日期时间。')
        return JSON.stringify(date.toISOString())
    }
    return JSON.stringify(plcTriggerForm.value.value)
}

async function readPlcTriggerDebugValue() {
    if (!selectedPlcTag.value || !selectedPlcCanRead.value) return
    try {
        plcTriggerDebugging.value = true
        const values = await readPlcTags(plcTriggerForm.value.plcDeviceId, [selectedPlcTag.value.id])
        plcTriggerDebugValue.value = values[0]
        if (!values.length) toast.warning('PLC 未返回该点位的值。')
    } catch (error) {
        toast.error(`读取触发点位失败：${String(error)}`)
    } finally {
        plcTriggerDebugging.value = false
    }
}

async function writePlcTriggerDebugValue() {
    if (!selectedPlcTag.value || !selectedPlcCanWrite.value) return
    try {
        plcTriggerDebugging.value = true
        await writePlcTag(
            plcTriggerForm.value.plcDeviceId,
            selectedPlcTag.value.id,
            JSON.parse(expectedValueJson()) as unknown
        )
        toast.success('已写入目标值，请观察任务是否被触发。')
        if (selectedPlcCanRead.value) await readPlcTriggerDebugValue()
    } catch (error) {
        toast.error(`写入调试值失败：${error instanceof Error ? error.message : String(error)}`)
    } finally {
        plcTriggerDebugging.value = false
    }
}

function plcDebugValueText(value: unknown) {
    if (value === undefined) return '-'
    if (typeof value === 'string') return value
    try {
        return JSON.stringify(value)
    } catch {
        return String(value)
    }
}

async function savePlcTrigger() {
    if (!projectId.value || !selectedPlcTag.value) {
        toast.warning('请选择 PLC 和点位。')
        return
    }
    try {
        plcTriggerSaving.value = true
        const input: SaveWorkflowPlcTrigger = {
            projectId: projectId.value,
            plcDeviceId: plcTriggerForm.value.plcDeviceId,
            plcTagId: plcTriggerForm.value.plcTagId,
            expectedValueJson: expectedValueJson(),
            tolerance: selectedPlcIsFloat.value ? Math.max(0, plcTriggerForm.value.tolerance) : 0,
            isEnabled: plcTriggerForm.value.isEnabled,
        }
        await saveWorkflowPlcTrigger(editingPlcTriggerId.value, input)
        plcTriggers.value = await getWorkflowPlcTriggers(projectId.value)
        resetPlcTriggerForm()
        toast.success('PLC 触发配置已保存。')
    } catch (error) {
        toast.error(`保存 PLC 触发配置失败：${error instanceof Error ? error.message : String(error)}`)
    } finally {
        plcTriggerSaving.value = false
    }
}

async function togglePlcTrigger(trigger: WorkflowPlcTrigger) {
    try {
        await saveWorkflowPlcTrigger(trigger.id, { ...trigger, isEnabled: !trigger.isEnabled })
        plcTriggers.value = await getWorkflowPlcTriggers(projectId.value)
    } catch (error) {
        toast.error(`更新触发状态失败：${String(error)}`)
    }
}

async function removePlcTrigger(trigger: WorkflowPlcTrigger) {
    if (!(await confirmAction({ message: '确认删除该 PLC 触发映射？' }))) return
    try {
        await deleteWorkflowPlcTrigger(trigger.id)
        plcTriggers.value = plcTriggers.value.filter((item) => item.id !== trigger.id)
        if (editingPlcTriggerId.value === trigger.id) resetPlcTriggerForm()
        toast.success('PLC 触发配置已删除。')
    } catch (error) {
        toast.error(`删除 PLC 触发配置失败：${String(error)}`)
    }
}

function triggerValueDisplay(trigger: WorkflowPlcTrigger) {
    try {
        return JSON.parse(trigger.expectedValueJson) as string | number | boolean
    } catch {
        return trigger.expectedValueJson
    }
}

function plcDeviceName(id: string) {
    return plcDevices.value.find((device) => device.id === id)?.name ?? id
}

function plcTagName(trigger: WorkflowPlcTrigger) {
    const tag = plcTags.value.find((item) => item.id === trigger.plcTagId)
    return tag ? `${tag.code} · ${tag.name}` : trigger.plcTagId
}

async function loadWorkflows() {
    if (!projectId.value) return
    workflows.value = await listWorkflows(projectId.value)
    if (!workflowId.value && workflows.value.length) workflowId.value = workflows.value[0]!.id
}

async function loadProjects() {
    projects.value = await listProjects()
    if (!projects.value.length) {
        projectId.value = ''
        return
    }
    if (!projects.value.some((project) => project.id === projectId.value)) {
        projectId.value = projects.value[0]!.id
    }
}

async function loadWorkflow() {
    if (!workflowId.value) return
    executionId.value = ''
    debugStatus.value = undefined
    debugResults.value = []
    previewResult.value = undefined
    lastDebugStatusTimestamp = 0
    lastDebugStateVersion = 0
    resultExecutionId = ''
    resultRequest = undefined
    clearTimeout(resultRetryTimer)
    resultRetryTimer = undefined
    resultFailureLoggedExecutionId = ''
    debugActivity.value = 'idle'
    breakpointNodes.value = []
    stackResults.value = []
    traceResults.value = []
    performanceResults.value = []
    source.value = await getWorkflowSource(workflowId.value)
    jsonText.value = JSON.stringify(source.value.graphData, null, 2)
    dirty.value = false
    documentVersion.value++
    editor?.setValue(source.value.sourceCode)
    await nextTick()
    void fitView({ padding: 0.2 })
    await validateNow()
    await restoreDebugSession()
}

async function restoreDebugSession() {
    if (!projectId.value || !workflowId.value) return
    try {
        const sessions = await getDebugSessions(projectId.value)
        const workflowSessions = sessions.filter((item) => item.workflowId === workflowId.value)
        const session = workflowSessions.find((item) => !item.isTerminal) ?? workflowSessions[0]
        if (!session) return

        executionId.value = session.executionId
        debugResults.value = []
        previewResult.value = undefined
        debugActivity.value = 'idle'
        breakpointNodes.value = await getBreakpoints(session.executionId)
        await connectDebugEvents(session.executionId)
        await applyDebugStatus(session)
        output.value.unshift(
            `已恢复调试会话：${session.debugState ?? 'paused'}，进度 ${session.executedSteps}/${session.totalSteps}`
        )
    } catch (error) {
        executionId.value = ''
        debugStatus.value = undefined
        debugResults.value = []
        previewResult.value = undefined
        breakpointNodes.value = []
        debugActivity.value = 'idle'
        output.value.unshift(`调试会话恢复失败：${String(error)}`)
    }
}

async function validateNow() {
    if (!source.value) return
    const version = documentVersion.value
    const result = await diagnostics(ideInput())
    if (result.documentVersion !== version) return
    problems.value = result.diagnostics
    const model = editor?.getModel()
    if (!model) return
    monaco.editor.setModelMarkers(
        model,
        'workflow',
        result.diagnostics.map((item) => ({
            severity: item.severity === 'warning' ? monaco.MarkerSeverity.Warning : monaco.MarkerSeverity.Error,
            message: `[${item.code}] ${item.message}`,
            startLineNumber: Math.max(1, item.range.start.line),
            startColumn: Math.max(1, item.range.start.column),
            endLineNumber: Math.max(1, item.range.end.line),
            endColumn: Math.max(1, item.range.end.column),
        }))
    )
}

function scheduleValidation() {
    clearTimeout(validationTimer)
    validationTimer = setTimeout(() => void validateNow(), 250)
}

async function save() {
    if (!source.value) return false
    saving.value = true
    try {
        source.value = await saveWorkflowSource(source.value.workflowId, source.value)
        dirty.value = false
        output.value.unshift(`已保存 revision ${source.value.revision}`)
        return true
    } catch (error) {
        output.value.unshift(`保存失败：${String(error)}`)
        return false
    } finally {
        saving.value = false
    }
}

async function formatDocument() {
    if (!source.value) return
    const result = await formatSource(ideInput())
    editor?.setValue(result.sourceCode)
}

async function applyJson() {
    if (!source.value) return
    try {
        const graph = JSON.parse(jsonText.value) as WorkflowGraph
        const result = await patchGraph(source.value, graph, ++documentVersion.value)
        if (result.hasConflict) {
            output.value.unshift('画布修改与服务器版本冲突，请重新加载后再合并。')
            return
        }
        source.value.graphData = result.graphData
        source.value.sourceCode = result.sourceCode
        editor?.setValue(result.sourceCode)
        dirty.value = true
    } catch (error) {
        output.value.unshift(`JSON 无效：${String(error)}`)
    }
}

async function startDebug() {
    if (!source.value || !projectId.value) return
    completedNodeIds.value = []
    debugActivity.value = 'starting'
    const result = await debugSource(projectId.value, source.value)
    if (result.error || !result.executionId || /^0{8}-0{4}-0{4}-0{4}-0{12}$/i.test(result.executionId)) {
        debugActivity.value = 'idle'
        executionId.value = ''
        debugStatus.value = undefined
        debugResults.value = []
        previewResult.value = undefined
        output.value.unshift(`调试启动失败：${result.message || result.errorCode || '服务端未创建执行会话'}`)
        bottomTab.value = 'problems'
        return
    }
    executionId.value = result.executionId
    debugResults.value = []
    previewResult.value = undefined
    resultExecutionId = ''
    resultRequest = undefined
    clearTimeout(resultRetryTimer)
    resultRetryTimer = undefined
    resultFailureLoggedExecutionId = ''
    await applyDebugStatus(result.status)
    debugActivity.value = 'idle'
    if (breakpointNodes.value.length) await setBreakpoints(executionId.value, breakpointNodes.value)
    bottomTab.value = 'variables'
}

async function startAndRunDebug() {
    if (!source.value || !projectId.value) return
    completedNodeIds.value = []
    debugActivity.value = 'starting'
    try {
        const result = await debugAndRunSource(projectId.value, source.value, breakpointNodes.value)
        if (result.error || !result.executionId || /^0{8}-0{4}-0{4}-0{4}-0{12}$/i.test(result.executionId)) {
            throw new Error(result.message || result.errorCode || '服务端未创建执行会话')
        }
        executionId.value = result.executionId
        debugResults.value = []
        previewResult.value = undefined
        resultExecutionId = ''
        resultRequest = undefined
        clearTimeout(resultRetryTimer)
        resultRetryTimer = undefined
        resultFailureLoggedExecutionId = ''
        await connectDebugEvents(result.executionId)
        await applyDebugStatus(await getDebugStatus(result.executionId, false))
    } catch (error) {
        executionId.value = ''
        debugStatus.value = undefined
        debugResults.value = []
        previewResult.value = undefined
        output.value.unshift(`调试启动失败：${String(error)}`)
        bottomTab.value = 'output'
    } finally {
        debugActivity.value = 'idle'
    }
}

async function primaryDebugAction() {
    if (!canPrimaryDebug.value) return
    if (!executionId.value || debugStatus.value?.isTerminal) {
        await startAndRunDebug()
        return
    }
    await resume()
}

async function ensurePausedSession(): Promise<boolean> {
    if (executionId.value && !debugStatus.value?.isTerminal) return true
    await startDebug()
    if (executionId.value) await connectDebugEvents(executionId.value)
    return !!executionId.value
}

async function stepOnce() {
    if (!(await ensurePausedSession()) || !executionId.value) return
    debugActivity.value = 'running'
    try {
        const status = (await step(executionId.value)).status
        debugActivity.value = 'idle'
        await applyDebugStatus(status)
    } finally {
        debugActivity.value = 'idle'
    }
}

async function resume() {
    if (!canContinueDebug.value) return
    debugActivity.value = 'running'
    try {
        const status = await continueDebug(executionId.value)
        debugActivity.value = 'idle'
        await applyDebugStatus(status)
    } finally {
        debugActivity.value = 'idle'
    }
}

async function pause() {
    if (!executionId.value) return
    await applyDebugStatus(await pauseDebug(executionId.value))
}

async function stepMode(mode: 'into' | 'over' | 'out') {
    if (!canStepDebug.value) return
    debugActivity.value = 'running'
    try {
        const status =
            mode === 'into'
                ? await stepInto(executionId.value)
                : mode === 'over'
                  ? await stepOver(executionId.value)
                  : await stepOut(executionId.value)
        debugActivity.value = 'idle'
        await applyDebugStatus(status)
    } finally {
        debugActivity.value = 'idle'
    }
}

async function runToSelection() {
    if (!selectedNodeId.value || !(await ensurePausedSession()) || !executionId.value) return
    debugActivity.value = 'running'
    try {
        const status = await runTo(executionId.value, selectedNodeId.value)
        debugActivity.value = 'idle'
        await applyDebugStatus(status)
    } finally {
        debugActivity.value = 'idle'
    }
}

async function stop() {
    if (!executionId.value) return
    await stopDebug(executionId.value)
    output.value.unshift('已请求停止，将在当前节点完成后结束调试。')
    if (effectiveDebugState.value !== 'running') {
        await applyDebugStatus(await getDebugStatus(executionId.value))
    }
}

async function refreshWatch() {
    if (!executionId.value) return
    watchResults.value = await evaluateWatch(executionId.value, watchExpressions.value)
}

async function refreshDebugDetails() {
    if (!executionId.value) return
    ;[stackResults.value, traceResults.value, performanceResults.value] = await Promise.all([
        getStack(executionId.value),
        getTrace(executionId.value),
        getPerformance(executionId.value),
    ])
    completedNodeIds.value = Array.from(
        new Set(
            traceResults.value
                .filter((item) => item.nodeId && item.status !== 'faulted')
                .map((item) => String(item.nodeId))
        )
    )
}

async function applyDebugStatus(status: DebugStatus, eventUpdatedAt?: string) {
    const stateVersion = status.stateVersion ?? 0
    if (stateVersion > 0) {
        if (stateVersion < lastDebugStateVersion) return
        lastDebugStateVersion = stateVersion
    } else {
        const statusTimestamp = Date.parse(status.updatedAt ?? eventUpdatedAt ?? '')
        if (Number.isFinite(statusTimestamp)) {
            if (statusTimestamp < lastDebugStatusTimestamp) return
            lastDebugStatusTimestamp = statusTimestamp
        }
    }
    debugStatus.value = status
    statusReceivedAt.value = Date.now()
    if (status.isTerminal) stopDebugPolling()
    if (status.debugState === 'completed') await ensureDebugResult(status)
    const effectiveStatus = debugStatus.value ?? status
    const terminalMessageKey = effectiveStatus.isTerminal
        ? `${effectiveStatus.executionId}:${effectiveStatus.debugState}:${effectiveStatus.faultNodeId ?? ''}:${effectiveStatus.errorMessage ?? ''}`
        : ''
    if (effectiveStatus.debugState === 'completed') {
        if (terminalMessageKey !== lastTerminalMessageKey) {
            output.value.unshift(
                `调试执行完成：${effectiveStatus.executedSteps}/${effectiveStatus.totalSteps} 个节点，耗时 ${formatDuration(effectiveStatus.durationMs ?? 0)}，输出 ${debugResults.value.length} 项`
            )
        }
        bottomTab.value = 'results'
    } else if (effectiveStatus.debugState === 'faulted') {
        if (terminalMessageKey !== lastTerminalMessageKey) {
            output.value.unshift(
                `调试执行失败${effectiveStatus.faultNodeId ? `（节点 ${effectiveStatus.faultNodeId}）` : ''}：${effectiveStatus.errorMessage || '未知错误'}`
            )
        }
        bottomTab.value = 'output'
    }
    if (terminalMessageKey) lastTerminalMessageKey = terminalMessageKey
    await nextTick()
    const currentNode = source.value?.graphData.nodes.find((node) => node.id === status.currentNodeId)
    if (currentNode) {
        selectedNodeId.value = currentNode.id
        await setCenter(currentNode.x ?? 0, currentNode.y ?? 0, {
            zoom: 1.15,
            duration: 300,
        })
    }
    if (status.debugState !== 'running') {
        await Promise.all([refreshWatch(), refreshDebugDetails()])
    }
}

async function ensureDebugResult(status: DebugStatus) {
    if (status.debugState !== 'completed' || !status.executionId) return
    if (resultExecutionId === status.executionId) return
    if (resultRequest) {
        await resultRequest
        return
    }

    const requestedExecutionId = status.executionId
    resultRequest = (async () => {
        try {
            const result = await getDebugResult(requestedExecutionId)
            if (executionId.value !== requestedExecutionId) return
            debugResults.value = result
            resultExecutionId = requestedExecutionId
            clearTimeout(resultRetryTimer)
            resultRetryTimer = undefined
            resultFailureLoggedExecutionId = ''
        } catch (error) {
            if (resultFailureLoggedExecutionId !== requestedExecutionId) {
                output.value.unshift(`调试结果查询失败，将自动重试：${String(error)}`)
                resultFailureLoggedExecutionId = requestedExecutionId
            }
            scheduleDebugResultRetry(status)
        } finally {
            resultRequest = undefined
        }
    })()
    await resultRequest
}

function scheduleDebugResultRetry(status: DebugStatus) {
    if (resultRetryTimer || executionId.value !== status.executionId) return
    resultRetryTimer = setTimeout(() => {
        resultRetryTimer = undefined
        if (
            executionId.value === status.executionId &&
            debugStatus.value?.debugState === 'completed' &&
            resultExecutionId !== status.executionId
        ) {
            void ensureDebugResult(debugStatus.value)
        }
    }, 1000)
}

async function connectDebugEvents(id: string) {
    if (!debugConnection) {
        debugConnection = new signalR.HubConnectionBuilder()
            .withUrl('/signalr-hubs/workflow-debug', {
                accessTokenFactory: () => authStore.token ?? '',
            })
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build()
        debugConnection.on(
            'DebugStateChangedAsync',
            async (eventExecutionId: string, eventType: string, updatedAt: string, status: DebugStatus) => {
                if (eventExecutionId.toLowerCase() !== executionId.value.toLowerCase()) return
                if (
                    eventType === 'node-completed' &&
                    status.currentNodeId &&
                    !completedNodeIds.value.includes(status.currentNodeId)
                ) {
                    completedNodeIds.value.push(status.currentNodeId)
                }
                await applyDebugStatus(status, updatedAt)
            }
        )
        debugConnection.onreconnected(async () => {
            joinedExecutionId = ''
            if (executionId.value) await joinDebugExecution(executionId.value)
        })
        debugConnection.onreconnecting(() => startDebugPolling())
        debugConnection.onclose(() => startDebugPolling())
        try {
            await debugConnection.start()
        } catch {
            startDebugPolling()
            return
        }
    }
    await joinDebugExecution(id)
    stopDebugPolling()
}

async function joinDebugExecution(id: string) {
    if (!debugConnection || debugConnection.state !== signalR.HubConnectionState.Connected) {
        startDebugPolling()
        return
    }
    if (joinedExecutionId && joinedExecutionId !== id) {
        await debugConnection.invoke('LeaveExecutionAsync', joinedExecutionId)
    }
    if (joinedExecutionId !== id) {
        await debugConnection.invoke('JoinExecutionAsync', id)
        joinedExecutionId = id
    }
}

function startDebugPolling() {
    if (debugPollTimer) return
    debugPollTimer = setInterval(async () => {
        if (!executionId.value) return
        try {
            await applyDebugStatus(await getDebugStatus(executionId.value, false))
        } catch {
            // SignalR 重连或页面恢复时会再次同步完整状态。
        }
    }, 750)
}

function stopDebugPolling() {
    clearInterval(debugPollTimer)
    debugPollTimer = undefined
}

function formatDuration(ms: number) {
    return `${(ms / 1000).toFixed(2)} s`
}

function tableColumns(rows: Array<Record<string, unknown>>, preferred: string[]): string[] {
    const available = new Set(rows.flatMap((row) => Object.keys(row)))
    return [
        ...preferred.filter((key) => available.has(key)),
        ...Array.from(available).filter((key) => !preferred.includes(key)),
    ]
}

function formatCell(value: unknown): string {
    if (value === null || value === undefined) return '—'
    if (typeof value === 'string') return value
    if (typeof value === 'number' || typeof value === 'boolean') return String(value)
    return JSON.stringify(value)
}

function resultPresentation(result: DebugExecutionResult): WorkflowResultPresentation {
    return classifyWorkflowResult(result.valueType, result.value)
}

function isFilePresentation(presentation: WorkflowResultPresentation): boolean {
    return ['image', 'point-cloud', 'model-3d', 'cad', 'json-file', 'text-file', 'file'].includes(presentation)
}

function openResultPreview(result: DebugExecutionResult) {
    previewResult.value = previewResult.value?.name === result.name ? undefined : result
}

function selectProblem(problem: IdeDiagnostic) {
    activeView.value = 'source'
    editor?.setPosition({ lineNumber: problem.range.start.line, column: problem.range.start.column })
    editor?.revealLineInCenter(problem.range.start.line)
    editor?.focus()
}

async function selectGraphNode(event: { node: Node }) {
    selectedNodeId.value = event.node.id
    selectedPort.value = undefined
    const problem = problems.value.find((x) => x.nodeId === event.node.id)
    if (problem) selectProblem(problem)
    else if (editor) {
        const result = await mapPosition({ ...ideInput(0), nodeId: event.node.id })
        if (result.documentVersion === documentVersion.value && result.range) {
            activeView.value = 'source'
            editor.setPosition({ lineNumber: result.range.start.line, column: result.range.start.column })
            editor.revealLineInCenter(result.range.start.line)
        }
    }
}

function toggleBreakpoint(nodeId: string) {
    breakpointNodes.value = breakpointNodes.value.includes(nodeId)
        ? breakpointNodes.value.filter((x) => x !== nodeId)
        : [...breakpointNodes.value, nodeId]
    if (executionId.value) void setBreakpoints(executionId.value, breakpointNodes.value)
}

function toggleSelectedBreakpoint() {
    if (selectedNodeId.value) toggleBreakpoint(selectedNodeId.value)
}

function registerLanguage() {
    if (!monaco.languages.getLanguages().some((x) => x.id === 'aurora-workflow')) {
        monaco.languages.register({ id: 'aurora-workflow' })
        monaco.languages.setMonarchTokensProvider('aurora-workflow', {
            tokenizer: {
                root: [
                    [/\b(Workflow|Return|var)\b/, 'keyword'],
                    [/[A-Za-z_][A-Za-z0-9_]*(?=\s*\()/, 'type.identifier'],
                    [/[A-Za-z_][A-Za-z0-9_]*(?=\s*:)/, 'attribute.name'],
                    [/"([^"\\]|\\.)*$/, 'string.invalid'],
                    [/"/, { token: 'string.quote', bracket: '@open', next: '@string' }],
                    [/-?\d+(\.\d+)?/, 'number'],
                    [/\b(true|false|null)\b/, 'constant'],
                    [/\/\/.*$/, 'comment'],
                ],
                string: [
                    [/[^\\"]+/, 'string'],
                    [/\\./, 'string.escape'],
                    [/"/, { token: 'string.quote', bracket: '@close', next: '@pop' }],
                ],
            },
        })
    }
    disposables.push(
        monaco.languages.registerCompletionItemProvider('aurora-workflow', {
            triggerCharacters: ['(', ',', ':', ' ', '.', '['],
            provideCompletionItems: async (model, position) => {
                const version = documentVersion.value
                const result = await completions(ideInput(model.getOffsetAt(position)))
                if (result.documentVersion !== version) return { suggestions: [] }
                const word = model.getWordUntilPosition(position)
                const range = new monaco.Range(
                    position.lineNumber,
                    word.startColumn,
                    position.lineNumber,
                    word.endColumn
                )
                return {
                    suggestions: result.items.map((item) => ({
                        label: item.label,
                        detail: item.detail,
                        documentation: item.documentation,
                        kind:
                            item.kind === 'operator'
                                ? monaco.languages.CompletionItemKind.Class
                                : item.kind === 'variable'
                                  ? monaco.languages.CompletionItemKind.Variable
                                  : item.kind === 'property'
                                    ? monaco.languages.CompletionItemKind.Property
                                    : item.kind === 'enum'
                                      ? monaco.languages.CompletionItemKind.EnumMember
                                      : monaco.languages.CompletionItemKind.Function,
                        insertText: item.insertText,
                        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                        range,
                    })),
                }
            },
        }),
        monaco.languages.registerHoverProvider('aurora-workflow', {
            provideHover: async (model, position) => {
                const result = await hover(ideInput(model.getOffsetAt(position)))
                if (!result.markdown) return null
                return { contents: [{ value: result.markdown }] }
            },
        }),
        monaco.languages.registerDefinitionProvider('aurora-workflow', {
            provideDefinition: async (model, position) => {
                const result = await definition(ideInput(model.getOffsetAt(position)))
                return result.locations.map((location) => {
                    let uri = model.uri
                    if (location.uri && location.virtualSource) {
                        uri = monaco.Uri.parse(location.uri)
                        const existing = monaco.editor.getModel(uri)
                        if (existing) existing.setValue(location.virtualSource)
                        else monaco.editor.createModel(location.virtualSource, 'csharp', uri)
                    }
                    return {
                        uri,
                        range: new monaco.Range(
                            location.range.start.line,
                            location.range.start.column,
                            location.range.end.line,
                            location.range.end.column
                        ),
                    }
                })
            },
        }),
        monaco.languages.registerReferenceProvider('aurora-workflow', {
            provideReferences: async (model, position) => {
                const result = await references(ideInput(model.getOffsetAt(position)))
                return result.locations.map((location) => ({
                    uri: model.uri,
                    range: new monaco.Range(
                        location.range.start.line,
                        location.range.start.column,
                        location.range.end.line,
                        location.range.end.column
                    ),
                }))
            },
        }),
        monaco.languages.registerDocumentSemanticTokensProvider('aurora-workflow', {
            getLegend: () => ({ tokenTypes: ['keyword', 'type', 'variable', 'property', 'enumMember', 'function', 'parameter'], tokenModifiers: [] }),
            provideDocumentSemanticTokens: async (_model) => {
                const version = documentVersion.value
                const result = await semanticTokens(ideInput(0))
                if (result.documentVersion !== version) return { data: new Uint32Array() }
                const typeIndex: Record<string, number> = {
                    keyword: 0, type: 1, variable: 2, property: 3,
                    enumMember: 4, function: 5, parameter: 6,
                }
                const sorted = [...result.tokens].sort((a, b) =>
                    a.range.start.line - b.range.start.line || a.range.start.column - b.range.start.column)
                const data: number[] = []
                let lastLine = 0
                let lastColumn = 0
                for (const token of sorted) {
                    const line = token.range.start.line - 1
                    const column = token.range.start.column - 1
                    const deltaLine = line - lastLine
                    const deltaColumn = deltaLine === 0 ? column - lastColumn : column
                    const length = Math.max(1, token.range.end.offset - token.range.start.offset)
                    data.push(deltaLine, deltaColumn, length, typeIndex[token.type] ?? 5, 0)
                    lastLine = line
                    lastColumn = column
                }
                return { data: new Uint32Array(data) }
            },
            releaseDocumentSemanticTokens: () => undefined,
        }),
        monaco.languages.registerRenameProvider('aurora-workflow', {
            resolveRenameLocation: (model, position) => {
                const word = model.getWordAtPosition(position)
                if (!word) throw new Error('当前位置没有可重命名的符号')
                return {
                    text: word.word,
                    range: new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn),
                }
            },
            provideRenameEdits: async (model, position, newName) => {
                if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(newName)) {
                    return { edits: [], rejectReason: '变量名必须是合法标识符' }
                }
                const word = model.getWordAtPosition(position)
                if (!word) return { edits: [], rejectReason: '当前位置没有可重命名的变量' }
                const result = await renameSymbol({
                    ...ideInput(model.getOffsetAt(position)), oldName: word.word, newName,
                })
                const error = result.diagnostics.find((item) => item.severity === 'error')
                if (error) return { edits: [], rejectReason: error.message }
                return {
                    edits: result.edits.map((edit) => ({
                        resource: model.uri,
                        textEdit: {
                            text: edit.newText,
                            range: new monaco.Range(
                                edit.range.start.line,
                                edit.range.start.column,
                                edit.range.end.line,
                                edit.range.end.column
                            ),
                        },
                        versionId: model.getVersionId(),
                    })),
                }
            },
        }),
        monaco.languages.registerSignatureHelpProvider('aurora-workflow', {
            signatureHelpTriggerCharacters: ['(', ','],
            signatureHelpRetriggerCharacters: [','],
            provideSignatureHelp: async (model, position) => {
                const version = documentVersion.value
                const result = await signatureHelp(ideInput(model.getOffsetAt(position)))
                if (result.documentVersion !== version || !result.label) return null
                return {
                    value: {
                        activeParameter: result.activeParameter,
                        activeSignature: 0,
                        signatures: [
                            {
                                label: result.label,
                                parameters: result.parameters.map((label) => ({ label })),
                            },
                        ],
                    },
                    dispose: () => undefined,
                }
            },
        }),
        monaco.languages.registerDocumentFormattingEditProvider('aurora-workflow', {
            provideDocumentFormattingEdits: async () => {
                const result = await formatSource(ideInput())
                return [
                    {
                        range: editor?.getModel()?.getFullModelRange() ?? new monaco.Range(1, 1, 1, 1),
                        text: result.sourceCode,
                    },
                ]
            },
        })
    )
}

onMounted(async () => {
    clockTimer = setInterval(() => {
        clockNow.value = Date.now()
    }, 100)
    window.addEventListener('keydown', handleDebugShortcut)
    window.addEventListener('beforeunload', handleBeforeUnload)
    registerLanguage()
    if (editorHost.value) {
        editor = monaco.editor.create(editorHost.value, {
            value: '',
            language: 'aurora-workflow',
            theme: document.documentElement.classList.contains('dark') ? 'vs-dark' : 'vs',
            automaticLayout: true,
            minimap: { enabled: true },
            glyphMargin: true,
            fontSize: 13,
            tabSize: 4,
            quickSuggestions: {
                other: true,
                comments: false,
                strings: false,
            },
            suggestOnTriggerCharacters: true,
            snippetSuggestions: 'top',
            parameterHints: { enabled: true },
        })
        editor.onDidChangeModelContent(() => {
            if (!source.value || editor?.getValue() === source.value.sourceCode) return
            source.value.sourceCode = editor?.getValue() ?? ''
            documentVersion.value++
            dirty.value = true
            scheduleValidation()
        })
        editor.onDidChangeCursorPosition((event) => {
            clearTimeout(cursorMapTimer)
            cursorMapTimer = setTimeout(async () => {
                if (!editor) return
                const result = await mapPosition(ideInput(editor.getModel()!.getOffsetAt(event.position)))
                if (result.documentVersion === documentVersion.value && result.nodeId)
                {
                    selectedNodeId.value = result.nodeId
                    selectedPort.value = result.portName && result.portDirection
                        ? { name: result.portName, direction: result.portDirection }
                        : undefined
                }
            }, 120)
        })
    }
    await loadProjects()
    await loadWorkflows()
    if (projectId.value) {
        try {
            applicationStatus.value = await getProjectApplicationStatus(projectId.value)
            deployment.value = applicationStatus.value.activeDeployment ?? undefined
        } catch {
            applicationStatus.value = undefined
        }
    }
    await loadWorkflow()
})

watch(workflowId, () => void loadWorkflow())
watch(projectId, () => {
    plcTriggerDrawerOpen.value = false
    plcTriggers.value = []
    resetPlcTriggerForm()
    workflowId.value = ''
    source.value = null
    applicationStatus.value = undefined
    taskConfig.value = undefined
    taskConfigBaseline.value = ''
    void router.replace({
        query: {
            ...route.query,
            projectId: projectId.value || undefined,
            workflowId: undefined,
        },
    })
    void loadWorkflows()
    if (projectId.value)
        void getProjectApplicationStatus(projectId.value).then((status) => {
            applicationStatus.value = status
            deployment.value = status.activeDeployment ?? undefined
        })
})
onBeforeUnmount(() => {
    clearTimeout(validationTimer)
    clearTimeout(cursorMapTimer)
    clearTimeout(resultRetryTimer)
    clearInterval(clockTimer)
    stopDebugPolling()
    window.removeEventListener('keydown', handleDebugShortcut)
    window.removeEventListener('beforeunload', handleBeforeUnload)
    if (debugConnection) void debugConnection.stop()
    disposables.forEach((x) => x.dispose())
    editor?.dispose()
})

function handleBeforeUnload(event: BeforeUnloadEvent): void {
    if (!dirty.value) return
    event.preventDefault()
}

onBeforeRouteLeave(async () => {
    if (!dirty.value) return true
    return confirmAction({
        header: '存在未保存修改',
        message: '当前工作流尚未保存，确认离开并放弃修改吗？',
    })
})

function handleDebugShortcut(event: KeyboardEvent) {
    if (event.key !== 'F5') return
    event.preventDefault()
    void primaryDebugAction()
}

async function previewMigration() {
    migrationBusy.value = true
    migrationPanelOpen.value = true
    try {
        const result = await previewWorkflowMigration()
        migrationItems.value = result.items
        toast.success(`扫描 ${result.scannedCount} 个，${result.migratableCount} 个可迁移。`)
    } catch (error) {
        toast.error(`迁移预览失败：${String(error)}`)
    } finally { migrationBusy.value = false }
}

async function executeMigration() {
    if (!(await confirmAction({ header: '执行旧工作流迁移', message: '将逐工作流备份并升级为 V3，确认继续？' }))) return
    migrationBusy.value = true
    try {
        migrationBatch.value = await startWorkflowMigration()
        migrationItems.value = migrationBatch.value.items
        toast.success(`迁移完成：成功 ${migrationBatch.value.migratedCount}，失败 ${migrationBatch.value.failedCount}。`)
    } catch (error) { toast.error(`迁移失败：${String(error)}`) }
    finally { migrationBusy.value = false }
}

async function retryMigration() {
    if (!migrationBatch.value) return
    migrationBusy.value = true
    try {
        migrationBatch.value = await retryWorkflowMigration(migrationBatch.value.batchId)
        migrationItems.value = migrationBatch.value.items
    } finally { migrationBusy.value = false }
}

async function rollbackMigration(workflowId?: string) {
    if (!migrationBatch.value || !(await confirmAction({ header: '回滚迁移', message: '将恢复迁移前完整快照，确认继续？' }))) return
    migrationBusy.value = true
    try {
        migrationBatch.value = await rollbackWorkflowMigration(migrationBatch.value.batchId, workflowId)
        migrationItems.value = migrationBatch.value.items
        toast.success('迁移已回滚。')
    } finally { migrationBusy.value = false }
}
</script>

<template>
    <div
        class="workflow-ide-shell -m-3 flex h-[calc(100vh-3.5rem)] flex-col overflow-hidden bg-background text-foreground sm:-m-4 lg:-m-6"
    >
        <header class="flex-none border-b bg-card/70 shadow-sm backdrop-blur">
            <div class="flex min-h-12 flex-wrap items-center gap-2 px-3 py-2">
                <div class="mr-1 flex items-center gap-2 font-medium">
                    <GitBranch class="size-4 text-primary" />
                    <span class="hidden xl:inline">工作流调试</span>
                </div>
                <InputText
                    v-model.trim="projectFilter"
                    size="small"
                    class="w-36"
                    placeholder="搜索项目"
                    title="按项目名称或编号搜索"
                />
                <Select
                    v-model="projectId"
                    size="small"
                    class="min-w-52 max-w-72 flex-1"
                    :options="filteredProjects"
                    option-label="name"
                    option-value="id"
                    placeholder="选择项目"
                >
                    <template #option="{ option }">{{ option.projectCode }} · {{ option.name }}</template>
                </Select>
                <Select
                    v-model="workflowId"
                    size="small"
                    class="min-w-44 max-w-64 flex-1"
                    :options="workflows"
                    option-label="name"
                    option-value="id"
                    placeholder="选择工作流"
                />
                <span class="debug-state-badge" :class="debugStateInfo.className">
                    <span class="debug-state-dot" />
                    {{ debugStateInfo.label }}
                    <template v-if="debugStatus">
                        · {{ debugStatus.executedSteps }}/{{ debugStatus.totalSteps }} ·
                        {{ formatDuration(displayDurationMs) }}
                    </template>
                </span>
                <span
                    v-if="effectiveDebugState === 'running' && debugStatus?.currentNodeName"
                    class="max-w-52 truncate text-xs text-muted-foreground"
                >
                    {{ debugStatus.currentNodeName }}
                    <template v-if="displayCurrentNodeDurationMs >= 2000">
                        · {{ formatDuration(displayCurrentNodeDurationMs) }}
                    </template>
                </span>
                <span
                    class="ml-auto whitespace-nowrap text-xs"
                    :class="
                        dirty || taskConfigDirty || applicationStatus?.hasUnappliedChanges
                            ? 'text-amber-600 dark:text-amber-400'
                            : 'text-emerald-600'
                    "
                >
                    {{
                        dirty
                            ? '● 工作流未保存'
                            : taskConfigDirty
                              ? '● 任务配置未保存'
                              : deployment
                                ? `任务已生效 R${deployment.revision}`
                                : '尚未配置任务'
                    }}
                </span>
            </div>
            <div class="ide-action-bar">
                <div class="ide-action-group">
                    <Button size="small" :disabled="!dirty || saving || taskSaving" @click="save">
                        <Save class="size-3.5" />
                        {{ saving ? '保存中' : '保存工作流' }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="!projectId" @click="openTaskDrawer">
                        任务配置
                    </Button>
                    <Button size="small" severity="secondary" outlined @click="openOperatorDocs">算子说明</Button>
                    <Button size="small" severity="secondary" outlined :loading="migrationBusy" @click="previewMigration">
                        V1/V2 迁移
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="!projectId"
                        @click="openVersionHistory"
                    >
                        版本历史
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="!projectId"
                        @click="openPlcTriggerDrawer"
                    >
                        PLC 触发
                    </Button>
                    <Button size="small" severity="secondary" outlined @click="formatDocument">
                        <WandSparkles class="size-3.5" />
                        格式化
                    </Button>
                </div>
                <div class="ide-action-group">
                    <Button
                        size="small"
                        :disabled="!canPrimaryDebug"
                        title="调试运行/继续 (F5)"
                        @click="primaryDebugAction"
                    >
                        <Bug v-if="primaryDebugLabel === '调试运行'" class="size-3.5" />
                        <Play v-else class="size-3.5" />
                        {{ primaryDebugLabel }}
                    </Button>
                    <Button size="small" severity="secondary" outlined :disabled="!canPauseDebug" @click="pause">
                        暂停
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="debugActivity !== 'idle' || (!!executionId && !canStepDebug)"
                        @click="stepOnce"
                    >
                        <StepForward class="size-3.5" />
                        单步
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="!selectedNodeId"
                        :aria-pressed="selectedHasBreakpoint"
                        :title="selectedHasBreakpoint ? '取消选中节点的断点' : '在选中节点执行前暂停'"
                        @click="toggleSelectedBreakpoint"
                    >
                        {{ selectedHasBreakpoint ? '取消断点' : '设置断点' }}
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="!canStepDebug"
                        title="执行当前节点后停在下一节点；扁平工作流中等同于“越过"
                        @click="stepMode('into')"
                    >
                        进入节点
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="!canStepDebug"
                        title="执行当前节点后停在下一节点；当前工作流没有可进入的子调用"
                        @click="stepMode('over')"
                    >
                        越过节点
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="!canStepDebug"
                        title="当前为顶层工作流，将运行到工作流结束"
                        @click="stepMode('out')"
                    >
                        跳出工作流
                    </Button>
                    <Button
                        size="small"
                        severity="secondary"
                        outlined
                        :disabled="!selectedNodeId || debugActivity !== 'idle' || (!!executionId && !canContinueDebug)"
                        @click="runToSelection"
                    >
                        运行到节点
                    </Button>
                    <Button size="small" severity="danger" outlined :disabled="!canStopDebug" @click="stop">
                        <CircleStop class="size-3.5" />
                        停止
                    </Button>
                </div>
            </div>
            <div v-if="migrationPanelOpen" class="border-t bg-card px-3 py-2 text-xs">
                <div class="mb-2 flex items-center gap-2">
                    <strong>旧工作流迁移预览</strong>
                    <Button size="small" :disabled="migrationItems.length === 0" :loading="migrationBusy" @click="executeMigration">执行迁移</Button>
                    <Button v-if="migrationBatch?.failedCount" size="small" severity="secondary" :loading="migrationBusy" @click="retryMigration">重试失败项</Button>
                    <Button v-if="migrationBatch?.migratedCount" size="small" severity="danger" outlined :loading="migrationBusy" @click="rollbackMigration()">回滚整批</Button>
                    <Button size="small" severity="secondary" text class="ml-auto" @click="migrationPanelOpen = false">关闭</Button>
                </div>
                <div class="max-h-36 overflow-auto rounded border">
                    <div v-for="item in migrationItems" :key="item.workflowId" class="flex items-center gap-2 border-b px-2 py-1 last:border-0">
                        <span class="min-w-44 font-medium">{{ item.workflowName }}</span>
                        <span :class="item.success ? 'text-emerald-600' : 'text-red-600'">{{ item.success ? (item.rolledBack ? '已回滚' : '可迁移') : '失败' }}</span>
                        <span class="flex-1 truncate text-muted-foreground"
                            :title="item.portChanges?.map(change => `${change.nodeId}: ${change.oldVariable} (${change.oldPort}) → ${change.newExpression}`).join('\n')">
                            {{ item.diagnostic || item.changes.join('；') }}
                            <template v-if="item.portChanges?.length">（{{ item.portChanges.length }} 个端口引用）</template>
                        </span>
                        <Button v-if="migrationBatch && item.success && !item.rolledBack" size="small" severity="danger" text @click="rollbackMigration(item.workflowId)">回滚</Button>
                    </div>
                    <div v-if="migrationItems.length === 0" class="p-3 text-muted-foreground">没有需要迁移的工作流。</div>
                </div>
            </div>
        </header>

        <div class="flex h-9 flex-none items-center overflow-x-auto border-b bg-muted/20 px-3 text-xs">
            <Button
                v-for="view in ['split', 'graph', 'source', 'json'] as const"
                :key="view"
                size="small"
                text
                severity="secondary"
                class="h-full rounded-none border-b-2 px-3"
                :class="activeView === view ? 'border-primary text-primary' : 'border-transparent'"
                @click="activeView = view"
            >
                {{ { split: '分屏', graph: '画布', source: '脚本', json: 'JSON' }[view] }}
            </Button>
            <span v-if="selectedNodeId" class="ml-auto">
                节点：{{ selectedNodeId }}
                <strong v-if="selectedPort" class="ml-2 text-primary">
                    {{ selectedPort.direction === 'input' ? '入参' : '出参' }}：{{ selectedPort.name }}
                </strong>
            </span>
        </div>

        <main
            class="grid min-h-0 flex-1"
            :class="activeView === 'split' ? 'grid-cols-1 grid-rows-2 lg:grid-cols-2 lg:grid-rows-1' : 'grid-cols-1'"
        >
            <section
                v-show="activeView === 'split' || activeView === 'graph'"
                class="relative min-h-0 overflow-hidden border-b lg:border-b-0 lg:border-r"
            >
                <VueFlow
                    :nodes="graphNodes"
                    :edges="graphEdges"
                    fit-view-on-init
                    @node-click="selectGraphNode"
                    @node-double-click="({ node }) => toggleBreakpoint(node.id)"
                >
                    <Background />
                    <Controls />
                </VueFlow>
                <div
                    class="absolute left-3 top-3 max-w-[calc(100%-1.5rem)] rounded border bg-background/90 px-2 py-1 text-xs shadow-sm backdrop-blur"
                >
                    双击节点切换断点 · 红框为断点 · 绿色为当前节点
                </div>
                <div v-if="selectedPort"
                    class="absolute bottom-3 left-3 rounded border-2 border-primary bg-background px-3 py-2 text-xs font-semibold text-primary shadow">
                    {{ selectedPort.direction === 'input' ? '输入端口' : '输出端口' }} · {{ selectedPort.name }}
                </div>
                <div
                    v-if="breakpointNodes.length"
                    class="absolute right-3 top-3 rounded bg-background/90 px-2 py-1 text-xs text-red-500 shadow"
                >
                    ● {{ breakpointNodes.length }} 个断点
                </div>
            </section>
            <section v-show="activeView === 'split' || activeView === 'source'" class="min-h-0 overflow-hidden">
                <div ref="editorHost" class="h-full w-full" />
            </section>
            <section v-if="activeView === 'json'" class="flex min-h-0 flex-col p-3">
                <Textarea v-model="jsonText" class="min-h-0 flex-1 resize-none font-mono text-xs" />
                <div class="mt-2 flex justify-end">
                    <Button size="small" text severity="secondary" class="" @click="applyJson">
                        <Code2 class="size-4" />
                        生成候选脚本
                    </Button>
                </div>
            </section>
        </main>

        <div v-if="operatorDocsOpen" class="plc-trigger-mask" @click.self="operatorDocsOpen = false">
            <aside class="plc-trigger-drawer operator-docs-drawer">
                <header class="drawer-header">
                    <div>
                        <div class="font-medium">算子参数配置说明</div>
                        <div class="text-xs text-muted-foreground">入参、配置参数和出参的连接与填写指南</div>
                    </div>
                    <div class="ml-auto flex gap-1">
                        <Button size="small" text severity="secondary" class="h-7" @click="openOperatorDocs">
                            刷新
                        </Button>
                        <Button size="small" text severity="secondary" class="h-7" @click="operatorDocsOpen = false">
                            关闭
                        </Button>
                    </div>
                </header>
                <div class="operator-docs-body">
                    <section class="operator-docs-list">
                        <InputText
                            v-model.trim="operatorDocsSearch"
                            size="small"
                            class="w-full"
                            placeholder="搜索算子"
                        />
                        <div v-if="operatorDocsLoading" class="p-3 text-sm text-muted-foreground">
                            正在加载算子说明…
                        </div>
                        <Button
                            size="small"
                            text
                            severity="secondary"
                            v-for="operator in filteredOperatorDefinitions"
                            :key="operator.id"
                            class="operator-docs-item"
                            :class="{ active: selectedOperator?.id === operator.id }"
                            @click="selectedOperator = operator"
                        >
                            <span class="font-medium">{{ operator.displayName }}</span>
                            <span class="line-clamp-2 text-xs text-muted-foreground">
                                {{ operator.description || '暂无算子功能说明' }}
                            </span>
                        </Button>
                        <div
                            v-if="!operatorDocsLoading && filteredOperatorDefinitions.length === 0"
                            class="p-3 text-sm text-muted-foreground"
                        >
                            没有匹配的算子。
                        </div>
                    </section>
                    <section v-if="selectedOperator" class="operator-docs-detail">
                        <h2 class="text-lg font-semibold">{{ selectedOperator.displayName }}</h2>
                        <p class="mt-1 text-sm text-muted-foreground">
                            {{ selectedOperator.description || '暂无算子功能说明' }}
                        </p>
                        <template
                            v-for="group in [
                                { title: '入参', empty: '无入参', items: selectedOperator.inputPorts, config: false },
                                {
                                    title: '配置参数',
                                    empty: '无配置参数',
                                    items: selectedOperator.configFields,
                                    config: true,
                                },
                                { title: '出参', empty: '无出参', items: selectedOperator.outputPorts, config: false },
                            ]"
                            :key="group.title"
                        >
                            <h3 class="operator-docs-section-title">{{ group.title }}</h3>
                            <div v-if="group.items.length === 0" class="text-sm text-muted-foreground">
                                {{ group.empty }}
                            </div>
                            <article
                                v-for="parameter in group.items"
                                :key="parameter.name"
                                class="operator-parameter-card"
                            >
                                <div class="flex flex-wrap items-center gap-2">
                                    <strong>{{ parameter.displayName || parameter.name }}</strong>
                                    <code>{{ parameter.name }}</code>
                                    <span
                                        v-if="group.config && 'required' in parameter"
                                        class="text-xs"
                                        :class="parameter.required ? 'text-red-600' : 'text-muted-foreground'"
                                    >
                                        {{ parameter.required ? '必填' : '可选' }}
                                    </span>
                                </div>
                                <p class="mt-2 whitespace-pre-wrap text-sm leading-6">
                                    {{ parameter.description || '暂无配置说明' }}
                                </p>
                                <div class="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                                    <span>
                                        类型：{{
                                            'portTypeName' in parameter
                                                ? parameter.portTypeName
                                                : parameter.valueTypeName
                                        }}
                                    </span>
                                    <span v-if="metadataText(parameter.defaultValue)">
                                        默认值：{{ metadataText(parameter.defaultValue) }}
                                    </span>
                                    <span v-if="metadataText(parameter.valueLimit)">
                                        范围/选项：{{ metadataText(parameter.valueLimit) }}
                                    </span>
                                </div>
                            </article>
                        </template>
                    </section>
                    <section v-else class="grid flex-1 place-items-center text-sm text-muted-foreground">
                        请选择一个算子查看配置说明。
                    </section>
                </div>
            </aside>
        </div>

        <div v-if="taskDrawerOpen" class="plc-trigger-mask" @click.self="taskDrawerOpen = false">
            <aside class="plc-trigger-drawer task-config-drawer">
                <header class="drawer-header">
                    <div class="min-w-0">
                        <div class="font-medium">任务配置与 PLC 握手</div>
                        <div class="truncate text-xs text-muted-foreground">选择工作流并保存任务配置后直接生效</div>
                    </div>
                    <div class="ml-auto flex shrink-0 gap-1">
                        <Button size="small" severity="secondary" text @click="openTaskDrawer">刷新</Button>
                        <Button size="small" severity="secondary" text @click="taskDrawerOpen = false">关闭</Button>
                    </div>
                </header>
                <div v-if="taskLoading" class="p-6 text-sm text-muted-foreground">加载中…</div>
                <div v-else class="min-h-0 flex-1 space-y-4 overflow-auto p-4">
                    <section v-if="taskConfig" class="rounded border p-4">
                        <div class="mb-3 font-medium">配置任务工作流</div>
                        <div class="grid grid-cols-1 gap-3 text-xs sm:grid-cols-2">
                            <label>
                                执行方式
                                <Select
                                    v-model="taskConfig.taskType"
                                    size="small"
                                    class="mt-1 w-full"
                                    :options="[
                                        { label: '立即/外部触发', value: 0 },
                                        { label: '周期执行', value: 1 },
                                    ]"
                                    option-label="label"
                                    option-value="value"
                                />
                            </label>
                            <label v-if="taskConfig.taskType === 1">
                                周期（秒）
                                <InputNumber
                                    v-model="taskConfig.cycleIntervalSeconds"
                                    size="small"
                                    :min="1"
                                    class="mt-1 w-full"
                                    :use-grouping="false"
                                />
                            </label>
                        </div>
                        <div class="mt-3 space-y-2">
                            <div
                                v-for="(item, index) in taskConfig.items"
                                :key="item.workflowId"
                                class="flex items-center gap-2 rounded border px-3 py-2 text-xs"
                            >
                                <Checkbox v-model="item.isEnabled" binary />
                                <span class="min-w-0 flex-1 truncate">{{ index + 1 }}. {{ item.workflowName }}</span>
                                <Button
                                    size="small"
                                    severity="secondary"
                                    text
                                    :disabled="index === 0"
                                    aria-label="上移"
                                    @click="moveTask(index, -1)"
                                >
                                    ↑
                                </Button>
                                <Button
                                    size="small"
                                    severity="secondary"
                                    text
                                    :disabled="index === taskConfig.items.length - 1"
                                    aria-label="下移"
                                    @click="moveTask(index, 1)"
                                >
                                    ↓
                                </Button>
                            </div>
                        </div>
                        <div class="mt-3 grid grid-cols-1 gap-3 text-xs sm:grid-cols-2">
                            <label>
                                最终判定工作流
                                <Select
                                    v-model="taskConfig.resultWorkflowId"
                                    size="small"
                                    class="mt-1 w-full"
                                    :options="taskConfig.items.filter((x) => x.isEnabled)"
                                    option-label="workflowName"
                                    option-value="workflowId"
                                    placeholder="请选择"
                                    @change="loadResultOutputs"
                                />
                            </label>
                            <label>
                                布尔输出变量
                                <Select
                                    v-model="taskConfig.resultVariableName"
                                    size="small"
                                    :disabled="!taskConfig.resultWorkflowId"
                                    class="mt-1 w-full"
                                    :options="resultOutputs"
                                    placeholder="请选择 Boolean 输出"
                                />
                            </label>
                        </div>
                        <Button size="small" class="mt-3" :loading="taskSaving" @click="saveTaskConfig">
                            保存任务配置
                        </Button>
                    </section>

                    <section class="rounded border p-4">
                        <div class="flex items-center gap-2">
                            <div>
                                <div class="font-medium">配置 PLC 握手点位</div>
                                <div class="text-xs text-muted-foreground">握手点位单独保存，不会被任务配置覆盖</div>
                            </div>
                            <span
                                class="ml-auto rounded-full px-2 py-1 text-xs"
                                :class="
                                    handshakeStatus?.isEnabled
                                        ? 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400'
                                        : 'bg-muted text-muted-foreground'
                                "
                            >
                                {{
                                    handshakeStatus?.isConfigured
                                        ? handshakeStatus.isEnabled
                                            ? '运行中'
                                            : '已停用'
                                        : '未配置'
                                }}
                            </span>
                        </div>
                        <label class="mt-3 block text-xs">
                            PLC 设备
                            <Select
                                v-model="handshakeForm.plcDeviceId"
                                size="small"
                                class="mt-1 w-full"
                                :options="plcDevices"
                                option-label="name"
                                option-value="id"
                                placeholder="请选择 PLC"
                                @change="onHandshakeDeviceChange"
                            />
                        </label>
                        <p class="mt-2 text-[10px] text-muted-foreground">
                            握手点位来自 OPC UA 节点树，与 PLC 调试页的实时点位表相互独立。
                            <span v-if="handshakeTreeLoading">正在加载节点树…</span>
                            <span v-else-if="handshakeTreeTruncated" class="text-amber-600">
                                节点树达到服务端限制，仅显示部分节点。
                            </span>
                        </p>
                        <div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
                            <label v-for="field in handshakeFields" :key="field[0]" class="text-xs">
                                {{ field[1] }}
                                <Select
                                    v-model="handshakeForm[field[0]]"
                                    size="small"
                                    :disabled="!handshakeForm.plcDeviceId || handshakeTreeLoading"
                                    class="mt-1 w-full font-mono"
                                    :options="handshakeVariableOptions"
                                    option-label="label"
                                    option-value="address"
                                    placeholder="请选择 OPC UA 节点"
                                    :title="field[2]"
                                    editable
                                />
                                <span class="mt-0.5 block text-[10px] text-muted-foreground">{{ field[2] }}</span>
                            </label>
                        </div>
                        <label class="mt-3 flex items-center gap-2 text-xs">
                            <Checkbox v-model="handshakeForm.isEnabled" binary />
                            启用 PLC 握手
                        </label>
                        <Button
                            size="small"
                            class="mt-3"
                            :loading="taskSaving"
                            :disabled="!handshakeForm.plcDeviceId"
                            @click="saveHandshake"
                        >
                            保存握手配置
                        </Button>
                    </section>

                    <section class="rounded border p-4 text-xs">
                        <div class="flex items-center">
                            <div class="font-medium text-sm">运行状态</div>
                            <Button size="small" severity="secondary" outlined class="ml-auto" @click="resetHandshake">
                                复位握手
                            </Button>
                        </div>
                        <div class="mt-2 grid grid-cols-2 gap-2 text-muted-foreground sm:grid-cols-3">
                            <span>阶段：{{ handshakeStatus?.phase ?? '-' }}</span>
                            <span>当前请求：{{ handshakeStatus?.currentRequestId ?? '-' }}</span>
                            <span>已完成：{{ handshakeStatus?.lastCompletedRequestId ?? '-' }}</span>
                            <span>结果：{{ handshakeStatus?.resultCode ?? '-' }}</span>
                            <span>错误：{{ handshakeStatus?.errorCode ?? '-' }}</span>
                            <span class="col-span-2 break-all sm:col-span-3">
                                运行 ID：{{ handshakeStatus?.currentRunId ?? '-' }}
                            </span>
                        </div>
                        <div v-if="handshakeStatus?.lastError" class="mt-2 text-destructive">
                            {{ handshakeStatus.lastError }}
                        </div>
                    </section>
                </div>
            </aside>
        </div>

        <div v-if="versionHistoryOpen" class="plc-trigger-mask" @click.self="versionHistoryOpen = false">
            <aside class="plc-trigger-drawer">
                <header class="drawer-header">
                    <div>
                        <div class="font-medium">版本历史</div>
                        <div class="text-xs text-muted-foreground">高级部署与回滚记录</div>
                    </div>
                    <Button size="small" severity="secondary" text class="ml-auto" @click="versionHistoryOpen = false">
                        关闭
                    </Button>
                </header>
                <div class="min-h-0 flex-1 space-y-2 overflow-auto p-4">
                    <div v-for="item in deploymentHistory" :key="item.id" class="rounded border p-3 text-xs">
                        <div class="flex items-center">
                            <strong>R{{ item.revision }}</strong>
                            <span class="ml-2 rounded bg-muted px-2 py-0.5">
                                {{ item.isCurrentActive ? '当前生效' : item.status === 3 ? '已归档' : '已发布' }}
                            </span>
                            <span class="ml-auto">
                                {{ item.taskType === 1 ? `周期 ${item.cycleIntervalSeconds}s` : '立即/外部触发' }}
                            </span>
                        </div>
                        <div class="mt-2 flex items-center text-muted-foreground">
                            <span>
                                结果：{{ item.resultVariableName || '未配置' }} · 错误策略：{{
                                    item.onErrorAction === 1 ? '继续' : '停止'
                                }}
                            </span>
                            <Button
                                v-if="item.canRollback"
                                size="small"
                                severity="secondary"
                                outlined
                                class="ml-auto"
                                @click="rollbackToVersion(item)"
                            >
                                回滚
                            </Button>
                        </div>
                    </div>
                    <div v-if="!deploymentHistory.length" class="py-8 text-center text-sm text-muted-foreground">
                        暂无应用记录
                    </div>
                </div>
            </aside>
        </div>

        <div v-if="plcTriggerDrawerOpen" class="plc-trigger-mask" @click.self="plcTriggerDrawerOpen = false">
            <aside class="plc-trigger-drawer">
                <header class="drawer-header">
                    <div class="min-w-0">
                        <div class="font-medium">PLC 触发</div>
                        <div class="truncate text-xs text-muted-foreground">当前项目的正式任务触发映射</div>
                    </div>
                    <div class="ml-auto flex shrink-0 gap-1">
                        <Button size="small" text severity="secondary" class="h-7" @click="openPlcTriggerDrawer">
                            刷新
                        </Button>
                        <Button
                            size="small"
                            text
                            severity="secondary"
                            class="h-7"
                            @click="plcTriggerDrawerOpen = false"
                        >
                            关闭
                        </Button>
                    </div>
                </header>
                <div class="min-h-0 flex-1 overflow-auto p-4">
                    <form class="space-y-3 rounded border bg-muted/20 p-3" @submit.prevent="savePlcTrigger">
                        <div class="flex items-center">
                            <span class="font-medium text-sm">{{ editingPlcTriggerId ? '编辑触发' : '新增触发' }}</span>
                            <Button
                                size="small"
                                text
                                severity="secondary"
                                v-if="editingPlcTriggerId"
                                type="button"
                                class="ml-auto text-xs text-primary"
                                @click="resetPlcTriggerForm"
                            >
                                取消编辑
                            </Button>
                        </div>
                        <label class="block text-xs">
                            PLC
                            <Select
                                v-model="plcTriggerForm.plcDeviceId"
                                size="small"
                                :disabled="!!editingPlcTriggerId"
                                class="mt-1 w-full"
                                :options="plcDevices"
                                option-label="name"
                                option-value="id"
                                placeholder="选择 PLC"
                                @change="loadPlcTags"
                            />
                        </label>
                        <label class="block text-xs">
                            点位
                            <Select
                                v-model="plcTriggerForm.plcTagId"
                                size="small"
                                :disabled="!!editingPlcTriggerId || !plcTriggerForm.plcDeviceId"
                                class="mt-1 w-full"
                                :options="plcTags"
                                option-label="name"
                                option-value="id"
                                placeholder="选择点位"
                            >
                                <template #option="{ option }">{{ option.name }} · {{ option.code }}</template>
                            </Select>
                        </label>
                        <label v-if="selectedPlcIsBoolean" class="block text-xs">
                            目标值
                            <Select
                                v-model="plcTriggerForm.value"
                                size="small"
                                class="mt-1 w-full"
                                :options="['true', 'false']"
                            />
                        </label>
                        <label v-else class="block text-xs">
                            目标值
                            <InputText
                                v-model="plcTriggerForm.value"
                                size="small"
                                :type="selectedPlcIsNumeric ? 'number' : selectedPlcIsDate ? 'datetime-local' : 'text'"
                                :step="selectedPlcIsFloat ? 'any' : undefined"
                                class="mt-1 w-full"
                                :placeholder="
                                    selectedPlcTag?.dataType === PlcTagDataType.ByteString ? 'Base64' : '输入目标值'
                                "
                            />
                        </label>
                        <label v-if="selectedPlcIsFloat" class="block text-xs">
                            浮点容差
                            <InputNumber
                                v-model="plcTriggerForm.tolerance"
                                size="small"
                                :min="0"
                                :max-fraction-digits="8"
                                class="mt-1 w-full"
                                :use-grouping="false"
                            />
                        </label>
                        <label class="flex items-center gap-2 text-xs">
                            <Checkbox v-model="plcTriggerForm.isEnabled" binary />
                            启用此触发
                        </label>
                        <Button
                            size="small"
                            text
                            severity="secondary"
                            type="submit"
                            class="w-full justify-center"
                            :disabled="plcTriggerSaving || !selectedPlcTag"
                        >
                            {{ plcTriggerSaving ? '保存中…' : '保存触发' }}
                        </Button>
                        <section v-if="selectedPlcTag" class="rounded border border-dashed bg-background p-3">
                            <div class="flex items-center gap-2">
                                <div>
                                    <div class="text-xs font-medium">调试配置</div>
                                    <div class="text-[10px] text-muted-foreground">
                                        直接读写所选点位，用于保存前联调 PLC 触发条件
                                    </div>
                                </div>
                                <span class="ml-auto rounded bg-muted px-1.5 py-0.5 text-[10px]">
                                    {{ selectedPlcCanRead ? '可读' : '不可读' }} /
                                    {{ selectedPlcCanWrite ? '可写' : '不可写' }}
                                </span>
                            </div>
                            <div
                                class="mt-2 grid grid-cols-1 items-center gap-2 sm:grid-cols-[minmax(0,1fr)_auto_auto]"
                            >
                                <div
                                    class="min-w-0 rounded bg-muted px-2 py-1.5 font-mono text-xs"
                                    :title="plcDebugValueText(plcTriggerDebugValue?.engineeringValue)"
                                >
                                    当前值：
                                    <span class="break-all">
                                        {{ plcDebugValueText(plcTriggerDebugValue?.engineeringValue) }}
                                    </span>
                                    <span v-if="plcTriggerDebugValue" class="ml-1 text-[10px] text-muted-foreground">
                                        {{ plcTriggerDebugValue.quality }}
                                    </span>
                                </div>
                                <Button
                                    size="small"
                                    text
                                    severity="secondary"
                                    type="button"
                                    class="h-8"
                                    :disabled="plcTriggerDebugging || !selectedPlcCanRead"
                                    @click="readPlcTriggerDebugValue"
                                >
                                    读取
                                </Button>
                                <Button
                                    size="small"
                                    text
                                    severity="secondary"
                                    type="button"
                                    class="h-8"
                                    :disabled="
                                        plcTriggerDebugging || !selectedPlcCanWrite || plcTriggerForm.value === ''
                                    "
                                    title="将上面的目标值直接写入 PLC 点位"
                                    @click="writePlcTriggerDebugValue"
                                >
                                    写入目标值
                                </Button>
                            </div>
                            <div v-if="!selectedPlcCanWrite" class="mt-2 text-[10px] text-amber-600">
                                该点位不可写，只能读取现场值；请由 PLC 侧改变点位进行触发测试。
                            </div>
                        </section>
                    </form>
                    <div class="mt-5">
                        <div class="mb-2 text-sm font-medium">已配置触发</div>
                        <div v-if="plcTriggerLoading" class="text-xs text-muted-foreground">加载中…</div>
                        <div
                            v-else-if="!plcTriggers.length"
                            class="rounded border border-dashed p-4 text-center text-xs text-muted-foreground"
                        >
                            尚未配置 PLC 触发。
                        </div>
                        <article
                            v-for="trigger in plcTriggers"
                            :key="trigger.id"
                            class="mb-2 rounded border bg-card p-3 text-xs shadow-sm"
                        >
                            <div class="flex flex-wrap items-center gap-2">
                                <span class="font-mono text-sm font-semibold">
                                    = {{ triggerValueDisplay(trigger) }}
                                </span>
                                <span
                                    class="rounded-full px-2 py-0.5"
                                    :class="
                                        trigger.isEnabled
                                            ? 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-400'
                                            : 'bg-muted text-muted-foreground'
                                    "
                                >
                                    {{ trigger.isEnabled ? '已启用' : '已停用' }}
                                </span>
                                <div class="ml-auto flex gap-2">
                                    <Button
                                        size="small"
                                        text
                                        severity="secondary"
                                        class="text-primary hover:underline"
                                        @click="editPlcTrigger(trigger)"
                                    >
                                        编辑
                                    </Button>
                                    <Button
                                        size="small"
                                        text
                                        severity="secondary"
                                        class="text-primary hover:underline"
                                        @click="togglePlcTrigger(trigger)"
                                    >
                                        {{ trigger.isEnabled ? '停用' : '启用' }}
                                    </Button>
                                    <Button
                                        size="small"
                                        text
                                        severity="secondary"
                                        class="text-destructive hover:underline"
                                        @click="removePlcTrigger(trigger)"
                                    >
                                        删除
                                    </Button>
                                </div>
                            </div>
                            <div class="mt-2 break-all text-muted-foreground">
                                {{ plcDeviceName(trigger.plcDeviceId) }} · {{ plcTagName(trigger) }}
                                <template v-if="trigger.tolerance">· 容差 {{ trigger.tolerance }}</template>
                            </div>
                            <div v-if="trigger.lastTriggeredAt" class="mt-1 text-muted-foreground">
                                上次触发：{{ new Date(trigger.lastTriggeredAt).toLocaleString() }}
                            </div>
                            <div v-if="trigger.lastSkipReason" class="mt-1 text-amber-600">
                                最近跳过：{{ trigger.lastSkipReason }}
                            </div>
                        </article>
                    </div>
                </div>
            </aside>
        </div>

        <section class="h-[clamp(10rem,24vh,15rem)] flex-none border-t bg-card/40">
            <div class="flex h-8 items-center overflow-x-auto border-b px-2 text-xs">
                <Button
                    size="small"
                    text
                    severity="secondary"
                    v-for="tab in [
                        'problems',
                        'variables',
                        'watch',
                        'trace',
                        'performance',
                        'results',
                        'output',
                    ] as const"
                    :key="tab"
                    class="h-full px-3"
                    :class="bottomTab === tab && 'text-primary'"
                    @click="bottomTab = tab"
                >
                    {{
                        {
                            problems: `问题 (${problems.length})`,
                            variables: '变量/栈',
                            watch: '监视',
                            trace: '跟踪',
                            performance: '性能',
                            results: `结果 (${debugStatus?.outputs?.length ?? 0})`,
                            output: '输出日志',
                        }[tab]
                    }}
                </Button>
                <Button
                    size="small"
                    text
                    severity="secondary"
                    v-if="bottomTab === 'watch'"
                    class="ml-auto h-6"
                    @click="refreshWatch"
                >
                    刷新
                </Button>
                <Button
                    size="small"
                    text
                    severity="secondary"
                    v-if="bottomTab === 'trace' || bottomTab === 'performance'"
                    class="ml-auto h-6"
                    @click="refreshDebugDetails"
                >
                    刷新
                </Button>
            </div>
            <div class="h-[calc(100%-2rem)] overflow-auto p-2 text-xs">
                <template v-if="bottomTab === 'problems'">
                    <Button
                        size="small"
                        text
                        severity="secondary"
                        v-for="problem in problems"
                        :key="`${problem.code}-${problem.range.start.offset}`"
                        type="button"
                        class="flex w-full gap-3 rounded px-2 py-1 text-left hover:bg-muted"
                        @click="selectProblem(problem)"
                    >
                        <span :class="problem.severity === 'warning' ? 'text-amber-500' : 'text-red-500'">
                            {{ problem.code }}
                        </span>
                        <span>{{ problem.message }}</span>
                        <span class="ml-auto">Ln {{ problem.range.start.line }}</span>
                    </Button>
                </template>
                <template v-else-if="bottomTab === 'variables'">
                    <div class="mb-1 font-medium">变量</div>
                    <table class="debug-table">
                        <thead>
                            <tr>
                                <th
                                    v-for="column in tableColumns(debugStatus?.variables ?? [], [
                                        'name',
                                        'typeName',
                                        'value',
                                        'valueType',
                                    ])"
                                    :key="column"
                                >
                                    {{ column }}
                                </th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="(row, index) in debugStatus?.variables ?? []" :key="index">
                                <td
                                    v-for="column in tableColumns(debugStatus?.variables ?? [], [
                                        'name',
                                        'typeName',
                                        'value',
                                        'valueType',
                                    ])"
                                    :key="column"
                                >
                                    {{ formatCell(row[column]) }}
                                </td>
                            </tr>
                            <tr v-if="!debugStatus?.variables?.length">
                                <td class="text-muted-foreground">暂无变量</td>
                            </tr>
                        </tbody>
                    </table>
                    <div class="mb-1 mt-3 font-medium">调用栈</div>
                    <table class="debug-table">
                        <thead>
                            <tr>
                                <th
                                    v-for="column in tableColumns(stackResults, [
                                        'depth',
                                        'workflowName',
                                        'nodeId',
                                        'statementId',
                                    ])"
                                    :key="column"
                                >
                                    {{ column }}
                                </th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="(row, index) in stackResults" :key="index">
                                <td
                                    v-for="column in tableColumns(stackResults, [
                                        'depth',
                                        'workflowName',
                                        'nodeId',
                                        'statementId',
                                    ])"
                                    :key="column"
                                >
                                    {{ formatCell(row[column]) }}
                                </td>
                            </tr>
                            <tr v-if="!stackResults.length"><td class="text-muted-foreground">暂无调用栈</td></tr>
                        </tbody>
                    </table>
                </template>
                <table v-else-if="bottomTab === 'watch'" class="debug-table">
                    <thead>
                        <tr>
                            <th
                                v-for="column in tableColumns(watchResults, [
                                    'expression',
                                    'success',
                                    'value',
                                    'valueType',
                                    'error',
                                ])"
                                :key="column"
                            >
                                {{ column }}
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="(row, index) in watchResults" :key="index">
                            <td
                                v-for="column in tableColumns(watchResults, [
                                    'expression',
                                    'success',
                                    'value',
                                    'valueType',
                                    'error',
                                ])"
                                :key="column"
                            >
                                {{ formatCell(row[column]) }}
                            </td>
                        </tr>
                        <tr v-if="!watchResults.length"><td class="text-muted-foreground">暂无监视结果</td></tr>
                    </tbody>
                </table>
                <table v-else-if="bottomTab === 'trace'" class="debug-table">
                    <thead>
                        <tr>
                            <th
                                v-for="column in tableColumns(traceResults, [
                                    'sequence',
                                    'timestamp',
                                    'eventType',
                                    'nodeId',
                                    'message',
                                ])"
                                :key="column"
                            >
                                {{ column }}
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="(row, index) in traceResults" :key="index">
                            <td
                                v-for="column in tableColumns(traceResults, [
                                    'sequence',
                                    'timestamp',
                                    'eventType',
                                    'nodeId',
                                    'message',
                                ])"
                                :key="column"
                            >
                                {{ formatCell(row[column]) }}
                            </td>
                        </tr>
                        <tr v-if="!traceResults.length"><td class="text-muted-foreground">暂无跟踪事件</td></tr>
                    </tbody>
                </table>
                <table v-else-if="bottomTab === 'performance'" class="debug-table">
                    <thead>
                        <tr>
                            <th
                                v-for="column in tableColumns(performanceResults, [
                                    'nodeId',
                                    'nodeTitle',
                                    'executionCount',
                                    'totalElapsedMs',
                                    'averageElapsedMs',
                                    'maxElapsedMs',
                                ])"
                                :key="column"
                            >
                                {{ column }}
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="(row, index) in performanceResults" :key="index">
                            <td
                                v-for="column in tableColumns(performanceResults, [
                                    'nodeId',
                                    'nodeTitle',
                                    'executionCount',
                                    'totalElapsedMs',
                                    'averageElapsedMs',
                                    'maxElapsedMs',
                                ])"
                                :key="column"
                            >
                                {{ formatCell(row[column]) }}
                            </td>
                        </tr>
                        <tr v-if="!performanceResults.length"><td class="text-muted-foreground">暂无性能数据</td></tr>
                    </tbody>
                </table>
                <template v-else-if="bottomTab === 'results'">
                    <div class="mb-2 flex flex-wrap gap-x-5 gap-y-1 rounded bg-muted/40 px-3 py-2">
                        <span>状态：{{ debugStateInfo.label }}</span>
                        <span>进度：{{ debugStatus?.executedSteps ?? 0 }}/{{ debugStatus?.totalSteps ?? 0 }}</span>
                        <span>耗时：{{ formatDuration(debugStatus?.durationMs ?? 0) }}</span>
                        <span v-if="executionId">执行 ID：{{ executionId }}</span>
                    </div>
                    <div
                        v-if="!debugResults.length"
                        class="rounded border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-amber-700 dark:text-amber-400"
                    >
                        工作流已执行完成，但结束节点未设置输出。
                    </div>
                    <table v-else class="debug-table">
                        <thead>
                            <tr>
                                <th>Display 名称</th>
                                <th>变量名称</th>
                                <th>值类型</th>
                                <th>值</th>
                            </tr>
                        </thead>
                        <tbody>
                            <tr v-for="result in debugResults" :key="result.name">
                                <td>{{ result.displayName || result.name }}</td>
                                <td>{{ result.name }}</td>
                                <td>{{ result.valueType || '—' }}</td>
                                <td>
                                    <span
                                        v-if="resultPresentation(result) === 'boolean'"
                                        :class="result.value === true ? 'text-emerald-600' : 'text-red-600'"
                                        class="font-semibold"
                                        :title="`原始值：${String(result.value)}`"
                                    >
                                        {{ result.value === true ? 'OK' : 'NG' }}
                                    </span>
                                    <span
                                        v-else-if="resultPresentation(result) === 'unsupported-binary'"
                                        class="text-amber-600"
                                    >
                                        请先使用存 Blob 算子，再将 Blob Key 绑定到结束节点
                                    </span>
                                    <div
                                        v-else-if="isFilePresentation(resultPresentation(result))"
                                        class="flex items-center gap-2"
                                    >
                                        <span class="max-w-[32rem] truncate" :title="String(result.value)">
                                            {{ workflowResultFileName(result.value) }}
                                        </span>
                                        <Button
                                            size="small"
                                            text
                                            severity="secondary"
                                            class=""
                                            type="button"
                                            @click="openResultPreview(result)"
                                        >
                                            {{
                                                previewResult?.name === result.name
                                                    ? '收起'
                                                    : resultPresentation(result) === 'cad' ||
                                                        resultPresentation(result) === 'file'
                                                      ? '下载'
                                                      : '查看'
                                            }}
                                        </Button>
                                    </div>
                                    <div
                                        v-else-if="
                                            resultPresentation(result) === 'object' ||
                                            resultPresentation(result) === 'array'
                                        "
                                        class="max-h-40 min-w-64 overflow-auto"
                                    >
                                        <WorkflowJsonTree :value="result.value" />
                                    </div>
                                    <span v-else>{{ formatCell(result.value) }}</span>
                                </td>
                            </tr>
                        </tbody>
                    </table>
                    <WorkflowBlobPreview
                        v-if="
                            previewResult &&
                            typeof previewResult.value === 'string' &&
                            isFilePresentation(resultPresentation(previewResult))
                        "
                        class="mt-3"
                        :blob-key="previewResult.value"
                        :presentation="resultPresentation(previewResult)"
                    />
                </template>
                <div v-else class="font-mono" v-for="(line, index) in output" :key="index">{{ line }}</div>
            </div>
        </section>
    </div>
</template>

<style scoped>
.ide-action-bar {
    display: flex;
    min-height: 2.6rem;
    align-items: center;
    gap: 0.5rem;
    overflow-x: auto;
    border-top: 1px solid hsl(var(--border) / 55%);
    padding: 0.3rem 0.75rem;
    scrollbar-width: thin;
}
.ide-action-group {
    display: flex;
    flex: 0 0 auto;
    align-items: center;
    gap: 0.15rem;
}
.ide-action-group + .ide-action-group {
    border-left: 1px solid hsl(var(--border));
    padding-left: 0.5rem;
}
.plc-trigger-mask {
    position: fixed;
    inset: 0;
    z-index: 60;
    display: flex;
    justify-content: flex-end;
    background: rgb(15 23 42 / 38%);
    backdrop-filter: blur(2px);
}
.plc-trigger-drawer {
    display: flex;
    height: 100%;
    width: min(30rem, 100vw);
    min-width: 0;
    flex-direction: column;
    border-left: 1px solid hsl(var(--border));
    background: hsl(var(--background));
    box-shadow: -12px 0 30px rgb(15 23 42 / 20%);
}
.task-config-drawer {
    width: min(56rem, 100vw);
}
.operator-docs-drawer {
    width: min(72rem, 100vw);
}
.operator-docs-body {
    display: flex;
    min-height: 0;
    flex: 1;
    overflow: hidden;
}
.operator-docs-list {
    width: 18rem;
    flex: none;
    overflow-y: auto;
    border-right: 1px solid hsl(var(--border));
    padding: 0.75rem;
}
.operator-docs-item {
    display: flex;
    width: 100%;
    flex-direction: column;
    gap: 0.25rem;
    border-radius: 0.375rem;
    padding: 0.625rem;
    text-align: left;
}
.operator-docs-item:hover,
.operator-docs-item.active {
    background: hsl(var(--accent));
    color: hsl(var(--accent-foreground));
}
.operator-docs-detail {
    min-width: 0;
    flex: 1;
    overflow-y: auto;
    padding: 1rem 1.25rem 2rem;
}
.operator-docs-section-title {
    margin: 1.25rem 0 0.5rem;
    border-bottom: 1px solid hsl(var(--border));
    padding-bottom: 0.375rem;
    font-weight: 600;
}
.operator-parameter-card {
    margin-top: 0.5rem;
    border: 1px solid hsl(var(--border));
    border-radius: 0.5rem;
    background: hsl(var(--card));
    padding: 0.75rem;
}
.drawer-header {
    display: flex;
    min-height: 3.5rem;
    flex: 0 0 auto;
    align-items: center;
    gap: 0.5rem;
    border-bottom: 1px solid hsl(var(--border));
    background: hsl(var(--card) / 85%);
    padding: 0.65rem 1rem;
    backdrop-filter: blur(8px);
}
.debug-state-badge {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    border-radius: 9999px;
    padding: 0.25rem 0.55rem;
    font-size: 0.75rem;
    font-weight: 600;
    white-space: nowrap;
}
.debug-state-dot {
    width: 0.5rem;
    height: 0.5rem;
    border-radius: 9999px;
    background: currentColor;
}
.debug-state-idle {
    color: hsl(var(--muted-foreground));
    background: hsl(var(--muted));
}
.debug-state-running {
    color: #2563eb;
    background: rgb(37 99 235 / 14%);
}
.debug-state-running .debug-state-dot {
    animation: workflow-node-pulse 0.7s ease-in-out infinite alternate;
}
.debug-state-paused {
    color: #b45309;
    background: rgb(217 119 6 / 14%);
}
.debug-state-completed {
    color: #15803d;
    background: rgb(22 163 74 / 14%);
}
.debug-state-faulted {
    color: #dc2626;
    background: rgb(220 38 38 / 14%);
}
.debug-state-stopped {
    color: hsl(var(--muted-foreground));
    background: hsl(var(--muted));
}
:global(.dark) .debug-state-running {
    color: #60a5fa;
}
:global(.dark) .debug-state-paused {
    color: #fbbf24;
}
:global(.dark) .debug-state-completed {
    color: #4ade80;
}
:global(.dark) .debug-state-faulted {
    color: #f87171;
}
:deep(.workflow-node-running) {
    box-shadow:
        0 0 0 4px #3b82f6,
        0 0 18px rgb(59 130 246 / 70%);
    animation: workflow-node-pulse 1.2s ease-in-out infinite alternate;
}
:deep(.workflow-node-paused) {
    box-shadow:
        0 0 0 4px #eab308,
        0 0 14px rgb(234 179 8 / 55%);
}
:deep(.workflow-node-completed) {
    box-shadow: 0 0 0 3px #22c55e;
}
:deep(.workflow-node-faulted) {
    box-shadow:
        0 0 0 4px #ef4444,
        0 0 14px rgb(239 68 68 / 60%);
}
:deep(.workflow-node-breakpoint) {
    box-shadow: 0 0 0 3px #ef4444;
}

:deep(.workflow-node-port-selected) {
    border: 3px solid var(--p-primary-color) !important;
    box-shadow: 0 0 0 4px color-mix(in srgb, var(--p-primary-color) 25%, transparent) !important;
}
.debug-table {
    width: max-content;
    min-width: 100%;
    border-collapse: separate;
    border-spacing: 0;
    font-family: ui-monospace, monospace;
}
.debug-table th {
    position: sticky;
    top: 0;
    z-index: 1;
    background: hsl(var(--muted));
    text-align: left;
    font-weight: 600;
}
.debug-table th,
.debug-table td {
    max-width: 32rem;
    border-right: 1px solid hsl(var(--border));
    border-bottom: 1px solid hsl(var(--border));
    padding: 0.25rem 0.4rem;
    overflow-wrap: anywhere;
    vertical-align: top;
}
.debug-table tr > :first-child {
    border-left: 1px solid hsl(var(--border));
}
.debug-table thead tr:first-child > * {
    border-top: 1px solid hsl(var(--border));
}
@media (max-width: 640px) {
    .workflow-ide-shell {
        margin: -1rem;
        height: calc(100vh - 3.5rem);
    }
    .plc-trigger-drawer,
    .task-config-drawer,
    .operator-docs-drawer {
        width: 100vw;
        border-left: 0;
    }
    .operator-docs-list {
        width: 13rem;
    }
    .drawer-header {
        padding-inline: 0.75rem;
    }
    .debug-state-badge {
        order: 5;
    }
}
@keyframes workflow-node-pulse {
    from {
        filter: brightness(1);
    }
    to {
        filter: brightness(1.18);
    }
}
</style>
