<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import * as monaco from 'monaco-editor'
import * as signalR from '@microsoft/signalr'
import { VueFlow, useVueFlow, type Edge, type Node } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import {
    completions,
    continueDebug,
    debugAndRunSource,
    debugSource,
    diagnostics,
    formatSource,
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

self.MonacoEnvironment = {
    getWorker: () =>
        new Worker(
            new URL('../../workers/monaco-editor.worker.ts', import.meta.url),
            { type: 'module' },
        ),
}

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
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
const dirty = ref(false)
const saving = ref(false)
const debugStatus = ref<DebugStatus>()
const debugResults = ref<DebugExecutionResult[]>([])
const previewResult = ref<DebugExecutionResult>()
const executionId = ref('')
const debugActivity = ref<'idle' | 'starting' | 'running' | 'stopped'>('idle')
const breakpointNodes = ref<string[]>([])
const completedNodeIds = ref<string[]>([])
const selectedHasBreakpoint = computed(
    () => !!selectedNodeId.value && breakpointNodes.value.includes(selectedNodeId.value),
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
    () => !!executionId.value && debugActivity.value === 'idle'
        && (debugStatus.value?.canContinue ?? !debugStatus.value?.isTerminal),
)
const canStepDebug = computed(
    () => !!executionId.value && debugActivity.value === 'idle'
        && (debugStatus.value?.canStep ?? !debugStatus.value?.isTerminal),
)
const canPauseDebug = computed(
    () => !!executionId.value && effectiveDebugState.value === 'running',
)
const canStopDebug = computed(
    () => !!executionId.value && (debugStatus.value?.canStop ?? !debugStatus.value?.isTerminal),
)
const watchExpressions = ref<string[]>(['retstatus.status'])
const watchResults = ref<Array<Record<string, unknown>>>([])
const traceResults = ref<Array<Record<string, unknown>>>([])
const stackResults = ref<Array<Record<string, unknown>>>([])
const performanceResults = ref<Array<Record<string, unknown>>>([])
const bottomTab = ref<'problems' | 'variables' | 'watch' | 'trace' | 'performance' | 'results' | 'output'>(
    'problems'
)
const output = ref<string[]>([])
const statusReceivedAt = ref(Date.now())
const clockNow = ref(Date.now())
const displayDurationMs = computed(() =>
    (debugStatus.value?.durationMs ?? 0)
    + (effectiveDebugState.value === 'running' ? Math.max(0, clockNow.value - statusReceivedAt.value) : 0),
)
const displayCurrentNodeDurationMs = computed(() =>
    (debugStatus.value?.currentNodeDurationMs ?? 0)
    + (effectiveDebugState.value === 'running' ? Math.max(0, clockNow.value - statusReceivedAt.value) : 0),
)
const primaryDebugLabel = computed(() =>
    executionId.value && !debugStatus.value?.isTerminal ? '继续' : '调试运行',
)
const canPrimaryDebug = computed(() =>
    debugActivity.value !== 'starting'
    && debugActivity.value !== 'running'
    && (
        !executionId.value
        || !!debugStatus.value?.isTerminal
        || (debugStatus.value?.canContinue ?? false)
    ),
)
const editorHost = ref<HTMLElement>()
const jsonText = ref('')
let editor: monaco.editor.IStandaloneCodeEditor | undefined
let validationTimer: ReturnType<typeof setTimeout> | undefined
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
        data: { type: node.type },
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
        const session =
            workflowSessions.find((item) => !item.isTerminal)
            ?? workflowSessions[0]
        if (!session) return

        executionId.value = session.executionId
        debugResults.value = []
        previewResult.value = undefined
        debugActivity.value = 'idle'
        breakpointNodes.value = await getBreakpoints(session.executionId)
        await connectDebugEvents(session.executionId)
        await applyDebugStatus(session)
        output.value.unshift(
            `已恢复调试会话：${session.debugState ?? 'paused'}，进度 ${session.executedSteps}/${session.totalSteps}`,
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
            severity:
                item.severity === 'warning'
                    ? monaco.MarkerSeverity.Warning
                    : monaco.MarkerSeverity.Error,
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
    if (!source.value) return
    saving.value = true
    try {
        source.value = await saveWorkflowSource(source.value.workflowId, source.value)
        dirty.value = false
        output.value.unshift(`已保存 revision ${source.value.revision}`)
    } catch (error) {
        output.value.unshift(`保存失败：${String(error)}`)
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
        const status = mode === 'into'
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
    completedNodeIds.value = Array.from(new Set(
        traceResults.value
            .filter((item) => item.nodeId && item.status !== 'faulted')
            .map((item) => String(item.nodeId)),
    ))
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
                `调试执行完成：${effectiveStatus.executedSteps}/${effectiveStatus.totalSteps} 个节点，耗时 ${formatDuration(effectiveStatus.durationMs ?? 0)}，输出 ${debugResults.value.length} 项`,
            )
        }
        bottomTab.value = 'results'
    } else if (effectiveStatus.debugState === 'faulted') {
        if (terminalMessageKey !== lastTerminalMessageKey) {
            output.value.unshift(
                `调试执行失败${effectiveStatus.faultNodeId ? `（节点 ${effectiveStatus.faultNodeId}）` : ''}：${effectiveStatus.errorMessage || '未知错误'}`,
            )
        }
        bottomTab.value = 'output'
    }
    if (terminalMessageKey) lastTerminalMessageKey = terminalMessageKey
    await nextTick()
    const currentNode = source.value?.graphData.nodes.find(
        (node) => node.id === status.currentNodeId,
    )
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
            executionId.value === status.executionId
            && debugStatus.value?.debugState === 'completed'
            && resultExecutionId !== status.executionId
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
                if (eventType === 'node-completed' && status.currentNodeId
                    && !completedNodeIds.value.includes(status.currentNodeId)) {
                    completedNodeIds.value.push(status.currentNodeId)
                }
                await applyDebugStatus(status, updatedAt)
            },
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

function tableColumns(
    rows: Array<Record<string, unknown>>,
    preferred: string[],
): string[] {
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
    return [
        'image',
        'point-cloud',
        'model-3d',
        'cad',
        'json-file',
        'text-file',
        'file',
    ].includes(presentation)
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

function selectGraphNode(event: { node: Node }) {
    selectedNodeId.value = event.node.id
    const problem = problems.value.find((x) => x.nodeId === event.node.id)
    if (problem) selectProblem(problem)
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
            triggerCharacters: ['(', ',', ':', ' '],
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
                                : monaco.languages.CompletionItemKind.Function,
                        insertText: item.insertText,
                        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                        range,
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
    }
    await loadProjects()
    await loadWorkflows()
    await loadWorkflow()
})

watch(workflowId, () => void loadWorkflow())
watch(projectId, () => {
    workflowId.value = ''
    source.value = null
    void router.replace({
        query: {
            ...route.query,
            projectId: projectId.value || undefined,
            workflowId: undefined,
        },
    })
    void loadWorkflows()
})
onBeforeUnmount(() => {
    clearTimeout(validationTimer)
    clearTimeout(resultRetryTimer)
    clearInterval(clockTimer)
    stopDebugPolling()
    window.removeEventListener('keydown', handleDebugShortcut)
    if (debugConnection) void debugConnection.stop()
    disposables.forEach((x) => x.dispose())
    editor?.dispose()
})

function handleDebugShortcut(event: KeyboardEvent) {
    if (event.key !== 'F5') return
    event.preventDefault()
    void primaryDebugAction()
}
</script>

<template>
    <div class="-m-6 flex h-[calc(100vh-3.5rem)] flex-col bg-background text-foreground">
        <header class="flex h-12 items-center gap-2 border-b px-3">
            <GitBranch class="size-4 text-primary" />
            <input
                v-model.trim="projectFilter"
                class="h-8 w-40 rounded border bg-background px-2 text-sm"
                placeholder="搜索项目"
                title="按项目名称或编号搜索"
            />
            <select v-model="projectId" class="h-8 min-w-64 rounded border bg-background px-2 text-sm">
                <option value="" disabled>选择项目</option>
                <option v-for="item in filteredProjects" :key="item.id" :value="item.id">
                    {{ item.projectCode }} · {{ item.name }}
                </option>
            </select>
            <select v-model="workflowId" class="h-8 min-w-56 rounded border bg-background px-2 text-sm">
                <option value="" disabled>选择工作流</option>
                <option v-for="item in workflows" :key="item.id" :value="item.id">{{ item.name }}</option>
            </select>
            <div class="mx-2 h-5 w-px bg-border" />
            <button class="ide-button" :disabled="!dirty || saving" @click="save">
                <Save class="size-4" />{{ saving ? '保存中' : '保存' }}
            </button>
            <button class="ide-button" @click="formatDocument"><WandSparkles class="size-4" />格式化</button>
            <button class="ide-button" :disabled="!canPrimaryDebug" title="调试运行/继续 (F5)"
                @click="primaryDebugAction">
                <Bug v-if="primaryDebugLabel === '调试运行'" class="size-4" />
                <Play v-else class="size-4" />{{ primaryDebugLabel }}
            </button>
            <button class="ide-button" :disabled="!canPauseDebug" @click="pause">暂停</button>
            <button class="ide-button" :disabled="debugActivity !== 'idle' || (!!executionId && !canStepDebug)"
                @click="stepOnce"><StepForward class="size-4" />单步</button>
            <button class="ide-button" :disabled="!selectedNodeId" :aria-pressed="selectedHasBreakpoint"
                :title="selectedHasBreakpoint ? '取消选中节点的断点' : '在选中节点执行前暂停'"
                @click="toggleSelectedBreakpoint">
                {{ selectedHasBreakpoint ? '取消断点' : '设置断点' }}
            </button>
            <button class="ide-button" :disabled="!canStepDebug" title="执行当前节点后停在下一节点；扁平工作流中等同于“越过”"
                @click="stepMode('into')">进入节点</button>
            <button class="ide-button" :disabled="!canStepDebug" title="执行当前节点后停在下一节点；当前工作流没有可进入的子调用"
                @click="stepMode('over')">越过节点</button>
            <button class="ide-button" :disabled="!canStepDebug" title="当前为顶层工作流，将运行到工作流结束"
                @click="stepMode('out')">跳出工作流</button>
            <button class="ide-button"
                :disabled="!selectedNodeId || debugActivity !== 'idle' || (!!executionId && !canContinueDebug)"
                @click="runToSelection">运行到节点</button>
            <button class="ide-button" :disabled="!canStopDebug" @click="stop"><CircleStop class="size-4" />停止</button>
            <span class="debug-state-badge" :class="debugStateInfo.className">
                <span class="debug-state-dot" />{{ debugStateInfo.label }}
                <template v-if="debugStatus">
                    · {{ debugStatus.executedSteps }}/{{ debugStatus.totalSteps }}
                    · {{ formatDuration(displayDurationMs) }}
                </template>
            </span>
            <span v-if="effectiveDebugState === 'running' && debugStatus?.currentNodeName"
                class="max-w-48 truncate text-xs text-muted-foreground">
                {{ debugStatus.currentNodeName }}
                <template v-if="displayCurrentNodeDurationMs >= 2000">
                    · 当前节点耗时 {{ formatDuration(displayCurrentNodeDurationMs) }}
                </template>
            </span>
            <span class="ml-auto text-xs text-muted-foreground">
                {{ dirty ? '● 未保存' : '已同步' }}
                <template v-if="source"> · rev {{ source.revision }}</template>
            </span>
        </header>

        <div class="flex h-9 items-center border-b px-3 text-xs">
            <button v-for="view in ['split', 'graph', 'source', 'json'] as const" :key="view"
                class="h-full border-b-2 px-3" :class="activeView === view ? 'border-primary text-primary' : 'border-transparent'"
                @click="activeView = view">
                {{ { split: '分屏', graph: '画布', source: '脚本', json: 'JSON' }[view] }}
            </button>
            <span v-if="selectedNodeId" class="ml-auto">节点：{{ selectedNodeId }}</span>
        </div>

        <main class="grid min-h-0 flex-1" :class="activeView === 'split' ? 'grid-cols-2' : 'grid-cols-1'">
            <section v-show="activeView === 'split' || activeView === 'graph'" class="relative min-h-0 border-r">
                <VueFlow :nodes="graphNodes" :edges="graphEdges" fit-view-on-init
                    @node-click="selectGraphNode" @node-double-click="({ node }) => toggleBreakpoint(node.id)">
                    <Background />
                    <Controls />
                </VueFlow>
                <div class="absolute left-3 top-3 rounded bg-background/90 px-2 py-1 text-xs shadow">
                    双击节点切换断点 · 红框为断点 · 绿色为当前节点
                </div>
                <div v-if="breakpointNodes.length"
                    class="absolute right-3 top-3 rounded bg-background/90 px-2 py-1 text-xs text-red-500 shadow">
                    ● {{ breakpointNodes.length }} 个断点
                </div>
            </section>
            <section v-show="activeView === 'split' || activeView === 'source'" class="min-h-0">
                <div ref="editorHost" class="h-full w-full" />
            </section>
            <section v-if="activeView === 'json'" class="flex min-h-0 flex-col p-3">
                <textarea v-model="jsonText" class="min-h-0 flex-1 resize-none rounded border bg-muted/30 p-3 font-mono text-xs" />
                <div class="mt-2 flex justify-end">
                    <button class="ide-button" @click="applyJson"><Code2 class="size-4" />生成候选脚本</button>
                </div>
            </section>
        </main>

        <section class="h-48 border-t">
            <div class="flex h-8 items-center border-b px-2 text-xs">
                <button v-for="tab in ['problems', 'variables', 'watch', 'trace', 'performance', 'results', 'output'] as const" :key="tab"
                    class="h-full px-3" :class="bottomTab === tab && 'text-primary'" @click="bottomTab = tab">
                    {{ { problems: `问题 (${problems.length})`, variables: '变量/栈', watch: '监视', trace: '跟踪', performance: '性能', results: `结果 (${debugStatus?.outputs?.length ?? 0})`, output: '输出日志' }[tab] }}
                </button>
                <button v-if="bottomTab === 'watch'" class="ml-auto ide-button h-6" @click="refreshWatch">刷新</button>
                <button v-if="bottomTab === 'trace' || bottomTab === 'performance'" class="ml-auto ide-button h-6" @click="refreshDebugDetails">刷新</button>
            </div>
            <div class="h-40 overflow-auto p-2 text-xs">
                <button v-if="bottomTab === 'problems'" v-for="problem in problems" :key="`${problem.code}-${problem.range.start.offset}`"
                    class="flex w-full gap-3 rounded px-2 py-1 text-left hover:bg-muted" @click="selectProblem(problem)">
                    <span :class="problem.severity === 'warning' ? 'text-amber-500' : 'text-red-500'">{{ problem.code }}</span>
                    <span>{{ problem.message }}</span><span class="ml-auto">Ln {{ problem.range.start.line }}</span>
                </button>
                <template v-else-if="bottomTab === 'variables'">
                    <div class="mb-1 font-medium">变量</div>
                    <table class="debug-table">
                        <thead><tr><th v-for="column in tableColumns(debugStatus?.variables ?? [], ['name', 'typeName', 'value', 'valueType'])" :key="column">{{ column }}</th></tr></thead>
                        <tbody>
                            <tr v-for="(row, index) in debugStatus?.variables ?? []" :key="index">
                                <td v-for="column in tableColumns(debugStatus?.variables ?? [], ['name', 'typeName', 'value', 'valueType'])" :key="column">{{ formatCell(row[column]) }}</td>
                            </tr>
                            <tr v-if="!(debugStatus?.variables?.length)"><td class="text-muted-foreground">暂无变量</td></tr>
                        </tbody>
                    </table>
                    <div class="mb-1 mt-3 font-medium">调用栈</div>
                    <table class="debug-table">
                        <thead><tr><th v-for="column in tableColumns(stackResults, ['depth', 'workflowName', 'nodeId', 'statementId'])" :key="column">{{ column }}</th></tr></thead>
                        <tbody>
                            <tr v-for="(row, index) in stackResults" :key="index">
                                <td v-for="column in tableColumns(stackResults, ['depth', 'workflowName', 'nodeId', 'statementId'])" :key="column">{{ formatCell(row[column]) }}</td>
                            </tr>
                            <tr v-if="!stackResults.length"><td class="text-muted-foreground">暂无调用栈</td></tr>
                        </tbody>
                    </table>
                </template>
                <table v-else-if="bottomTab === 'watch'" class="debug-table">
                    <thead><tr><th v-for="column in tableColumns(watchResults, ['expression', 'success', 'value', 'valueType', 'error'])" :key="column">{{ column }}</th></tr></thead>
                    <tbody>
                        <tr v-for="(row, index) in watchResults" :key="index">
                            <td v-for="column in tableColumns(watchResults, ['expression', 'success', 'value', 'valueType', 'error'])" :key="column">{{ formatCell(row[column]) }}</td>
                        </tr>
                        <tr v-if="!watchResults.length"><td class="text-muted-foreground">暂无监视结果</td></tr>
                    </tbody>
                </table>
                <table v-else-if="bottomTab === 'trace'" class="debug-table">
                    <thead><tr><th v-for="column in tableColumns(traceResults, ['sequence', 'timestamp', 'eventType', 'nodeId', 'message'])" :key="column">{{ column }}</th></tr></thead>
                    <tbody>
                        <tr v-for="(row, index) in traceResults" :key="index">
                            <td v-for="column in tableColumns(traceResults, ['sequence', 'timestamp', 'eventType', 'nodeId', 'message'])" :key="column">{{ formatCell(row[column]) }}</td>
                        </tr>
                        <tr v-if="!traceResults.length"><td class="text-muted-foreground">暂无跟踪事件</td></tr>
                    </tbody>
                </table>
                <table v-else-if="bottomTab === 'performance'" class="debug-table">
                    <thead><tr><th v-for="column in tableColumns(performanceResults, ['nodeId', 'nodeTitle', 'executionCount', 'totalElapsedMs', 'averageElapsedMs', 'maxElapsedMs'])" :key="column">{{ column }}</th></tr></thead>
                    <tbody>
                        <tr v-for="(row, index) in performanceResults" :key="index">
                            <td v-for="column in tableColumns(performanceResults, ['nodeId', 'nodeTitle', 'executionCount', 'totalElapsedMs', 'averageElapsedMs', 'maxElapsedMs'])" :key="column">{{ formatCell(row[column]) }}</td>
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
                    <div v-if="!debugResults.length"
                        class="rounded border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-amber-700">
                        工作流已执行完成，但结束节点未设置输出。
                    </div>
                    <table v-else class="debug-table">
                        <thead><tr><th>Display 名称</th><th>变量名称</th><th>值类型</th><th>值</th></tr></thead>
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
                                        <button
                                            class="ide-button"
                                            type="button"
                                            @click="openResultPreview(result)"
                                        >
                                            {{
                                                previewResult?.name === result.name
                                                    ? '收起'
                                                    : resultPresentation(result) === 'cad'
                                                      || resultPresentation(result) === 'file'
                                                      ? '下载'
                                                      : '查看'
                                            }}
                                        </button>
                                    </div>
                                    <div
                                        v-else-if="
                                            resultPresentation(result) === 'object'
                                            || resultPresentation(result) === 'array'
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
                            previewResult
                            && typeof previewResult.value === 'string'
                            && isFilePresentation(resultPresentation(previewResult))
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
.ide-button {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    border-radius: 0.3rem;
    padding: 0.35rem 0.6rem;
    font-size: 0.75rem;
}
.ide-button:hover:not(:disabled) { background: hsl(var(--accent)); }
.ide-button:disabled { opacity: 0.4; }
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
.debug-state-dot { width: 0.5rem; height: 0.5rem; border-radius: 9999px; background: currentColor; }
.debug-state-idle { color: #64748b; background: rgb(100 116 139 / 12%); }
.debug-state-running { color: #2563eb; background: rgb(37 99 235 / 12%); }
.debug-state-running .debug-state-dot { animation: workflow-node-pulse 0.7s ease-in-out infinite alternate; }
.debug-state-paused { color: #d97706; background: rgb(217 119 6 / 12%); }
.debug-state-completed { color: #16a34a; background: rgb(22 163 74 / 12%); }
.debug-state-faulted { color: #dc2626; background: rgb(220 38 38 / 12%); }
.debug-state-stopped { color: #475569; background: rgb(71 85 105 / 12%); }
:deep(.workflow-node-running) {
    box-shadow: 0 0 0 4px #3b82f6, 0 0 18px rgb(59 130 246 / 70%);
    animation: workflow-node-pulse 1.2s ease-in-out infinite alternate;
}
:deep(.workflow-node-paused) { box-shadow: 0 0 0 4px #eab308, 0 0 14px rgb(234 179 8 / 55%); }
:deep(.workflow-node-completed) { box-shadow: 0 0 0 3px #22c55e; }
:deep(.workflow-node-faulted) { box-shadow: 0 0 0 4px #ef4444, 0 0 14px rgb(239 68 68 / 60%); }
:deep(.workflow-node-breakpoint) { box-shadow: 0 0 0 3px #ef4444; }
.debug-table { width: 100%; border-collapse: collapse; font-family: ui-monospace, monospace; }
.debug-table th {
    position: sticky;
    top: 0;
    z-index: 1;
    background: hsl(var(--muted));
    text-align: left;
    font-weight: 600;
}
.debug-table th, .debug-table td {
    max-width: 32rem;
    border: 1px solid hsl(var(--border));
    padding: 0.25rem 0.4rem;
    overflow-wrap: anywhere;
    vertical-align: top;
}
@keyframes workflow-node-pulse {
    from { filter: brightness(1); }
    to { filter: brightness(1.18); }
}
</style>
