<script setup lang="ts">
/**
 * 租户管理页面：分页查询、创建、删除（仅宿主租户可见）
 */
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Plus, RefreshCw, Search, Trash2 } from '@lucide/vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Dialog from 'primevue/dialog'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { AppCard } from '@/components/primevue'
import { useAppToast } from '@/composables/useAppToast'
import { getTenantPageAsync, createTenantAsync, deleteTenantAsync, type TenantDto } from '@/api/management'

const { t } = useI18n()
const toast = useAppToast()
const confirm = useConfirm()

const pageIndex = ref(1)
const pageSize = ref(20)
const filter = ref('')
const total = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const tenants = ref<TenantDto[]>([])

async function loadTenants(): Promise<void> {
    loading.value = true
    try {
        const result = await getTenantPageAsync({
            pageIndex: pageIndex.value,
            pageSize: pageSize.value,
            filter: filter.value || null,
        })
        tenants.value = result.items ?? []
        total.value = result.totalCount ?? 0
    } finally {
        loading.value = false
    }
}

watch(
    total,
    () => {
        totalPages.value = Math.ceil(total.value / pageSize.value)
    },
    { immediate: true }
)

onMounted(loadTenants)

function handleSearch(): void {
    pageIndex.value = 1
    loadTenants()
}

function prevPage(): void {
    if (pageIndex.value > 1) {
        pageIndex.value--
        loadTenants()
    }
}

function nextPage(): void {
    if (pageIndex.value < totalPages.value) {
        pageIndex.value++
        loadTenants()
    }
}

// ——— 创建租户 ———
const showCreate = ref(false)
const createForm = ref({ name: '', adminEmailAddress: '', adminPassword: '' })
const creating = ref(false)

function openCreate(): void {
    createForm.value = { name: '', adminEmailAddress: '', adminPassword: '' }
    showCreate.value = true
}

async function submitCreate(): Promise<void> {
    if (!createForm.value.name || !createForm.value.adminEmailAddress || !createForm.value.adminPassword) {
        toast.warning(t('common.requiredFields'))
        return
    }
    creating.value = true
    try {
        await createTenantAsync(createForm.value)
        toast.success(t('common.success'))
        showCreate.value = false
        await loadTenants()
    } finally {
        creating.value = false
    }
}

function handleDelete(tenant: TenantDto): void {
    if (!tenant.id) return
    confirm.require({
        message: t('management.confirmDelete', { name: tenant.name }),
        header: t('common.delete'),
        icon: 'pi pi-exclamation-triangle',
        acceptProps: { severity: 'danger', size: 'small' },
        rejectProps: { severity: 'secondary', outlined: true, size: 'small' },
        accept: async () => {
            try {
                await deleteTenantAsync(tenant.id!)
                toast.success(t('common.success'))
                await loadTenants()
            } catch {
                // 错误已在拦截器处理
            }
        },
    })
}
</script>

<template>
    <div class="space-y-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.tenants') }}</h1>
            <div class="flex gap-2">
                <Button severity="secondary" outlined :disabled="loading" @click="loadTenants">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button @click="openCreate">
                    <Plus class="mr-1 size-4" />
                    {{ t('management.createTenant') }}
                </Button>
            </div>
        </div>

        <div class="flex gap-2">
            <InputText
                v-model="filter"
                :placeholder="t('management.searchTenant')"
                class="max-w-xs"
                @keydown.enter="handleSearch"
            />
            <Button severity="secondary" outlined @click="handleSearch">
                <Search class="mr-1 size-4" />
                {{ t('common.search') }}
            </Button>
        </div>

        <AppCard :beam="false">
            <DataTable :value="tenants" data-key="id" size="small" striped-rows scrollable :loading="loading">
                <template #empty>
                    <div class="py-8 text-center text-muted-foreground">{{ t('management.noData') }}</div>
                </template>
                <template #loading>
                    <div class="py-8 text-center text-muted-foreground">{{ t('common.loading') }}</div>
                </template>
                <Column :header="t('management.tenantName')">
                    <template #body="{ data }: { data: TenantDto }">
                        <div class="max-w-[200px] truncate font-medium" :title="data.name">{{ data.name }}</div>
                    </template>
                </Column>
                <Column header="ID">
                    <template #body="{ data }: { data: TenantDto }">
                        <div
                            class="max-w-[280px] truncate font-mono text-xs text-muted-foreground"
                            :title="data.id"
                        >
                            {{ data.id }}
                        </div>
                    </template>
                </Column>
                <Column :header="t('common.action')" header-class="text-right" body-class="text-right">
                    <template #body="{ data }: { data: TenantDto }">
                        <Button
                            text
                            severity="danger"
                            size="small"
                            :title="t('common.delete')"
                            @click="handleDelete(data)"
                        >
                            <Trash2 class="size-4" />
                        </Button>
                    </template>
                </Column>
            </DataTable>
        </AppCard>

        <div class="flex items-center justify-between text-sm text-muted-foreground">
            <span>{{ t('management.totalRecords', { total }) }}</span>
            <div class="flex gap-2">
                <Button severity="secondary" outlined size="small" :disabled="pageIndex <= 1" @click="prevPage">
                    {{ t('management.prevPage') }}
                </Button>
                <span class="flex items-center px-2">{{ pageIndex }} / {{ totalPages }}</span>
                <Button
                    severity="secondary"
                    outlined
                    size="small"
                    :disabled="pageIndex >= totalPages"
                    @click="nextPage"
                >
                    {{ t('management.nextPage') }}
                </Button>
            </div>
        </div>
    </div>

    <!-- 创建租户弹窗 -->
    <Dialog v-model:visible="showCreate" modal :header="t('management.createTenant')" :style="{ width: '460px' }">
        <div class="space-y-3">
            <div class="space-y-1">
                <label class="text-sm">{{ t('management.tenantName') }} *</label>
                <InputText v-model="createForm.name" class="w-full" />
            </div>
            <div class="space-y-1">
                <label class="text-sm">{{ t('management.adminEmail') }} *</label>
                <InputText v-model="createForm.adminEmailAddress" type="email" class="w-full" />
            </div>
            <div class="space-y-1">
                <label class="text-sm">{{ t('management.adminPassword') }} *</label>
                <InputText v-model="createForm.adminPassword" type="password" class="w-full" />
            </div>
        </div>
        <template #footer>
            <Button severity="secondary" outlined size="small" @click="showCreate = false">
                {{ t('common.cancel') }}
            </Button>
            <Button size="small" :disabled="creating" @click="submitCreate">
                {{ creating ? t('common.loading') : t('common.confirm') }}
            </Button>
        </template>
    </Dialog>

    <ConfirmDialog />
</template>
