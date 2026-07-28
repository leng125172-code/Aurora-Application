import { httpClient } from '@/api/client'

const BASE = '/api/app/workflow'

export interface WorkflowBrief {
    id: string
    name: string
    projectId: string
}

export interface ProjectBrief {
    id: string
    projectCode: string
    name: string
    statusDisplay?: string
}

interface PagedResult<T> {
    items?: T[] | null
}

export async function listProjects(filter?: string): Promise<ProjectBrief[]> {
    const response = await httpClient.get<PagedResult<ProjectBrief>>('/api/app/project-info', {
        params: {
            filter: filter || undefined,
            skipCount: 0,
            maxResultCount: 1000,
            sorting: 'name',
        },
    })
    return response.data.items ?? []
}

export interface WorkflowSource {
    workflowId: string
    name: string
    sourceCode: string
    sourceHash: string
    contentHash: string
    semanticHash: string
    programHash: string
    operatorContractHash: string
    languageVersion: number
    revision: number
    concurrencyStamp: string
    graphData: WorkflowGraph
}

export interface WorkflowGraph {
    nodes: Array<{
        id: string
        type: string
        x?: number
        y?: number
        text?: { value?: string }
        properties?: Record<string, unknown>
    }>
    edges: Array<{
        id: string
        sourceNodeId?: string
        targetNodeId?: string
    }>
}

export interface IdePosition {
    offset: number
    line: number
    column: number
}

export interface IdeRange {
    start: IdePosition
    end: IdePosition
}

export interface IdeDiagnostic {
    code: string
    severity: string
    message: string
    range: IdeRange
    nodeId?: string
    statementId?: string
}

export interface IdeDocument {
    sourceCode: string
    documentVersion: number
    offset?: number
    workflowId?: string
    projectId?: string
}

export interface DebugStatus {
    executionId: string
    workflowId: string
    workflowName: string
    status: number
    isPaused: boolean
    currentNodeId?: string
    currentStatementId?: string
    executedSteps: number
    totalSteps: number
    variables: Array<Record<string, unknown>>
}

export async function listWorkflows(projectId: string): Promise<WorkflowBrief[]> {
    return (await httpClient.get<WorkflowBrief[]>(BASE, { params: { projectId } })).data
}

export async function getWorkflowSource(id: string): Promise<WorkflowSource> {
    return (await httpClient.get<WorkflowSource>(`${BASE}/${id}/source`)).data
}

export async function saveWorkflowSource(id: string, source: WorkflowSource): Promise<WorkflowSource> {
    return (
        await httpClient.put<WorkflowSource>(`${BASE}/${id}/source`, {
            sourceCode: source.sourceCode,
            expectedRevision: source.revision,
            baseRevision: source.revision,
            documentVersion: 0,
            editOrigin: 'source',
            concurrencyStamp: source.concurrencyStamp,
        })
    ).data
}

export async function diagnostics(input: IdeDocument) {
    return (
        await httpClient.post<{
            documentVersion: number
            contentHash: string
            semanticHash?: string
            diagnostics: IdeDiagnostic[]
        }>(`${BASE}/source/diagnostics`, input)
    ).data
}

export async function completions(input: IdeDocument) {
    return (
        await httpClient.post<{
            documentVersion: number
            items: Array<{
                label: string
                kind: string
                insertText: string
                detail?: string
                documentation?: string
            }>
        }>(`${BASE}/source/completions`, input)
    ).data
}

export async function hover(input: IdeDocument) {
    return (
        await httpClient.post<{ documentVersion: number; markdown?: string; range?: IdeRange }>(
            `${BASE}/source/hover`,
            input
        )
    ).data
}

export async function formatSource(input: IdeDocument) {
    return (
        await httpClient.post<{ documentVersion: number; sourceCode: string }>(
            `${BASE}/source/format`,
            input
        )
    ).data
}

export async function patchGraph(source: WorkflowSource, graph: WorkflowGraph, documentVersion: number) {
    return (
        await httpClient.post<{
            documentVersion: number
            hasConflict: boolean
            sourceCode: string
            graphData: WorkflowGraph
        }>(`${BASE}/source/patch-graph`, {
            sourceCode: source.sourceCode,
            documentVersion,
            workflowId: source.workflowId,
            baseRevision: source.revision,
            concurrencyStamp: source.concurrencyStamp,
            graphData: graph,
        })
    ).data
}

export async function debugSource(projectId: string, source: WorkflowSource) {
    return (
        await httpClient.post<{ executionId: string; status: DebugStatus }>(`${BASE}/source/debug`, {
            projectId,
            workflowId: source.workflowId,
            sourceCode: source.sourceCode,
            mode: 1,
            loopCount: 1,
        })
    ).data
}

export async function step(executionId: string) {
    return (
        await httpClient.post<{ status: DebugStatus }>(`${BASE}/executions/${executionId}/steps`, {
            steps: 1,
            includeVariables: true,
        })
    ).data
}

export async function continueDebug(executionId: string): Promise<DebugStatus> {
    return (await httpClient.post<DebugStatus>(`${BASE}/executions/${executionId}/continue`)).data
}

export async function pauseDebug(executionId: string): Promise<DebugStatus> {
    return (await httpClient.post<DebugStatus>(`${BASE}/executions/${executionId}/pause`)).data
}

export async function stepInto(executionId: string): Promise<DebugStatus> {
    return (await httpClient.post<DebugStatus>(`${BASE}/executions/${executionId}/step-into`)).data
}

export async function stepOver(executionId: string): Promise<DebugStatus> {
    return (await httpClient.post<DebugStatus>(`${BASE}/executions/${executionId}/step-over`)).data
}

export async function stepOut(executionId: string): Promise<DebugStatus> {
    return (await httpClient.post<DebugStatus>(`${BASE}/executions/${executionId}/step-out`)).data
}

export async function runTo(executionId: string, nodeId: string): Promise<DebugStatus> {
    return (
        await httpClient.post<DebugStatus>(`${BASE}/executions/${executionId}/run-to`, { nodeId })
    ).data
}

export async function getStack(executionId: string) {
    return (await httpClient.get<Array<Record<string, unknown>>>(`${BASE}/executions/${executionId}/stack`))
        .data
}

export async function getTrace(executionId: string) {
    return (await httpClient.get<Array<Record<string, unknown>>>(`${BASE}/executions/${executionId}/trace`))
        .data
}

export async function getPerformance(executionId: string) {
    return (
        await httpClient.get<Array<Record<string, unknown>>>(
            `${BASE}/executions/${executionId}/performance`
        )
    ).data
}

export async function stopDebug(executionId: string) {
    await httpClient.delete(`${BASE}/executions/${executionId}`)
}

export async function setBreakpoints(executionId: string, nodeIds: string[]) {
    return (
        await httpClient.put(`${BASE}/executions/${executionId}/breakpoints`, {
            breakpoints: nodeIds.map((nodeId) => ({ nodeId, enabled: true })),
        })
    ).data
}

export async function watch(executionId: string, expressions: string[]) {
    return (
        await httpClient.post<Array<Record<string, unknown>>>(`${BASE}/executions/${executionId}/watch`, {
            expressions,
        })
    ).data
}
