import { httpClient } from '@/api/client'

const BASE = '/api/app/workflow'

/** Stable graph-level contract intended for product pages and third-party integrations. */
export interface WorkflowGraph {
    nodes: Array<Record<string, unknown> & { id: string; type: string }>
    edges: Array<Record<string, unknown> & { id: string }>
}

export interface WorkflowPayload {
    projectId: string
    name: string
    graphData: WorkflowGraph
}

export interface Workflow extends WorkflowPayload {
    id: string
    outputVariables?: string
}

export interface WorkflowBrief {
    id: string
    projectId: string
    name: string
}

export interface WorkflowExecutionInput {
    projectId: string
    workflowId: string
    mode?: 0 | 2
    loopCount?: number
    inputVariableKeys?: string[]
    outputVariableNames?: string[]
}

export interface WorkflowExecutionStatus {
    executionId: string
    workflowId: string
    workflowName: string
    status: number
    error?: boolean
    errorCode?: string
    message?: string
    variables?: Array<Record<string, unknown>>
}

export interface ProjectTaskBatch {
    projectId: string
    taskType: number
    cycleIntervalSeconds?: number
    items: Array<{
        id: string
        projectId: string
        workflowId: string
        workflowName: string
        isEnabled: boolean
        orderNo: number
    }>
}

export interface ProjectDeployment {
    id: string
    projectId: string
    revision: number
    status: number
    isCurrentActive: boolean
    canActivate: boolean
    canReactivate: boolean
    canRollback: boolean
    snapshotHash: string
}

export interface ProjectRunQuery {
    projectId: string
    skipCount?: number
    maxResultCount?: number
}

export interface ProjectRun {
    id: string
    projectId: string
    name: string
    status: number
    deploymentId: string
    deploymentRevision: number
    [key: string]: unknown
}

export async function listWorkflows(projectId: string): Promise<WorkflowBrief[]> {
    return (await httpClient.get<WorkflowBrief[]>(BASE, { params: { projectId } })).data
}

export async function getWorkflow(id: string): Promise<Workflow> {
    return (await httpClient.get<Workflow>(`${BASE}/${id}`)).data
}

export async function createWorkflow(payload: WorkflowPayload): Promise<Workflow> {
    return (await httpClient.post<Workflow>(BASE, payload)).data
}

export async function updateWorkflow(id: string, payload: WorkflowPayload): Promise<Workflow> {
    return (await httpClient.put<Workflow>(`${BASE}/${id}`, payload)).data
}

export async function deleteWorkflow(id: string): Promise<void> {
    await httpClient.delete(`${BASE}/${id}`)
}

export async function validateWorkflow(id: string) {
    return (await httpClient.post(`${BASE}/${id}/validate`)).data
}

export async function simulateWorkflow(id: string) {
    return (await httpClient.post(`${BASE}/${id}/simulate`)).data
}

export async function getOutputPaths(id: string, variableName: string) {
    return (
        await httpClient.get(`${BASE}/${id}/output-paths`, { params: { variableName } })
    ).data
}

export async function executeWorkflow(
    input: WorkflowExecutionInput
): Promise<{ executionId: string; status: WorkflowExecutionStatus }> {
    return (await httpClient.post(`${BASE}/executions`, { mode: 0, loopCount: 1, ...input })).data
}

export async function getExecutionStatus(executionId: string): Promise<WorkflowExecutionStatus> {
    return (
        await httpClient.get<WorkflowExecutionStatus>(`${BASE}/executions/${executionId}`, {
            params: { includeVariables: true },
        })
    ).data
}

export async function getProjectRuns(input: ProjectRunQuery): Promise<ProjectRun[]> {
    return (await httpClient.get<ProjectRun[]>(`${BASE}/runs`, { params: input })).data
}

export async function getProjectRunStatus(runId: string): Promise<ProjectRun> {
    return (await httpClient.get<ProjectRun>(`${BASE}/runs/${runId}`)).data
}

export async function cancelProjectRun(runId: string): Promise<void> {
    await httpClient.post(`${BASE}/runs/${runId}/cancel`)
}

export async function getProjectTasks(projectId: string): Promise<ProjectTaskBatch> {
    return (await httpClient.get<ProjectTaskBatch>(`${BASE}/projects/${projectId}/tasks`)).data
}

export async function updateProjectTasks(input: ProjectTaskBatch): Promise<ProjectTaskBatch> {
    return (await httpClient.put<ProjectTaskBatch>(`${BASE}/projects/tasks`, input)).data
}

export async function getProjectDeployments(projectId: string): Promise<ProjectDeployment[]> {
    return (
        await httpClient.get<ProjectDeployment[]>(`${BASE}/projects/${projectId}/deployments`)
    ).data
}

export async function publishProjectDeployment(projectId: string): Promise<ProjectDeployment> {
    return (
        await httpClient.post<ProjectDeployment>(`${BASE}/projects/${projectId}/deployments`)
    ).data
}

export async function activateProjectDeployment(deploymentId: string): Promise<ProjectDeployment> {
    return (
        await httpClient.post<ProjectDeployment>(`${BASE}/deployments/${deploymentId}/activate`)
    ).data
}

export async function reactivateProjectDeployment(deploymentId: string): Promise<ProjectDeployment> {
    return (
        await httpClient.post<ProjectDeployment>(`${BASE}/deployments/${deploymentId}/reactivate`)
    ).data
}

export async function rollbackProjectDeployment(deploymentId: string): Promise<ProjectDeployment> {
    return (
        await httpClient.post<ProjectDeployment>(`${BASE}/deployments/${deploymentId}/rollback`)
    ).data
}

export async function deleteProjectDeployment(deploymentId: string): Promise<void> {
    await httpClient.delete(`${BASE}/deployments/${deploymentId}`)
}
