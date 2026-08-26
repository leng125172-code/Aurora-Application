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
    resultWorkflowId?: string
    resultVariableName?: string
    onErrorAction: 0 | 1
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
    snapshotSchemaVersion: number
    taskType: number
    cycleIntervalSeconds?: number
    resultWorkflowId?: string
    resultVariableName?: string
    onErrorAction: 0 | 1
}

export interface ProjectApplicationStatus {
    projectId: string
    activeDeployment?: ProjectDeployment | null
    latestDeployment?: ProjectDeployment | null
    hasUnappliedChanges: boolean
    canApply: boolean
    validationMessages: string[]
}

export interface ApplyProjectResult {
    projectId: string
    deployment: ProjectDeployment
    createdNewRevision: boolean
    activationChanged: boolean
    unchanged: boolean
    appliedAt: string
}

export interface WorkflowPlcTrigger {
    id: string
    projectId: string
    plcDeviceId: string
    plcTagId: string
    expectedValueJson: string
    tolerance: number
    isEnabled: boolean
    lastTriggeredAt?: string
    lastSkipReason?: string
}

export type SaveWorkflowPlcTrigger = Omit<WorkflowPlcTrigger, 'id' | 'lastTriggeredAt' | 'lastSkipReason'>

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
    inspectionDecision: number
    inspectionErrorCode: number
    inspectionErrorMessage?: string
    errorCode?: string
    [key: string]: unknown
}

export interface WorkflowPlcHandshakeConfig {
    id: string
    taskConfigId: string
    projectId: string
    plcDeviceId: string
    captureRequestAddress: string
    requestIdAddress: string
    resultAckAddress: string
    resultAckIdAddress: string
    heartbeatAddress: string
    deviceStatusAddress: string
    taskStatusAddress: string
    canCaptureAddress: string
    captureAckAddress: string
    ackRequestIdAddress: string
    resultValidAddress: string
    resultRequestIdAddress: string
    resultCodeAddress: string
    errorCodeAddress: string
    isEnabled: boolean
}

export type SaveWorkflowPlcHandshakeConfig = Omit<WorkflowPlcHandshakeConfig, 'id' | 'taskConfigId'>

export interface WorkflowPlcHandshakeStatus {
    projectId: string
    isConfigured: boolean
    isEnabled: boolean
    phase: 0 | 1 | 2 | 3 | 4
    currentRequestId: number
    lastCompletedRequestId: number
    currentRunId?: string
    resultCode: 0 | 1 | 2 | 3 | 4
    errorCode: number
    lastRequestAt?: string
    lastResultAt?: string
    lastError?: string
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

export interface ProjectTaskRegistration {
    id: string
    projectId: string
    name: string
    isEnabled: boolean
    creationTime: string
    plcHandshake?: WorkflowPlcHandshakeConfig
}

export async function getProjectTaskRegistrations(projectId?: string): Promise<ProjectTaskRegistration[]> {
    return (await httpClient.get<ProjectTaskRegistration[]>(`${BASE}/project-tasks`, {
        params: projectId ? { projectId } : undefined,
    })).data
}

export async function createProjectTask(
    input: { projectId: string; name: string; isEnabled: boolean; plcHandshake?: SaveWorkflowPlcHandshakeConfig }
): Promise<ProjectTaskRegistration> {
    return (await httpClient.post<ProjectTaskRegistration>(`${BASE}/project-tasks`, input)).data
}

export async function updateProjectTask(
    taskId: string,
    input: { name: string; isEnabled: boolean; plcHandshake?: SaveWorkflowPlcHandshakeConfig }
): Promise<ProjectTaskRegistration> {
    return (await httpClient.put<ProjectTaskRegistration>(
        `${BASE}/project-tasks/${taskId}`, input
    )).data
}

export async function setProjectTaskEnabled(
    taskId: string, isEnabled: boolean
): Promise<ProjectTaskRegistration> {
    return (await httpClient.put<ProjectTaskRegistration>(
        `${BASE}/project-tasks/${taskId}/enabled`, { isEnabled }
    )).data
}

export async function getProjectDeployments(projectId: string): Promise<ProjectDeployment | null> {
    return (
        await httpClient.get<ProjectDeployment | null>(`${BASE}/projects/${projectId}/deployments`)
    ).data
}

export async function getProjectDeploymentHistory(
    projectId: string,
    skipCount = 0,
    maxResultCount = 20
): Promise<{ totalCount: number; items: ProjectDeployment[] }> {
    return (await httpClient.get(`${BASE}/projects/${projectId}/deployments/history`, {
        params: { skipCount, maxResultCount },
    })).data
}

export async function getProjectApplicationStatus(projectId: string): Promise<ProjectApplicationStatus> {
    return (await httpClient.get<ProjectApplicationStatus>(
        `${BASE}/projects/${projectId}/application-status`
    )).data
}

export async function applyProjectToDevice(
    projectId: string,
    taskConfig: ProjectTaskBatch,
    expectedActiveDeploymentId?: string | null
): Promise<ApplyProjectResult> {
    return (await httpClient.post<ApplyProjectResult>(`${BASE}/projects/${projectId}/apply`, {
        expectedActiveDeploymentId: expectedActiveDeploymentId ?? null,
        taskConfig,
    })).data
}

export async function getWorkflowPlcHandshake(projectId: string): Promise<WorkflowPlcHandshakeConfig> {
    return (await httpClient.get<WorkflowPlcHandshakeConfig>(`${BASE}/projects/${projectId}/plc-handshake`)).data
}

export async function saveWorkflowPlcHandshake(projectId: string, input: SaveWorkflowPlcHandshakeConfig): Promise<WorkflowPlcHandshakeConfig> {
    return (await httpClient.put<WorkflowPlcHandshakeConfig>(`${BASE}/projects/${projectId}/plc-handshake`, input)).data
}

export async function getWorkflowPlcHandshakeStatus(projectId: string): Promise<WorkflowPlcHandshakeStatus> {
    return (await httpClient.get<WorkflowPlcHandshakeStatus>(`${BASE}/projects/${projectId}/plc-handshake/status`)).data
}

export async function resetWorkflowPlcHandshake(projectId: string): Promise<WorkflowPlcHandshakeStatus> {
    return (await httpClient.post<WorkflowPlcHandshakeStatus>(`${BASE}/projects/${projectId}/plc-handshake/reset`)).data
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

export async function getWorkflowPlcTriggers(projectId: string): Promise<WorkflowPlcTrigger[]> {
    return (await httpClient.get<WorkflowPlcTrigger[]>(`${BASE}/projects/${projectId}/plc-triggers`)).data
}

export async function saveWorkflowPlcTrigger(id: string | undefined, input: SaveWorkflowPlcTrigger): Promise<WorkflowPlcTrigger> {
    return (await httpClient.post<WorkflowPlcTrigger>(`${BASE}/plc-triggers`, input, { params: id ? { id } : undefined })).data
}

export async function deleteWorkflowPlcTrigger(id: string): Promise<void> {
    await httpClient.delete(`${BASE}/plc-triggers/${id}`)
}
