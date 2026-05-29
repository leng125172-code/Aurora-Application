<script setup lang="ts">
/**
 * 角色管理页面：分页查询、创建、删除
 */
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { Plus, RefreshCw, Search, Trash2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogClose } from '@/components/ui/dialog'
import { getRolePageAsync, createRoleAsync, deleteRoleAsync, type RoleDto } from '@/api/management'

const { t } = useI18n()

const pageIndex = ref(1)
const pageSize = ref(20)
const filter = ref('')
const total = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const roles = ref<RoleDto[]>([])

async function loadRoles(): Promise<void> {
    loading.value = true
    try {
        const result = await getRolePageAsync({
            pageIndex: pageIndex.value,
            pageSize: pageSize.value,
            filter: filter.value || null,
        })
        roles.value = result.items ?? []
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

onMounted(loadRoles)

function handleSearch(): void {
    pageIndex.value = 1
    loadRoles()
}

function prevPage(): void {
    if (pageIndex.value > 1) {
        pageIndex.value--
        loadRoles()
    }
}

function nextPage(): void {
    if (pageIndex.value < totalPages.value) {
        pageIndex.value++
        loadRoles()
    }
}

// ——— 创建角色 ———
const showCreate = ref(false)
const createForm = ref({ name: '', isDefault: false, isPublic: true })
const creating = ref(false)

function openCreate(): void {
    createForm.value = { name: '', isDefault: false, isPublic: true }
    showCreate.value = true
}

async function submitCreate(): Promise<void> {
    if (!createForm.value.name) {
        toast.warning(t('common.requiredFields'))
        return
    }
    creating.value = true
    try {
        await createRoleAsync(createForm.value)
        toast.success(t('common.success'))
        showCreate.value = false
        await loadRoles()
    } finally {
        creating.value = false
    }
}

async function handleDelete(role: RoleDto): Promise<void> {
    if (!role.id || role.isStatic) {
        toast.warning(t('management.cannotDeleteStaticRole'))
        return
    }
    if (!confirm(t('management.confirmDelete', { name: role.name }))) return
    try {
        await deleteRoleAsync(role.id)
        toast.success(t('common.success'))
        await loadRoles()
    } catch {
        // 错误已在拦截器处理
    }
}
</script>

<template>
    <div class="space-y-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.roles') }}</h1>
            <div class="flex gap-2">
                <Button variant="outline" size="icon" :disabled="loading" @click="loadRoles">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button @click="openCreate">
                    <Plus class="mr-1 size-4" />
                    {{ t('management.createRole') }}
                </Button>
            </div>
        </div>

        <div class="flex gap-2">
            <Input
                v-model="filter"
                :placeholder="t('management.searchRole')"
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
                        <TableHead>{{ t('management.roleName') }}</TableHead>
                        <TableHead>{{ t('management.isDefault') }}</TableHead>
                        <TableHead>{{ t('management.isPublic') }}</TableHead>
                        <TableHead>{{ t('management.isStatic') }}</TableHead>
                        <TableHead class="text-right whitespace-nowrap">{{ t('common.action') }}</TableHead>
                    </TableRow>
                </TableHeader>
                <TableBody>
                    <TableRow v-if="loading">
                        <TableCell colspan="5" class="py-8 text-center text-muted-foreground">
                            {{ t('common.loading') }}
                        </TableCell>
                    </TableRow>
                    <TableRow v-else-if="roles.length === 0">
                        <TableCell colspan="5" class="py-8 text-center text-muted-foreground">
                            {{ t('management.noData') }}
                        </TableCell>
                    </TableRow>
                    <TableRow v-for="role in roles" :key="role.id">
                        <TableCell class="font-medium">
                            <div class="max-w-[200px] truncate" :title="role.name">{{ role.name }}</div>
                        </TableCell>
                        <TableCell>
                            <Badge v-if="role.isDefault" variant="secondary">{{ t('common.yes') }}</Badge>
                            <span v-else class="text-muted-foreground">{{ t('common.no') }}</span>
                        </TableCell>
                        <TableCell>
                            <Badge v-if="role.isPublic" variant="outline">{{ t('common.yes') }}</Badge>
                            <span v-else class="text-muted-foreground">{{ t('common.no') }}</span>
                        </TableCell>
                        <TableCell>
                            <Badge v-if="role.isStatic" variant="destructive">{{ t('common.yes') }}</Badge>
                            <span v-else class="text-muted-foreground">{{ t('common.no') }}</span>
                        </TableCell>
                        <TableCell class="text-right">
                            <Button
                                variant="ghost"
                                size="icon"
                                :disabled="role.isStatic"
                                :title="t('common.delete')"
                                class="text-destructive hover:text-destructive"
                                @click="handleDelete(role)"
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

    <!-- 创建角色弹窗 -->
    <Dialog v-model:open="showCreate">
        <DialogContent class="sm:max-w-sm">
            <DialogHeader>
                <DialogTitle>{{ t('management.createRole') }}</DialogTitle>
            </DialogHeader>
            <div class="space-y-3">
                <div class="space-y-1">
                    <Label>{{ t('management.roleName') }} *</Label>
                    <Input v-model="createForm.name" />
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
