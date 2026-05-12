<script setup lang="ts">
/**
 * 租户管理页面：分页查询、创建、删除（仅宿主租户可见）
 */
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { Plus, RefreshCw, Search, Trash2 } from 'lucide-vue-next'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogClose } from '@/components/ui/dialog'
import { getTenantPageAsync, createTenantAsync, deleteTenantAsync, type TenantDto } from '@/api/management'

const { t } = useI18n()

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

async function handleDelete(tenant: TenantDto): Promise<void> {
    if (!tenant.id) return
    if (!confirm(t('management.confirmDelete', { name: tenant.name }))) return
    try {
        await deleteTenantAsync(tenant.id)
        toast.success(t('common.success'))
        await loadTenants()
    } catch {
        // 错误已在拦截器处理
    }
}
</script>

<template>
    <div class="space-y-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.tenants') }}</h1>
            <div class="flex gap-2">
                <Button variant="outline" size="icon" :disabled="loading" @click="loadTenants">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button @click="openCreate">
                    <Plus class="mr-1 size-4" />
                    {{ t('management.createTenant') }}
                </Button>
            </div>
        </div>

        <div class="flex gap-2">
            <Input
                v-model="filter"
                :placeholder="t('management.searchTenant')"
                class="max-w-xs"
                @keydown.enter="handleSearch"
            />
            <Button variant="outline" @click="handleSearch">
                <Search class="mr-1 size-4" />
                {{ t('common.search') }}
            </Button>
        </div>

        <div class="rounded-md border">
            <Table>
                <TableHeader>
                    <TableRow>
                        <TableHead>{{ t('management.tenantName') }}</TableHead>
                        <TableHead>ID</TableHead>
                        <TableHead class="text-right">{{ t('common.action') }}</TableHead>
                    </TableRow>
                </TableHeader>
                <TableBody>
                    <TableRow v-if="loading">
                        <TableCell colspan="3" class="py-8 text-center text-muted-foreground">
                            {{ t('common.loading') }}
                        </TableCell>
                    </TableRow>
                    <TableRow v-else-if="tenants.length === 0">
                        <TableCell colspan="3" class="py-8 text-center text-muted-foreground">
                            {{ t('management.noData') }}
                        </TableCell>
                    </TableRow>
                    <TableRow v-for="tenant in tenants" :key="tenant.id">
                        <TableCell class="font-medium">{{ tenant.name }}</TableCell>
                        <TableCell class="font-mono text-xs text-muted-foreground">{{ tenant.id }}</TableCell>
                        <TableCell class="text-right">
                            <Button
                                variant="ghost"
                                size="icon"
                                :title="t('common.delete')"
                                class="text-destructive hover:text-destructive"
                                @click="handleDelete(tenant)"
                            >
                                <Trash2 class="size-4" />
                            </Button>
                        </TableCell>
                    </TableRow>
                </TableBody>
            </Table>
        </div>

        <div class="flex items-center justify-between text-sm text-muted-foreground">
            <span>{{ t('management.totalRecords', { total }) }}</span>
            <div class="flex gap-2">
                <Button variant="outline" size="sm" :disabled="pageIndex <= 1" @click="prevPage">
                    {{ t('management.prevPage') }}
                </Button>
                <span class="flex items-center px-2">{{ pageIndex }} / {{ totalPages }}</span>
                <Button variant="outline" size="sm" :disabled="pageIndex >= totalPages" @click="nextPage">
                    {{ t('management.nextPage') }}
                </Button>
            </div>
        </div>
    </div>

    <!-- 创建租户弹窗 -->
    <Dialog v-model:open="showCreate">
        <DialogContent class="sm:max-w-md">
            <DialogHeader>
                <DialogTitle>{{ t('management.createTenant') }}</DialogTitle>
            </DialogHeader>
            <div class="space-y-3">
                <div class="space-y-1">
                    <Label>{{ t('management.tenantName') }} *</Label>
                    <Input v-model="createForm.name" />
                </div>
                <div class="space-y-1">
                    <Label>{{ t('management.adminEmail') }} *</Label>
                    <Input v-model="createForm.adminEmailAddress" type="email" />
                </div>
                <div class="space-y-1">
                    <Label>{{ t('management.adminPassword') }} *</Label>
                    <Input v-model="createForm.adminPassword" type="password" />
                </div>
            </div>
            <DialogFooter>
                <DialogClose as-child>
                    <Button variant="outline">{{ t('common.cancel') }}</Button>
                </DialogClose>
                <Button :disabled="creating" @click="submitCreate">
                    {{ creating ? t('common.loading') : t('common.confirm') }}
                </Button>
            </DialogFooter>
        </DialogContent>
    </Dialog>
</template>
