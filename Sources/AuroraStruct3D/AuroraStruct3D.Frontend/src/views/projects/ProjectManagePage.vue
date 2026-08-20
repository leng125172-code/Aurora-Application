<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import Column from 'primevue/column'
import ConfirmDialog from 'primevue/confirmdialog'
import DataTable from 'primevue/datatable'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { FolderKanban, GitBranch, Pencil, Plus, RefreshCw, Search, Trash2 } from '@lucide/vue'
import BorderBeam from '@/components/ui/border-beam/BorderBeam.vue'
import { showErrorToastOnce } from '@/api/client'
import { useAppToast } from '@/composables/useAppToast'
import {
    ProjectStatus,
    changeProjectStatus,
    createProject,
    deleteProject,
    getProjectList,
    updateProject,
    type CreateProjectInput,
    type ProjectInfoDto,
    type UpdateProjectInput,
} from '@/api/projects'

const { t } = useI18n()
const router = useRouter()
const confirm = useConfirm()
const toast = useAppToast()
const workflowDebugEnabled = __WORKFLOW_DEBUG__

const loading = ref(false)
const items = ref<ProjectInfoDto[]>([])
const total = ref(0)
const filter = ref('')
const status = ref<ProjectStatus | null>(null)
const sorting = ref('CreationTime DESC')
const skipCount = ref(0)
const maxResultCount = ref(20)

const statusOptions = computed(() => [
    { label: t('projectManagement.statusActive'), value: ProjectStatus.Active },
    { label: t('projectManagement.statusSuspended'), value: ProjectStatus.Suspended },
    { label: t('projectManagement.statusCompleted'), value: ProjectStatus.Completed },
    { label: t('projectManagement.statusArchived'), value: ProjectStatus.Archived },
])
const filterStatusOptions = computed(() => [
    { label: t('projectManagement.allStatuses'), value: null },
    ...statusOptions.value,
])
const sortingOptions = computed(() => [
    { label: t('projectManagement.sortNewest'), value: 'CreationTime DESC' },
    { label: t('projectManagement.sortOldest'), value: 'CreationTime ASC' },
    { label: t('projectManagement.sortName'), value: 'Name ASC' },
    { label: t('projectManagement.sortCode'), value: 'ProjectCode ASC' },
])
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / maxResultCount.value)))
const currentPage = computed(() => Math.floor(skipCount.value / maxResultCount.value) + 1)

function statusLabel(value: ProjectStatus): string {
    return statusOptions.value.find((x) => x.value === value)?.label ?? String(value)
}

function statusSeverity(value: ProjectStatus): 'success' | 'warn' | 'info' | 'secondary' {
    if (value === ProjectStatus.Active) return 'success'
    if (value === ProjectStatus.Suspended) return 'warn'
    if (value === ProjectStatus.Completed) return 'info'
    return 'secondary'
}

async function loadList(): Promise<void> {
    loading.value = true
    try {
        const result = await getProjectList({
            filter: filter.value.trim() || undefined,
            status: status.value ?? undefined,
            skipCount: skipCount.value,
            maxResultCount: maxResultCount.value,
            sorting: sorting.value,
        })
        items.value = result.items
        total.value = result.totalCount
    } catch (error) {
        showErrorToastOnce(error)
    } finally {
        loading.value = false
    }
}

function search(): void {
    skipCount.value = 0
    void loadList()
}

function resetFilters(): void {
    filter.value = ''
    status.value = null
    sorting.value = 'CreationTime DESC'
    skipCount.value = 0
    void loadList()
}

function goToPage(page: number): void {
    skipCount.value = (page - 1) * maxResultCount.value
    void loadList()
}

const createOpen = ref(false)
const createSaving = ref(false)
const createForm = ref<CreateProjectInput>({ projectCode: '', name: '', version: '1.0.0', description: null })

function openCreate(): void {
    createForm.value = { projectCode: '', name: '', version: '1.0.0', description: null }
    createOpen.value = true
}

function validateForm(form: CreateProjectInput | UpdateProjectInput, projectCode?: string): boolean {
    if (projectCode !== undefined && !projectCode.trim()) {
        toast.warning(t('projectManagement.codeRequired'))
        return false
    }
    if (!form.name.trim()) {
        toast.warning(t('projectManagement.nameRequired'))
        return false
    }
    if (!form.version.trim()) {
        toast.warning(t('projectManagement.versionRequired'))
        return false
    }
    return true
}

async function submitCreate(): Promise<void> {
    if (!validateForm(createForm.value, createForm.value.projectCode)) return
    createSaving.value = true
    try {
        await createProject({
            projectCode: createForm.value.projectCode.trim(),
            name: createForm.value.name.trim(),
            version: createForm.value.version.trim(),
            description: createForm.value.description?.trim() || null,
        })
        createOpen.value = false
        toast.success(t('projectManagement.created'))
        skipCount.value = 0
        await loadList()
    } catch (error) {
        showErrorToastOnce(error)
    } finally {
        createSaving.value = false
    }
}

const editOpen = ref(false)
const editSaving = ref(false)
const editing = ref<ProjectInfoDto>()
const editForm = ref<UpdateProjectInput>({ name: '', version: '', description: null })

function openEdit(item: ProjectInfoDto): void {
    editing.value = item
    editForm.value = { name: item.name, version: item.version, description: item.description ?? null }
    editOpen.value = true
}

async function submitEdit(): Promise<void> {
    if (!editing.value || !validateForm(editForm.value)) return
    editSaving.value = true
    try {
        await updateProject(editing.value.id, {
            name: editForm.value.name.trim(),
            version: editForm.value.version.trim(),
            description: editForm.value.description?.trim() || null,
        })
        editOpen.value = false
        toast.success(t('projectManagement.updated'))
        await loadList()
    } catch (error) {
        showErrorToastOnce(error)
    } finally {
        editSaving.value = false
    }
}

async function setStatus(item: ProjectInfoDto, nextStatus: ProjectStatus): Promise<void> {
    if (item.status === nextStatus) return
    try {
        await changeProjectStatus(item.id, nextStatus)
        toast.success(t('projectManagement.statusChanged'))
        await loadList()
    } catch (error) {
        showErrorToastOnce(error)
    }
}

function confirmDelete(item: ProjectInfoDto): void {
    const deployment = item.hasActiveDeployment
        ? t('projectManagement.deleteDeployment', { revision: item.activeDeploymentRevision })
        : t('projectManagement.deleteNoDeployment')
    confirm.require({
        header: t('projectManagement.deleteTitle'),
        message: t('projectManagement.deleteMessage', {
            code: item.projectCode,
            name: item.name,
            workflows: item.workflowCount,
            deployment,
        }),
        icon: 'pi pi-exclamation-triangle',
        rejectLabel: t('projectManagement.cancel'),
        acceptLabel: t('projectManagement.delete'),
        acceptClass: 'p-button-danger',
        accept: async () => {
            try {
                await deleteProject(item.id)
                toast.success(t('projectManagement.deleted'))
                if (items.value.length === 1 && skipCount.value > 0) {
                    skipCount.value = Math.max(0, skipCount.value - maxResultCount.value)
                }
                await loadList()
            } catch (error) {
                showErrorToastOnce(error)
            }
        },
    })
}

function openWorkflow(item: ProjectInfoDto): void {
    void router.push({ name: 'WorkflowIde', query: { projectId: item.id } })
}

function displayDate(value?: string | null): string {
    return value ? new Date(value).toLocaleString() : '-'
}

onMounted(() => void loadList())
</script>

<template>
    <div class="relative flex h-full min-h-0 flex-col overflow-hidden p-1 sm:p-2">
        <BorderBeam :size="220" :duration="14" :border-width="1.5" />
        <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
            <div>
                <div class="flex items-center gap-2">
                    <FolderKanban class="size-6 text-primary" />
                    <h1 class="text-2xl font-semibold">{{ t('projectManagement.title') }}</h1>
                </div>
                <p class="mt-1 text-sm text-muted-foreground">{{ t('projectManagement.subtitle') }}</p>
            </div>
            <div class="flex w-full gap-2 sm:w-auto">
                <Button size="small" severity="secondary" outlined :disabled="loading" @click="loadList">
                    <RefreshCw class="mr-1.5 size-3.5" :class="{ 'animate-spin': loading }" />
                    {{ t('projectManagement.refresh') }}
                </Button>
                <Button size="small" @click="openCreate">
                    <Plus class="mr-1.5 size-3.5" />
                    {{ t('projectManagement.create') }}
                </Button>
            </div>
        </div>

        <section class="mb-4 flex flex-wrap gap-2 rounded-lg border bg-card/60 p-3">
            <InputText
                v-model="filter"
                class="min-w-0 flex-[1_1_100%] sm:min-w-64"
                :placeholder="t('projectManagement.searchPlaceholder')"
                @keyup.enter="search"
            />
            <Select
                v-model="status"
                :options="filterStatusOptions"
                option-label="label"
                option-value="value"
                class="min-w-0 flex-1 sm:w-44 sm:flex-none"
            />
            <Select
                v-model="sorting"
                :options="sortingOptions"
                option-label="label"
                option-value="value"
                class="min-w-0 flex-1 sm:w-44 sm:flex-none"
                @change="search"
            />
            <Button size="small" @click="search">
                <Search class="mr-1.5 size-3.5" />
                {{ t('projectManagement.search') }}
            </Button>
            <Button size="small" severity="secondary" text @click="resetFilters">
                {{ t('projectManagement.reset') }}
            </Button>
        </section>

        <div class="min-h-0 flex-1 overflow-auto rounded-lg border bg-card/60">
            <DataTable :value="items" :loading="loading" data-key="id" striped-rows>
                <Column :header="t('projectManagement.project')" style="min-width: 16rem">
                    <template #body="{ data }">
                        <div class="font-medium">{{ data.name }}</div>
                        <div class="font-mono text-xs text-muted-foreground">
                            {{ data.projectCode }} · v{{ data.version }}
                        </div>
                        <div
                            v-if="data.description"
                            class="mt-1 max-w-md truncate text-xs text-muted-foreground"
                            :title="data.description"
                        >
                            {{ data.description }}
                        </div>
                    </template>
                </Column>
                <Column :header="t('projectManagement.status')" style="width: 9rem">
                    <template #body="{ data }">
                        <Tag :value="statusLabel(data.status)" :severity="statusSeverity(data.status)" />
                    </template>
                </Column>
                <Column :header="t('projectManagement.workflows')" style="width: 8rem">
                    <template #body="{ data }">
                        <span class="font-medium">{{ data.workflowCount }}</span>
                    </template>
                </Column>
                <Column :header="t('projectManagement.deployment')" style="min-width: 10rem">
                    <template #body="{ data }">
                        <Tag
                            v-if="data.hasActiveDeployment"
                            severity="success"
                            :value="`rev ${data.activeDeploymentRevision}`"
                        />
                        <span v-else class="text-xs text-muted-foreground">
                            {{ t('projectManagement.noDeployment') }}
                        </span>
                    </template>
                </Column>
                <Column :header="t('projectManagement.audit')" style="min-width: 13rem">
                    <template #body="{ data }">
                        <div class="text-sm">{{ data.creatorUserName || '-' }}</div>
                        <div class="text-xs text-muted-foreground">
                            {{ displayDate(data.lastModificationTime || data.creationTime) }}
                        </div>
                    </template>
                </Column>
                <Column :header="t('projectManagement.actions')" style="min-width: 24rem">
                    <template #body="{ data }">
                        <div class="flex flex-wrap items-center gap-1">
                            <Button
                                v-if="workflowDebugEnabled"
                                size="small"
                                severity="secondary"
                                outlined
                                @click="openWorkflow(data)"
                            >
                                <GitBranch class="mr-1 size-3.5" />
                                {{ t('projectManagement.workflow') }}
                            </Button>
                            <Button size="small" severity="secondary" text @click="openEdit(data)">
                                <Pencil class="mr-1 size-3.5" />
                                {{ t('projectManagement.edit') }}
                            </Button>
                            <Select
                                size="small"
                                :model-value="data.status"
                                :options="statusOptions"
                                option-label="label"
                                option-value="value"
                                class="w-32 text-xs"
                                @update:model-value="setStatus(data, $event)"
                            />
                            <Button size="small" severity="danger" text @click="confirmDelete(data)">
                                <Trash2 class="size-3.5" />
                            </Button>
                        </div>
                    </template>
                </Column>
                <template #empty>
                    <div class="p-8 text-center text-muted-foreground">{{ t('projectManagement.empty') }}</div>
                </template>
            </DataTable>
        </div>

        <div class="mt-3 flex items-center justify-between text-sm text-muted-foreground">
            <span>{{ t('projectManagement.total', { total }) }}</span>
            <div class="flex items-center gap-2">
                <Button
                    size="small"
                    severity="secondary"
                    outlined
                    :disabled="currentPage <= 1"
                    @click="goToPage(currentPage - 1)"
                >
                    {{ t('projectManagement.previous') }}
                </Button>
                <span>{{ currentPage }} / {{ totalPages }}</span>
                <Button
                    size="small"
                    severity="secondary"
                    outlined
                    :disabled="currentPage >= totalPages"
                    @click="goToPage(currentPage + 1)"
                >
                    {{ t('projectManagement.next') }}
                </Button>
            </div>
        </div>

        <Dialog
            v-model:visible="createOpen"
            modal
            :header="t('projectManagement.createTitle')"
            class="w-[34rem] max-w-[95vw]"
        >
            <div class="grid gap-4">
                <label class="grid gap-1 text-sm">
                    {{ t('projectManagement.code') }} *
                    <InputText v-model="createForm.projectCode" maxlength="64" />
                </label>
                <label class="grid gap-1 text-sm">
                    {{ t('projectManagement.name') }} *
                    <InputText v-model="createForm.name" maxlength="256" />
                </label>
                <label class="grid gap-1 text-sm">
                    {{ t('projectManagement.version') }} *
                    <InputText v-model="createForm.version" maxlength="32" />
                </label>
                <label class="grid gap-1 text-sm">
                    {{ t('projectManagement.description') }}
                    <Textarea v-model="createForm.description" rows="5" maxlength="2000" />
                </label>
            </div>
            <template #footer>
                <Button severity="secondary" text @click="createOpen = false">
                    {{ t('projectManagement.cancel') }}
                </Button>
                <Button :loading="createSaving" @click="submitCreate">{{ t('projectManagement.save') }}</Button>
            </template>
        </Dialog>

        <Dialog
            v-model:visible="editOpen"
            modal
            :header="t('projectManagement.editTitle')"
            class="w-[34rem] max-w-[95vw]"
        >
            <div class="grid gap-4">
                <label class="grid gap-1 text-sm">
                    {{ t('projectManagement.code') }}
                    <InputText :model-value="editing?.projectCode" disabled />
                </label>
                <label class="grid gap-1 text-sm">
                    {{ t('projectManagement.name') }} *
                    <InputText v-model="editForm.name" maxlength="256" />
                </label>
                <label class="grid gap-1 text-sm">
                    {{ t('projectManagement.version') }} *
                    <InputText v-model="editForm.version" maxlength="32" />
                </label>
                <label class="grid gap-1 text-sm">
                    {{ t('projectManagement.description') }}
                    <Textarea v-model="editForm.description" rows="5" maxlength="2000" />
                </label>
            </div>
            <template #footer>
                <Button severity="secondary" text @click="editOpen = false">{{ t('projectManagement.cancel') }}</Button>
                <Button :loading="editSaving" @click="submitEdit">{{ t('projectManagement.save') }}</Button>
            </template>
        </Dialog>
        <ConfirmDialog />
    </div>
</template>
