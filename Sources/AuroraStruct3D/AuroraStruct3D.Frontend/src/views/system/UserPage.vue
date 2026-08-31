<script setup lang="ts">
/**
 * 用户管理页面：分页查询、创建、重置密码、锁定/禁用
 */
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Lock, Plus, RefreshCw, Search, KeyRound, Trash2 } from '@lucide/vue'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { AppCard } from '@/components/primevue'
import { useAppToast } from '@/composables/useAppToast'
import {
    getUserPageAsync,
    createUserAsync,
    deleteUserAsync,
    resetUserPasswordAsync,
    setUserActiveAsync,
    type UserDto,
} from '@/api/management'

const { t } = useI18n()
const toast = useAppToast()
const confirm = useConfirm()

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

// ——— 启用/禁用用户 ———
async function toggleUserActive(user: UserDto): Promise<void> {
    if (!user.id) return
    try {
        await setUserActiveAsync({ id: user.id, isActive: !user.isActive })
        toast.success(t('common.success'))
        await loadUsers()
    } catch {
        // 错误已在拦截器处理
    }
}

// ——— 删除用户 ———
function handleDelete(user: UserDto): void {
    if (!user.id) return
    confirm.require({
        message: t('management.confirmDelete', { name: user.userName }),
        header: t('common.delete'),
        icon: 'pi pi-exclamation-triangle',
        acceptProps: { severity: 'danger', size: 'small' },
        rejectProps: { severity: 'secondary', outlined: true, size: 'small' },
        accept: async () => {
            try {
                await deleteUserAsync(user.id!)
                toast.success(t('common.success'))
                await loadUsers()
            } catch {
                // 错误已在拦截器处理
            }
        },
    })
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
watch(total, updateTotalPages, { immediate: true })
</script>

<template>
    <div class="space-y-4">
        <div class="flex items-center justify-between">
            <h1 class="text-2xl font-bold tracking-tight">{{ t('menu.users') }}</h1>
            <div class="flex gap-2">
                <Button size="small" severity="secondary" outlined :disabled="loading" @click="loadUsers">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button size="small" @click="openCreate">
                    <Plus class="mr-1 size-4" />
                    {{ t('management.createUser') }}
                </Button>
            </div>
        </div>

        <!-- 搜索栏 -->
        <div class="flex gap-2">
            <InputText
                v-model="filter"
                :placeholder="t('management.searchUser')"
                class="max-w-xs"
                @keydown.enter="handleSearch"
            />
            <Button size="small" severity="secondary" outlined @click="handleSearch">
                <Search class="mr-1 size-4" />
                {{ t('common.search') }}
            </Button>
        </div>

        <!-- 数据表格 -->
        <AppCard :beam="false">
            <DataTable :value="users" data-key="id" size="small" striped-rows scrollable :loading="loading">
                <template #empty>
                    <div class="py-8 text-center text-muted-foreground">{{ t('management.noData') }}</div>
                </template>
                <template #loading>
                    <div class="py-8 text-center text-muted-foreground">{{ t('common.loading') }}</div>
                </template>
                <Column :header="t('management.userName')">
                    <template #body="{ data }: { data: UserDto }">
                        <div class="max-w-[150px] truncate font-medium" :title="data.userName ?? undefined">
                            {{ data.userName }}
                        </div>
                    </template>
                </Column>
                <Column :header="t('management.displayName')">
                    <template #body="{ data }: { data: UserDto }">
                        <div
                            class="max-w-[150px] truncate"
                            :title="data.name + (data.surname ? ' ' + data.surname : '')"
                        >
                            {{ data.name }}{{ data.surname ? ' ' + data.surname : '' }}
                        </div>
                    </template>
                </Column>
                <Column :header="t('management.email')">
                    <template #body="{ data }: { data: UserDto }">
                        <div class="max-w-[200px] truncate" :title="data.email ?? undefined">{{ data.email }}</div>
                    </template>
                </Column>
                <Column :header="t('management.phone')">
                    <template #body="{ data }: { data: UserDto }">
                        <span class="whitespace-nowrap">{{ data.phoneNumber ?? '-' }}</span>
                    </template>
                </Column>
                <Column :header="t('management.status')">
                    <template #body="{ data }: { data: UserDto }">
                        <Tag
                            :severity="data.isActive ? 'success' : 'secondary'"
                            :value="data.isActive ? t('management.active') : t('management.inactive')"
                        />
                    </template>
                </Column>
                <Column :header="t('common.action')" header-class="text-right" body-class="text-right">
                    <template #body="{ data }: { data: UserDto }">
                        <div class="flex justify-end gap-1">
                            <Button
                                text
                                severity="secondary"
                                size="small"
                                :title="t('management.resetPassword')"
                                @click="openReset(data)"
                            >
                                <KeyRound class="size-4" />
                            </Button>
                            <Button
                                text
                                severity="secondary"
                                size="small"
                                :title="data.isActive ? t('management.inactive') : t('management.active')"
                                @click="toggleUserActive(data)"
                            >
                                <Lock class="size-4" />
                            </Button>
                            <Button
                                text
                                severity="danger"
                                size="small"
                                :title="t('common.delete')"
                                @click="handleDelete(data)"
                            >
                                <Trash2 class="size-4" />
                            </Button>
                        </div>
                    </template>
                </Column>
            </DataTable>
        </AppCard>

        <!-- 分页 -->
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

    <!-- 创建用户弹窗 -->
    <Dialog v-model:visible="showCreate" modal :header="t('management.createUser')" :style="{ width: '460px' }">
        <div class="space-y-3">
            <div class="space-y-1">
                <label class="text-sm">{{ t('management.userName') }} *</label>
                <InputText v-model="createForm.userName" class="w-full" />
            </div>
            <div class="space-y-1">
                <label class="text-sm">{{ t('management.displayName') }}</label>
                <InputText v-model="createForm.name" class="w-full" />
            </div>
            <div class="space-y-1">
                <label class="text-sm">{{ t('management.email') }} *</label>
                <InputText v-model="createForm.email" type="email" class="w-full" />
            </div>
            <div class="space-y-1">
                <label class="text-sm">{{ t('management.password') }} *</label>
                <InputText v-model="createForm.password" type="password" class="w-full" />
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

    <!-- 重置密码弹窗 -->
    <Dialog
        v-model:visible="showReset"
        modal
        :header="`${t('management.resetPassword')}: ${resetTarget?.userName ?? ''}`"
        :style="{ width: '400px' }"
    >
        <div class="space-y-1">
            <label class="text-sm">{{ t('management.newPassword') }}</label>
            <InputText v-model="newPassword" type="password" class="w-full" />
        </div>
        <template #footer>
            <Button severity="secondary" outlined size="small" @click="showReset = false">
                {{ t('common.cancel') }}
            </Button>
            <Button size="small" :disabled="resetting || !newPassword" @click="submitReset">
                {{ resetting ? t('common.loading') : t('common.confirm') }}
            </Button>
        </template>
    </Dialog>

    <ConfirmDialog />
</template>
