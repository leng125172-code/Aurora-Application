<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import * as monaco from 'monaco-editor'
import { VueFlow, useVueFlow, type Edge, type Node } from '@vue-flow/core'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import {
    completions,
    continueDebug,
    debugSource,
    diagnostics,
    formatSource,
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
    step,
    stepInto,
    stepOut,
    stepOver,
    stopDebug,
    watch as evaluateWatch,
    type DebugStatus,
    type IdeDiagnostic,
    type ProjectBrief,
    type WorkflowBrief,
    type WorkflowGraph,
    type WorkflowSource,
} from '@/api/workflow-ide'
import { Bug, CircleStop, Code2, GitBranch, Play, Save, StepForward, WandSparkles } from '@lucide/vue'

const route = useRoute()
const router = useRouter()
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
const executionId = ref('')
const breakpointNodes = ref<string[]>([])
const watchExpressions = ref<string[]>(['retstatus.status'])
const watchResults = ref<Array<Record<string, unknown>>>([])
const traceResults = ref<Array<Record<string, unknown>>>([])
const stackResults = ref<Array<Record<string, unknown>>>([])
const performanceResults = ref<Array<Record<string, unknown>>>([])
const bottomTab = ref<'problems' | 'variables' | 'watch' | 'trace' | 'performance' | 'output'>(
    'problems'
)
const output = ref<string[]>([])
const editorHost = ref<HTMLElement>()
const jsonText = ref('')
let editor: monaco.editor.IStandaloneCodeEditor | undefined
let validationTimer: ReturnType<typeof setTimeout> | undefined
const disposables: monaco.IDisposable[] = []

const { fitView } = useVueFlow()
const graphNodes = computed<Node[]>(() =>
    (source.value?.graphData.nodes ?? []).map((node, index) => ({
        id: node.id,
        position: { x: node.x ?? (index % 4) * 220, y: node.y ?? Math.floor(index / 4) * 140 },
        label: node.text?.value || node.type,
        data: { type: node.type },
        class:
            debugStatus.value?.currentNodeId === node.id
                ? 'workflow-node-running'
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
    source.value = await getWorkflowSource(workflowId.value)
    jsonText.value = JSON.stringify(source.value.graphData, null, 2)
    dirty.value = false
    documentVersion.value++
    editor?.setValue(source.value.sourceCode)
    await nextTick()
    void fitView({ padding: 0.2 })
    await validateNow()
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
    const result = await debugSource(projectId.value, source.value)
    executionId.value = result.executionId
    debugStatus.value = result.status
    if (breakpointNodes.value.length) await setBreakpoints(executionId.value, breakpointNodes.value)
    bottomTab.value = 'variables'
}

async function stepOnce() {
    if (!executionId.value) return
    debugStatus.value = (await step(executionId.value)).status
}

async function resume() {
    if (!executionId.value) return
    debugStatus.value = await continueDebug(executionId.value)
}

async function pause() {
    if (!executionId.value) return
    debugStatus.value = await pauseDebug(executionId.value)
}

async function stepMode(mode: 'into' | 'over' | 'out') {
    if (!executionId.value) return
    debugStatus.value =
        mode === 'into'
            ? await stepInto(executionId.value)
            : mode === 'over'
              ? await stepOver(executionId.value)
              : await stepOut(executionId.value)
}

async function runToSelection() {
    if (!executionId.value || !selectedNodeId.value) return
    debugStatus.value = await runTo(executionId.value, selectedNodeId.value)
}

async function stop() {
    if (!executionId.value) return
    await stopDebug(executionId.value)
    executionId.value = ''
    debugStatus.value = undefined
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

function registerLanguage() {
    if (!monaco.languages.getLanguages().some((x) => x.id === 'aurora-workflow')) {
        monaco.languages.register({ id: 'aurora-workflow' })
        monaco.languages.setMonarchTokensProvider('aurora-workflow', {
            tokenizer: {
                root: [
                    [/\b(Workflow|GraphBegin|GraphEnd|Node|Input|Output|Param|Edge)\b/, 'keyword'],
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
            triggerCharacters: ['.', '"'],
            provideCompletionItems: async (model, position) => {
                const version = documentVersion.value
                const result = await completions(ideInput(model.getOffsetAt(position)))
                if (result.documentVersion !== version) return { suggestions: [] }
                return {
                    suggestions: result.items.map((item) => ({
                        label: item.label,
                        detail: item.detail,
                        documentation: item.documentation,
                        kind:
                            item.kind === 'operator'
                                ? monaco.languages.CompletionItemKind.Class
                                : monaco.languages.CompletionItemKind.Function,
                        insertText: item.insertText,
                        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                        range: undefined as never,
                    })),
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
    disposables.forEach((x) => x.dispose())
    editor?.dispose()
})
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
            <button class="ide-button" @click="startDebug"><Bug class="size-4" />调试</button>
            <button class="ide-button" :disabled="!executionId" @click="resume"><Play class="size-4" />继续</button>
            <button class="ide-button" :disabled="!executionId" @click="pause">暂停</button>
            <button class="ide-button" :disabled="!executionId" @click="stepOnce"><StepForward class="size-4" />单步</button>
            <button class="ide-button" :disabled="!executionId" @click="stepMode('into')">进入</button>
            <button class="ide-button" :disabled="!executionId" @click="stepMode('over')">越过</button>
            <button class="ide-button" :disabled="!executionId" @click="stepMode('out')">跳出</button>
            <button class="ide-button" :disabled="!executionId || !selectedNodeId" @click="runToSelection">运行到节点</button>
            <button class="ide-button" :disabled="!executionId" @click="stop"><CircleStop class="size-4" />停止</button>
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
                    双击节点切换断点
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
                <button v-for="tab in ['problems', 'variables', 'watch', 'trace', 'performance', 'output'] as const" :key="tab"
                    class="h-full px-3" :class="bottomTab === tab && 'text-primary'" @click="bottomTab = tab">
                    {{ { problems: `问题 (${problems.length})`, variables: '变量/栈', watch: '监视', trace: '跟踪', performance: '性能', output: '输出' }[tab] }}
                </button>
                <button v-if="bottomTab === 'watch'" class="ml-auto ide-button h-6" @click="refreshWatch">刷新</button>
                <button v-if="bottomTab === 'trace' || bottomTab === 'performance'" class="ml-auto ide-button h-6" @click="refreshDebugDetails">刷新</button>
            </div>
            <div class="h-40 overflow-auto p-2 font-mono text-xs">
                <button v-if="bottomTab === 'problems'" v-for="problem in problems" :key="`${problem.code}-${problem.range.start.offset}`"
                    class="flex w-full gap-3 rounded px-2 py-1 text-left hover:bg-muted" @click="selectProblem(problem)">
                    <span :class="problem.severity === 'warning' ? 'text-amber-500' : 'text-red-500'">{{ problem.code }}</span>
                    <span>{{ problem.message }}</span><span class="ml-auto">Ln {{ problem.range.start.line }}</span>
                </button>
                <pre v-else-if="bottomTab === 'variables'">{{ JSON.stringify({ variables: debugStatus?.variables ?? [], stack: stackResults }, null, 2) }}</pre>
                <pre v-else-if="bottomTab === 'watch'">{{ JSON.stringify(watchResults, null, 2) }}</pre>
                <pre v-else-if="bottomTab === 'trace'">{{ JSON.stringify(traceResults, null, 2) }}</pre>
                <pre v-else-if="bottomTab === 'performance'">{{ JSON.stringify(performanceResults, null, 2) }}</pre>
                <div v-else v-for="(line, index) in output" :key="index">{{ line }}</div>
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
:deep(.workflow-node-running) { box-shadow: 0 0 0 3px #22c55e; }
:deep(.workflow-node-breakpoint) { box-shadow: 0 0 0 3px #ef4444; }
</style>
