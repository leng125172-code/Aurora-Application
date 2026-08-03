import { httpClient } from './client'

const BASE = '/api/app/project-info'

export enum ProjectStatus {
    Active = 0,
    Suspended = 1,
    Completed = 2,
    Archived = 3,
}

export interface ProjectInfoDto {
    id: string
    projectCode: string
    name: string
    version: string
    description?: string | null
    status: ProjectStatus
    statusDisplay: string
    creatorUserName?: string | null
    workflowCount: number
    hasActiveDeployment: boolean
    activeDeploymentId?: string | null
    activeDeploymentRevision?: number | null
    creationTime: string
    lastModificationTime?: string | null
}

export interface GetProjectListInput {
    filter?: string
    status?: ProjectStatus
    skipCount: number
    maxResultCount: number
    sorting?: string
}

export interface PagedProjectResult {
    items: ProjectInfoDto[]
    totalCount: number
}

export interface CreateProjectInput {
    projectCode: string
    name: string
    version: string
    description?: string | null
}

export interface UpdateProjectInput {
    name: string
    version: string
    description?: string | null
}

export async function getProjectList(input: GetProjectListInput): Promise<PagedProjectResult> {
    return (await httpClient.get<PagedProjectResult>(BASE, { params: input })).data
}

export async function getProject(id: string): Promise<ProjectInfoDto> {
    return (await httpClient.get<ProjectInfoDto>(`${BASE}/${id}`)).data
}

export async function createProject(input: CreateProjectInput): Promise<ProjectInfoDto> {
    return (await httpClient.post<ProjectInfoDto>(BASE, input)).data
}

export async function updateProject(id: string, input: UpdateProjectInput): Promise<ProjectInfoDto> {
    return (await httpClient.put<ProjectInfoDto>(`${BASE}/${id}`, input)).data
}

export async function changeProjectStatus(id: string, status: ProjectStatus): Promise<ProjectInfoDto> {
    return (await httpClient.put<ProjectInfoDto>(`${BASE}/${id}/status`, undefined, { params: { status } })).data
}

export async function deleteProject(id: string): Promise<void> {
    await httpClient.delete(`${BASE}/${id}`)
}
