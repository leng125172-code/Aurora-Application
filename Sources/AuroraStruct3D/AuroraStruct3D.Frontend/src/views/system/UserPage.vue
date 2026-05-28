<script setup lang="ts">
/**
 * 用户管理页面：分页查询、创建、重置密码、锁定/禁用
 */
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { Lock, Plus, RefreshCw, Search, KeyRound, Trash2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogClose } from '@/components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
    getUserPageAsync,
    createUserAsync,
    deleteUserAsync,
    resetUserPasswordAsync,
    lockUserAsync,
    type UserDto,
} from '@/api/management'

const { t } = useI18n()

// ——— 分页状态 ———
const pageIndex = ref(1)
const pageSize = ref(20)
const filter = ref('')
const total = ref(0)
const loading = ref(false)
const users = ref<UserDto[]>([])

async function loadUsers(): Promise<void> {
    loading.value = true
    try {
        const result = await getUserPageAsync({
            pageIndex: pageIndex.value,
            pageSize: pageSize.value,
            filter: filter.value || null,
        })
        users.value = result.items ?? []
        total.value = result.totalCount ?? 0
    } finally {
        loading.value = false
    }
}

onMounted(loadUsers)

// ——— 搜索 ———
function handleSearch(): void {
    pageIndex.value = 1
    loadUsers()
}

// ——— 创建用户弹窗 ———
const showCreate = ref(false)
const createForm = ref({
    userName: '',
    name: '',
    email: '',
    password: '',
    isActive: true,
})
const creating = ref(false)

function openCreate(): void {
    createForm.value = { userName: '', name: '', email: '', password: '', isActive: true }
    showCreate.value = true
}

async function submitCreate(): Promise<void> {
    if (!createForm.value.userName || !createForm.value.email || !createForm.value.password) {
        toast.warning(t('common.requiredFields'))
        return
    }
    creating.value = true
    try {
        await createUserAsync(createForm.value)
        toast.success(t('common.success'))
        showCreate.value = false
        await loadUsers()
    } finally {
        creating.value = false
    }
}

// ——— 重置密码弹窗 ———
const showReset = ref(false)
const resetTarget = ref<UserDto | null>(null)
const newPassword = ref('')
const resetting = ref(false)

function openReset(user: UserDto): void {
    resetTarget.value = user
    newPassword.value = ''
    showReset.value = true
}

async function submitReset(): Promise<void> {
    if (!resetTarget.value?.id || !newPassword.value) return
    resetting.value = true
    try {
        await resetUserPasswordAsync({ id: resetTarget.value.id, password: newPassword.value })
        toast.success(t('common.success'))
        showReset.value = false
    } finally {
        resetting.value = false
    }
}

// ——— 锁定用户弹窗 ———
const showLock = ref(false)
const lockTarget = ref<UserDto | null>(null)
const lockSeconds = ref(3600)
const locking = ref(false)

function openLock(user: UserDto): void {
    lockTarget.value = user
    lockSeconds.value = 3600
    showLock.value = true
}

async function submitLock(): Promise<void> {
    if (!lockTarget.value?.id) return
    locking.value = true
    try {
        await lockUserAsync({ id: lockTarget.value.id, seconds: lockSeconds.value })
        toast.success(t('common.success'))
        showLock.value = false
        await loadUsers()
    } finally {
        locking.value = false
    }
}

// ——— 删除用户 ———
async function handleDelete(user: UserDto): Promise<void> {
    if (!user.id) return
    if (!confirm(t('management.confirmDelete', { name: user.userName }))) return
    try {
        await deleteUserAsync(user.id)
        toast.success(t('common.success'))
        await loadUsers()
    } catch {
        // 错误已在拦截器处理
    }
}

// ——— 分页 ———
const totalPages = ref(0)

function updateTotalPages(): void {
    totalPages.value = Math.ceil(total.value / pageSize.value)
}

function prevPage(): void {
    if (pageIndex.value > 1) {
        pageIndex.value--
        loadUsers()
    }
}

function nextPage(): void {
    if (pageIndex.value < totalPages.value) {
        pageIndex.value++
        loadUsers()
    }
}

// 监听 total 变化更新总页数
import { watch } from 'vue'
watch(total, updateTotalPages, { immediate: true })
</script>

<template>
    <div class="space-y-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.users') }}</h1>
            <div class="flex gap-2">
                <Button variant="outline" size="icon" :disabled="loading" @click="loadUsers">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button @click="openCreate">
                    <Plus class="mr-1 size-4" />
                    {{ t('management.createUser') }}
                </Button>
            </div>
        </div>

        <!-- 搜索栏 -->
        <div class="flex gap-2">
            <Input
                v-model="filter"
                :placeholder="t('management.searchUser')"
                class="max-w-xs"
                @keydown.enter="handleSearch"
            />
            <Button variant="outline" @click="handleSearch">
                <Search class="mr-1 size-4" />
                {{ t('common.search') }}
            </Button>
        </div>

        <!-- 数据表格 -->
        <div class="rounded-md border">
            <Table>
                <TableHeader>
                    <TableRow>
                        <TableHead>{{ t('management.userName') }}</TableHead>
                        <TableHead>{{ t('management.displayName') }}</TableHead>
                        <TableHead>{{ t('management.email') }}</TableHead>
                        <TableHead>{{ t('management.phone') }}</TableHead>
                        <TableHead>{{ t('management.status') }}</TableHead>
                        <TableHead class="text-right">{{ t('common.action') }}</TableHead>
                    </TableRow>
                </TableHeader>
                <TableBody>
                    <TableRow v-if="loading">
                        <TableCell colspan="6" class="text-center py-8 text-muted-foreground">
                            {{ t('common.loading') }}
                        </TableCell>
                    </TableRow>
                    <TableRow v-else-if="users.length === 0">
                        <TableCell colspan="6" class="text-center py-8 text-muted-foreground">
                            {{ t('management.noData') }}
                        </TableCell>
                    </TableRow>
                    <TableRow v-for="user in users" :key="user.id">
                        <TableCell class="font-medium">{{ user.userName }}</TableCell>
                        <TableCell>{{ user.name }}{{ user.surname ? ' ' + user.surname : '' }}</TableCell>
                        <TableCell>{{ user.email }}</TableCell>
                        <TableCell>{{ user.phoneNumber ?? '-' }}</TableCell>
                        <TableCell>
                            <Badge :variant="user.isActive ? 'default' : 'secondary'">
                                {{ user.isActive ? t('management.active') : t('management.inactive') }}
                            </Badge>
                        </TableCell>
                        <TableCell class="text-right">
                            <div class="flex justify-end gap-1">
                                <Button
                                    variant="ghost"
                                    size="icon"
                                    :title="t('management.resetPassword')"
                                    @click="openReset(user)"
                                >
                                    <KeyRound class="size-4" />
                                </Button>
                                <Button
                                    variant="ghost"
                                    size="icon"
                                    :title="t('management.lockUser')"
                                    @click="openLock(user)"
                                >
                                    <Lock class="size-4" />
                                </Button>
                                <Button
                                    variant="ghost"
                                    size="icon"
                                    :title="t('common.delete')"
                                    class="text-destructive hover:text-destructive"
                                    @click="handleDelete(user)"
                                >
                                    <Trash2 class="size-4" />
                                </Button>
                            </div>
                        </TableCell>
                    </TableRow>
                </TableBody>
            </Table>
        </div>

        <!-- 分页 -->
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

    <!-- 创建用户弹窗 -->
    <Dialog v-model:open="showCreate">
        <DialogContent class="sm:max-w-md">
            <DialogHeader>
                <DialogTitle>{{ t('management.createUser') }}</DialogTitle>
            </DialogHeader>
            <div class="space-y-3">
                <div class="space-y-1">
                    <Label>{{ t('management.userName') }} *</Label>
                    <Input v-model="createForm.userName" />
                </div>
                <div class="space-y-1">
                    <Label>{{ t('management.displayName') }}</Label>
                    <Input v-model="createForm.name" />
                </div>
                <div class="space-y-1">
                    <Label>{{ t('management.email') }} *</Label>
                    <Input v-model="createForm.email" type="email" />
                </div>
                <div class="space-y-1">
                    <Label>{{ t('management.password') }} *</Label>
                    <Input v-model="createForm.password" type="password" />
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

    <!-- 重置密码弹窗 -->
    <Dialog v-model:open="showReset">
        <DialogContent class="sm:max-w-sm">
            <DialogHeader>
                <DialogTitle>{{ t('management.resetPassword') }}: {{ resetTarget?.userName }}</DialogTitle>
            </DialogHeader>
            <div class="space-y-1">
                <Label>{{ t('management.newPassword') }}</Label>
                <Input v-model="newPassword" type="password" />
            </div>
            <DialogFooter>
                <DialogClose as-child>
                    <Button variant="outline">{{ t('common.cancel') }}</Button>
                </DialogClose>
                <Button :disabled="resetting || !newPassword" @click="submitReset">
                    {{ resetting ? t('common.loading') : t('common.confirm') }}
                </Button>
            </DialogFooter>
        </DialogContent>
    </Dialog>

    <!-- 锁定用户弹窗 -->
    <Dialog v-model:open="showLock">
        <DialogContent class="sm:max-w-sm">
            <DialogHeader>
                <DialogTitle>{{ t('management.lockUser') }}: {{ lockTarget?.userName }}</DialogTitle>
            </DialogHeader>
            <div class="space-y-1">
                <Label>{{ t('management.lockDuration') }}</Label>
                <Select v-model="lockSeconds">
                    <SelectTrigger>
                        <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem :value="300">5 {{ t('management.minutes') }}</SelectItem>
                        <SelectItem :value="1800">30 {{ t('management.minutes') }}</SelectItem>
                        <SelectItem :value="3600">1 {{ t('management.hours') }}</SelectItem>
                        <SelectItem :value="86400">24 {{ t('management.hours') }}</SelectItem>
                    </SelectContent>
                </Select>
            </div>
            <DialogFooter>
                <DialogClose as-child>
                    <Button variant="outline">{{ t('common.cancel') }}</Button>
                </DialogClose>
                <Button :disabled="locking" @click="submitLock">
                    {{ locking ? t('common.loading') : t('common.confirm') }}
                </Button>
            </DialogFooter>
        </DialogContent>
    </Dialog>
</template>
