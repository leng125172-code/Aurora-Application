<script setup lang="ts">
/**
 * 三维数模管理页面
 * 功能：多条件查询、上传（带进度/取消）、重命名、删除、重试转换、Three.js PLY 预览
 *
 * 上传流程：
 *  1. 用户拖放/选择文件并点击上传
 *  2. 逐文件调用上传接口，接口返回 needsConversion 字段
 *  3. 若有文件需要转换，建立 SignalR 连接（/signalr-hubs/product-model）
 *  4. 需要转换的文件显示"等待转换"钟表图标
 *  5. 收到 ReceiveConversionStarted 通知后，对应文件改为"转换中"转圈图标
 *  6. 收到 ReceiveConversionFinished 通知后，改为"成功"勾号或"失败"叉号
 *  7. 全部转换完成后断开 SignalR 连接，刷新列表
 */
import { ref, computed, watch, nextTick, onMounted, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import ConfirmDialog from 'primevue/confirmdialog'
import { AppCard } from '@/components/primevue'
import * as THREE from 'three'
import { PLYLoader } from 'three/examples/jsm/loaders/PLYLoader.js'
import { OBJLoader } from 'three/examples/jsm/loaders/OBJLoader.js'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import * as signalR from '@microsoft/signalr'
import {
    Upload,
    RefreshCw,
    Search,
    Eye,
    Pencil,
    Trash2,
    RotateCcw,
    Download,
    X,
    Clock,
    CheckCircle,
    XCircle,
    Loader2,
    Eraser,
} from '@lucide/vue'
import Tag from 'primevue/tag'
import Progress from 'primevue/progressbar'
import {
    ProductModelFormat,
    ProductModelConversionStatus,
    getProductModelListAsync,
    uploadProductModelAsync,
    updateProductModelNameAsync,
    deleteProductModelAsync,
    retryConversionAsync,
    downloadProductModelAsync,
    cleanUpOrphanedRecordsAsync,
    type ProductModelDto,
    type GetProductModelListInput,
} from '@/api/product-models'
import { showErrorToastOnce } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import { useAppToast } from '@/composables/useAppToast'

const { t } = useI18n()
const authStore = useAuthStore()
const toast = useAppToast()
const confirm = useConfirm()

// ===================== 列表状态 =====================

const loading = ref(false)
const items = ref<ProductModelDto[]>([])
const total = ref(0)
const skipCount = ref(0)
const maxResultCount = ref(20)

// 搜索条件
const filterText = ref('')
const filterFormat = ref<ProductModelFormat | null>(null)
const filterStatus = ref<ProductModelConversionStatus | null>(null)
const filterStartTime = ref<Date | null>(null)
const filterEndTime = ref<Date | null>(null)

/** 格式选项 */
const formatOptions = [
    { label: 'PLY', value: ProductModelFormat.PLY },
    { label: 'OBJ', value: ProductModelFormat.OBJ },
    { label: 'STEP', value: ProductModelFormat.STEP },
    { label: 'IGES', value: ProductModelFormat.IGES },
    { label: 'STL', value: ProductModelFormat.STL },
    { label: 'GLB', value: ProductModelFormat.GLB },
    { label: 'GLTF', value: ProductModelFormat.GLTF },
    { label: 'PCD', value: ProductModelFormat.PCD },
]

/** 转换状态选项（响应式，支持多语言） */
const statusOptions = computed(() => [
    { label: t('productModel.statusNotRequired'), value: ProductModelConversionStatus.NotRequired },
    { label: t('productModel.statusPending'), value: ProductModelConversionStatus.Pending },
    { label: t('productModel.statusConverting'), value: ProductModelConversionStatus.Converting },
    { label: t('productModel.statusSuccess'), value: ProductModelConversionStatus.Success },
    { label: t('productModel.statusFailed'), value: ProductModelConversionStatus.Failed },
])

/** 转换状态 Tag severity 映射 */
function statusVariant(status: ProductModelConversionStatus): 'success' | 'secondary' | 'danger' | 'info' {
    switch (status) {
        case ProductModelConversionStatus.Success:
        case ProductModelConversionStatus.NotRequired:
            return 'success'
        case ProductModelConversionStatus.Converting:
        case ProductModelConversionStatus.Pending:
            return 'secondary'
        case ProductModelConversionStatus.Failed:
            return 'danger'
        default:
            return 'info'
    }
}

/** 转换状态显示文字 */
function statusLabel(status: ProductModelConversionStatus): string {
    switch (status) {
        case ProductModelConversionStatus.NotRequired:
            return t('productModel.statusNotRequired')
        case ProductModelConversionStatus.Pending:
            return t('productModel.statusPending')
        case ProductModelConversionStatus.Converting:
            return t('productModel.statusConverting')
        case ProductModelConversionStatus.Success:
            return t('productModel.statusSuccess')
        case ProductModelConversionStatus.Failed:
            return t('productModel.statusFailed')
        default:
            return String(status)
    }
}

/** 格式化文件大小 */
function formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
    return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}

async function loadList(): Promise<void> {
    loading.value = true
    try {
        const input: GetProductModelListInput = {
            filter: filterText.value || null,
            fileFormat: filterFormat.value,
            conversionStatus: filterStatus.value,
            startTime: filterStartTime.value ? filterStartTime.value.toISOString() : null,
            endTime: filterEndTime.value ? filterEndTime.value.toISOString() : null,
            skipCount: skipCount.value,
            maxResultCount: maxResultCount.value,
            sorting: 'CreationTime DESC',
        }
        const result = await getProductModelListAsync(input)
        items.value = result.items
        total.value = result.totalCount
    } finally {
        loading.value = false
    }
}

function handleSearch(): void {
    skipCount.value = 0
    loadList()
}

function handleReset(): void {
    filterText.value = ''
    filterFormat.value = null
    filterStatus.value = null
    filterStartTime.value = null
    filterEndTime.value = null
    skipCount.value = 0
    loadList()
}

const totalPages = computed(() => Math.ceil(total.value / maxResultCount.value))
const currentPage = computed(() => Math.floor(skipCount.value / maxResultCount.value) + 1)

function goToPage(page: number): void {
    skipCount.value = (page - 1) * maxResultCount.value
    loadList()
}

// ===================== 上传对话框 =====================

/** 文件上传阶段状态 */
type UploadState = 'idle' | 'uploading' | 'done' | 'error' | 'cancelled'

/**
 * 转换阶段状态：
 *  - 'none'      无需转换
 *  - 'waiting'   等待转换（已入队，尚未开始）
 *  - 'converting' 转换中
 *  - 'success'   转换成功
 *  - 'failed'    转换失败
 */
type ConversionState = 'none' | 'waiting' | 'converting' | 'success' | 'failed'

interface UploadQueueItem {
    id: string
    file: File
    state: UploadState
    progress: number
    errorMessage?: string
    abortController?: AbortController
    /** 上传成功后从接口返回的数模 ID（用于匹配 SignalR 通知） */
    productModelId?: string
    /** 转换状态图标 */
    conversionState: ConversionState
}

const showUpload = ref(false)
const isDragging = ref(false)
const uploadQueue = ref<UploadQueueItem[]>([])

/** SignalR 连接（转换进度推送） */
let conversionHub: signalR.HubConnection | null = null

/** 断开并释放 SignalR 转换连接 */
function stopConversionHub(): void {
    if (conversionHub) {
        conversionHub.stop().catch(() => {})
        conversionHub = null
    }
}

/**
 * 建立 SignalR 连接，监听转换开始/完成通知。
 * 只监听 uploadQueue 中有 productModelId 且 conversionState === 'waiting' 的文件。
 * 全部完成后自动断开连接并刷新列表。
 */
async function startConversionHub(): Promise<void> {
    stopConversionHub()

    conversionHub = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/product-model', {
            accessTokenFactory: () => authStore.token ?? '',
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build()

    conversionHub.on('ReceiveConversionStarted', (productModelId: string) => {
        const item = uploadQueue.value.find(
            (q) => q.productModelId === productModelId && q.conversionState === 'waiting'
        )
        if (item) item.conversionState = 'converting'
    })

    conversionHub.on('ReceiveConversionFinished', (productModelId: string, success: boolean) => {
        const item = uploadQueue.value.find((q) => q.productModelId === productModelId)
        if (item) {
            item.conversionState = success ? 'success' : 'failed'
        }
        // 检查是否全部转换完毕
        const pendingCount = uploadQueue.value.filter(
            (q) => q.conversionState === 'waiting' || q.conversionState === 'converting'
        ).length
        if (pendingCount === 0) {
            stopConversionHub()
            loadList()
        }
    })

    try {
        await conversionHub.start()
    } catch {
        // 连接失败不阻断流程，后端转换仍会继续
    }
}

function openUpload(): void {
    showUpload.value = true
    uploadQueue.value = []
    isDragging.value = false
}

function addFilesToQueue(files: FileList | File[]): void {
    for (const file of Array.from(files)) {
        uploadQueue.value.push({
            id: `${Date.now()}-${Math.random()}`,
            file,
            state: 'idle',
            progress: 0,
            conversionState: 'none',
        })
    }
}

function handleDrop(event: DragEvent): void {
    isDragging.value = false
    if (event.dataTransfer?.files) {
        addFilesToQueue(event.dataTransfer.files)
    }
}

function handleFileInput(event: Event): void {
    const input = event.target as HTMLInputElement
    if (input.files) {
        addFilesToQueue(input.files)
        input.value = ''
    }
}

function cancelUpload(item: UploadQueueItem): void {
    item.abortController?.abort()
    item.state = 'cancelled'
}

function removeQueueItem(id: string): void {
    uploadQueue.value = uploadQueue.value.filter((i) => i.id !== id)
}

async function startUploadAll(): Promise<void> {
    const pending = uploadQueue.value.filter((i) => i.state === 'idle')
    // 提前建立 SignalR 连接，防止 Job 在 UoW 提交后立即执行，通知在连接建立前发出（竞态条件）
    await startConversionHub()

    await Promise.all(
        pending.map(async (item) => {
            const ac = new AbortController()
            item.abortController = ac
            item.state = 'uploading'
            item.progress = 0
            try {
                const dto: ProductModelDto = await uploadProductModelAsync(
                    item.file,
                    null,
                    (pct) => {
                        item.progress = pct
                    },
                    ac.signal
                )
                item.state = 'done'
                item.progress = 100
                item.productModelId = dto.id
                // 根据接口返回决定是否显示转换等待图标
                item.conversionState = dto.needsConversion ? 'waiting' : 'none'
            } catch (e: unknown) {
                if (ac.signal.aborted) return
                item.state = 'error'
                item.errorMessage = e instanceof Error ? e.message : String(e)
            }
        })
    )

    // 检查是否有文件需要转换
    const needsConversionItems = uploadQueue.value.filter((i) => i.conversionState === 'waiting')
    if (needsConversionItems.length === 0) {
        // 无需转换，断开 Hub 连接并刷新列表
        stopConversionHub()
        if (uploadQueue.value.every((i) => i.state === 'done' || i.state === 'cancelled')) {
            showUpload.value = false
        }
        await loadList()
    }
    // 有文件需要转换：保持 Hub 连接，等待 ReceiveConversionFinished 回调处理后续逻辑
}

// ===================== 重命名对话框 =====================

const showRename = ref(false)
const renaming = ref(false)
const renameName = ref('')
const renameTargetId = ref<string | null>(null)

function openRename(item: ProductModelDto): void {
    renameTargetId.value = item.id
    renameName.value = item.name
    showRename.value = true
}

async function submitRename(): Promise<void> {
    if (!renameTargetId.value || !renameName.value.trim()) return
    renaming.value = true
    try {
        await updateProductModelNameAsync(renameTargetId.value, { name: renameName.value.trim() })
        toast.success(t('productModel.renameSuccess'))
        showRename.value = false
        await loadList()
    } catch (e: unknown) {
        showErrorToastOnce(e)
    } finally {
        renaming.value = false
    }
}

// ===================== 删除确认 =====================

const deleting = ref<string | null>(null)

function openDeleteConfirm(item: ProductModelDto): void {
    confirm.require({
        message: t('productModel.confirmDelete', { name: item.name }),
        header: t('common.confirmDeleteTitle'),
        icon: 'pi pi-exclamation-triangle',
        acceptProps: { severity: 'danger', size: 'small', label: t('common.delete') },
        rejectProps: { severity: 'secondary', outlined: true, size: 'small', label: t('common.cancel') },
        accept: async () => {
            deleting.value = item.id
            try {
                await deleteProductModelAsync(item.id)
                toast.success(t('productModel.deleteSuccess'))
                await loadList()
            } catch (e: unknown) {
                showErrorToastOnce(e)
            } finally {
                deleting.value = null
            }
        },
    })
}

// ===================== 重试转换 =====================

const retrying = ref<string | null>(null)

async function handleRetry(item: ProductModelDto): Promise<void> {
    retrying.value = item.id
    try {
        await retryConversionAsync(item.id)
        toast.success(t('productModel.retrySuccess'))
        await loadList()
    } catch (e: unknown) {
        showErrorToastOnce(e)
    } finally {
        retrying.value = null
    }
}

// ===================== 清理孤立记录 =====================

const cleaningUp = ref(false)

function openCleanUpConfirm(): void {
    confirm.require({
        message: t('productModel.cleanUpConfirm'),
        header: t('productModel.cleanUpTitle'),
        icon: 'pi pi-exclamation-triangle',
        acceptProps: { severity: 'danger', size: 'small', label: t('common.confirm') },
        rejectProps: { severity: 'secondary', outlined: true, size: 'small', label: t('common.cancel') },
        accept: handleCleanUp,
    })
}

async function handleCleanUp(): Promise<void> {
    cleaningUp.value = true
    try {
        const count = await cleanUpOrphanedRecordsAsync()
        toast.success(t('productModel.cleanUpSuccess', { count }))
        await loadList()
    } catch (e: unknown) {
        showErrorToastOnce(e)
    } finally {
        cleaningUp.value = false
    }
}

// ===================== 下载 =====================

async function handleDownload(item: ProductModelDto): Promise<void> {
    try {
        const blob = await downloadProductModelAsync(item.id)
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = item.originalFileName || item.name
        a.click()
        URL.revokeObjectURL(url)
    } catch (e: unknown) {
        showErrorToastOnce(e)
    }
}

// ===================== PLY 预览（Three.js） =====================
const showPreview = ref(false)
const previewLoading = ref(false)
const previewError = ref<string | null>(null)
const previewTarget = ref<ProductModelDto | null>(null)
const previewCanvasRef = ref<HTMLCanvasElement | null>(null)

let threeRenderer: THREE.WebGLRenderer | null = null
let threeAnimFrame: number | null = null

function disposeThree(): void {
    if (threeAnimFrame !== null) {
        cancelAnimationFrame(threeAnimFrame)
        threeAnimFrame = null
    }
    if (threeRenderer) {
        threeRenderer.dispose()
        threeRenderer = null
    }
}

function openPreview(item: ProductModelDto): void {
    disposeThree()
    previewTarget.value = item
    previewError.value = null
    previewLoading.value = false
    showPreview.value = true
}

async function initThreePreview(): Promise<void> {
    if (!previewTarget.value || !previewCanvasRef.value) return
    disposeThree()
    previewLoading.value = true
    previewError.value = null
    try {
        const blob = await downloadProductModelAsync(previewTarget.value.id)
        const arrayBuffer = await blob.arrayBuffer()

        const canvas = previewCanvasRef.value
        const renderer = new THREE.WebGLRenderer({ canvas, antialias: true })
        renderer.setSize(canvas.clientWidth, canvas.clientHeight, false)
        renderer.setPixelRatio(window.devicePixelRatio)
        threeRenderer = renderer

        const scene = new THREE.Scene()
        scene.background = new THREE.Color(0x1a1a1a)
        scene.add(new THREE.AmbientLight(0xffffff, 0.8))
        const dirLight = new THREE.DirectionalLight(0xffffff, 1)
        dirLight.position.set(5, 10, 7)
        scene.add(dirLight)

        const camera = new THREE.PerspectiveCamera(60, canvas.clientWidth / canvas.clientHeight, 0.01, 1000)
        const controls = new OrbitControls(camera, canvas)
        controls.enableDamping = true

        let sceneObject: THREE.Object3D

        if (previewTarget.value.fileFormat === ProductModelFormat.OBJ) {
            // OBJ 格式：使用 OBJLoader 解析
            const text = new TextDecoder().decode(arrayBuffer)
            const obj = new OBJLoader().parse(text)
            obj.traverse((child) => {
                if (child instanceof THREE.Mesh) {
                    child.material = new THREE.MeshStandardMaterial({ color: 0x888888 })
                }
            })
            sceneObject = obj
        } else {
            // PLY 格式（含转换后的 PLY）：使用 PLYLoader
            const geometry = new PLYLoader().parse(arrayBuffer)
            geometry.computeVertexNormals()
            const material = new THREE.MeshStandardMaterial({
                color: 0x888888,
                vertexColors: geometry.hasAttribute('color'),
            })
            sceneObject = new THREE.Mesh(geometry, material)
        }

        scene.add(sceneObject)

        // 计算包围盒，居中模型并调整摄像机距离
        const box = new THREE.Box3().setFromObject(sceneObject)
        const center = new THREE.Vector3()
        box.getCenter(center)
        sceneObject.position.sub(center)
        const size = new THREE.Vector3()
        box.getSize(size)
        const maxDim = Math.max(size.x, size.y, size.z)
        camera.position.set(0, 0, maxDim * 2)
        controls.update()

        function animate(): void {
            threeAnimFrame = requestAnimationFrame(animate)
            controls.update()
            renderer.render(scene, camera)
        }
        animate()
    } catch (e: unknown) {
        previewError.value = e instanceof Error ? e.message : String(e)
    } finally {
        previewLoading.value = false
    }
}

watch(showPreview, async (open) => {
    if (open) {
        await nextTick()
        initThreePreview()
    } else {
        disposeThree()
    }
})

onMounted(loadList)

onBeforeUnmount(() => {
    disposeThree()
    stopConversionHub()
})
</script>

<template>
    <div class="space-y-4">
        <!-- 页面标题 -->
        <h1 class="text-2xl font-bold tracking-tight">{{ t('productModel.title') }}</h1>

        <!-- 主内容卡片：操作按钮 + 筛选 + 表格 + 分页 -->
        <AppCard :beam="true">
            <!-- 操作按钮区 -->
            <div class="flex items-center gap-2 border-b border-border/40 px-4 py-3">
                <Button severity="secondary" outlined size="small" :disabled="loading" @click="loadList">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button severity="secondary" outlined size="small" @click="openCleanUpConfirm">
                    <Eraser class="mr-1 size-4" />
                    {{ t('productModel.cleanUp') }}
                </Button>
                <Button size="small" @click="openUpload">
                    <Upload class="mr-1 size-4" />
                    {{ t('productModel.upload') }}
                </Button>
            </div>

            <!-- 筛选区 -->
            <div class="flex flex-col gap-3 border-b border-border/40 px-3 py-2">
                <div
                    class="grid items-center gap-x-3 gap-y-2"
                    style="grid-template-columns: repeat(auto-fill, 5.5rem 13rem)"
                >
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('productModel.name') }}</span>
                    <InputText
                        v-model="filterText"
                        size="small"
                        class="!text-xs w-full"
                        :placeholder="t('productModel.searchPlaceholder')"
                        @keydown.enter="handleSearch"
                    />
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('productModel.format') }}</span>
                    <Select
                        v-model="filterFormat"
                        :options="formatOptions"
                        option-label="label"
                        option-value="value"
                        :placeholder="t('productModel.formatPlaceholder')"
                        show-clear
                        size="small"
                        class="!text-xs w-full"
                        :pt="{
                            root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                            label: {
                                class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                            },
                            dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                        }"
                        @change="handleSearch"
                    />
                    <span class="whitespace-nowrap text-sm text-muted-foreground">
                        {{ t('productModel.conversionStatus') }}
                    </span>
                    <Select
                        v-model="filterStatus"
                        :options="statusOptions"
                        option-label="label"
                        option-value="value"
                        :placeholder="t('productModel.statusPlaceholder')"
                        show-clear
                        size="small"
                        class="!text-xs w-full"
                        :pt="{
                            root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                            label: {
                                class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                            },
                            dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                        }"
                        @change="handleSearch"
                    />
                    <span class="whitespace-nowrap text-sm text-muted-foreground">
                        {{ t('productModel.startDate') }}
                    </span>
                    <DatePicker
                        v-model="filterStartTime"
                        show-time
                        show-icon
                        fluid
                        :showOnFocus="false"
                        size="small"
                        show-button-bar
                        @date-select="handleSearch"
                        @clear-click="handleSearch"
                    />
                    <span class="whitespace-nowrap text-sm text-muted-foreground">
                        {{ t('productModel.endDate') }}
                    </span>
                    <DatePicker
                        v-model="filterEndTime"
                        show-time
                        show-icon
                        fluid
                        :showOnFocus="false"
                        size="small"
                        show-button-bar
                        @date-select="handleSearch"
                        @clear-click="handleSearch"
                    />
                </div>
                <!-- 操作行 -->
                <div class="flex items-center gap-3">
                    <Button severity="secondary" outlined size="small" @click="handleSearch">
                        <Search class="mr-1 size-4" />
                        {{ t('common.search') }}
                    </Button>
                    <Button text severity="secondary" size="small" @click="handleReset">
                        {{ t('common.reset') }}
                    </Button>
                    <span class="ml-auto text-xs text-muted-foreground">
                        {{ t('management.totalRecords', { total }) }}
                    </span>
                </div>
            </div>

            <!-- 数据表格 (PrimeVue DataTable) -->
            <DataTable
                :value="items"
                :loading="loading"
                :lazy="true"
                :paginator="false"
                striped-rows
                size="small"
                data-key="id"
                :pt="{ root: { class: 'overflow-hidden' } }"
            >
                <template #empty>
                    <div class="py-6 text-center text-muted-foreground">{{ t('common.noData') }}</div>
                </template>
                <template #loading>
                    <div class="py-6 text-center text-muted-foreground">{{ t('common.loading') }}</div>
                </template>

                <Column :header="t('productModel.name')" style="min-width: 10rem; max-width: 16rem">
                    <template #body="{ data }">
                        <div class="truncate" :title="data.name">{{ data.name }}</div>
                    </template>
                </Column>
                <Column :header="t('productModel.format')" style="min-width: 5rem">
                    <template #body="{ data }">
                        <Tag severity="info" :value="data.fileFormatDisplay" />
                    </template>
                </Column>
                <Column :header="t('productModel.fileSize')" style="min-width: 6rem">
                    <template #body="{ data }">{{ formatFileSize(data.fileSizeBytes) }}</template>
                </Column>
                <Column :header="t('productModel.conversionStatus')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <Tag
                            :severity="statusVariant(data.conversionStatus)"
                            :value="statusLabel(data.conversionStatus)"
                        />
                    </template>
                </Column>
                <Column :header="t('productModel.uploader')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <div class="max-w-[120px] truncate" :title="data.uploaderUserName ?? '-'">
                            {{ data.uploaderUserName ?? '-' }}
                        </div>
                    </template>
                </Column>
                <Column :header="t('productModel.uploadTime')" style="min-width: 11rem">
                    <template #body="{ data }">
                        <span class="tabular-nums">{{ new Date(data.creationTime).toLocaleString() }}</span>
                    </template>
                </Column>
                <Column :header="t('common.actions')" style="min-width: 10rem">
                    <template #body="{ data }">
                        <div class="flex justify-end gap-1">
                            <!-- PLY 预览（仅 isReady 可预览） -->
                            <Button
                                v-if="data.isReady"
                                text
                                severity="secondary"
                                size="small"
                                :title="t('productModel.preview')"
                                @click="openPreview(data)"
                            >
                                <Eye class="size-4" />
                            </Button>
                            <!-- 下载 -->
                            <Button
                                text
                                severity="secondary"
                                size="small"
                                :title="t('productModel.download')"
                                @click="handleDownload(data)"
                            >
                                <Download class="size-4" />
                            </Button>
                            <!-- 重命名 -->
                            <Button
                                text
                                severity="secondary"
                                size="small"
                                :title="t('productModel.rename')"
                                @click="openRename(data)"
                            >
                                <Pencil class="size-4" />
                            </Button>
                            <!-- 重试转换（仅失败状态） -->
                            <Button
                                v-if="data.conversionStatus === ProductModelConversionStatus.Failed"
                                text
                                severity="secondary"
                                size="small"
                                :title="t('productModel.retryConversion')"
                                :disabled="retrying === data.id"
                                @click="handleRetry(data)"
                            >
                                <RotateCcw :class="['size-4', retrying === data.id && 'animate-spin']" />
                            </Button>
                            <!-- 删除 -->
                            <Button
                                text
                                severity="secondary"
                                size="small"
                                :title="t('common.delete')"
                                :disabled="deleting === data.id"
                                @click="openDeleteConfirm(data)"
                            >
                                <Trash2 class="size-4 text-destructive" />
                            </Button>
                        </div>
                    </template>
                </Column>
            </DataTable>

            <!-- 自定义分页控件 -->
            <div
                v-if="total > maxResultCount"
                class="flex items-center justify-between border-t border-border/50 px-4 py-3 text-sm"
            >
                <span class="text-xs text-muted-foreground">
                    {{ t('management.totalRecords', { total }) }}
                    &nbsp;·&nbsp;
                    {{
                        t('management.pageRange', {
                            from: (currentPage - 1) * maxResultCount + 1,
                            to: Math.min(currentPage * maxResultCount, total),
                        })
                    }}
                </span>
                <div class="flex items-center gap-1">
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage <= 1 || loading"
                        @click="goToPage(1)"
                    >
                        «
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage <= 1 || loading"
                        @click="goToPage(currentPage - 1)"
                    >
                        ‹
                    </Button>
                    <span class="px-3 text-muted-foreground">{{ currentPage }} / {{ totalPages }}</span>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage >= totalPages || loading"
                        @click="goToPage(currentPage + 1)"
                    >
                        ›
                    </Button>
                    <Button
                        severity="secondary"
                        outlined
                        size="small"
                        :disabled="currentPage >= totalPages || loading"
                        @click="goToPage(totalPages)"
                    >
                        »
                    </Button>
                </div>
            </div>
        </AppCard>
    </div>

    <!-- ===================== 全局确认对话框（删除 / 清理） ===================== -->
    <ConfirmDialog />

    <!-- ===================== 上传对话框 ===================== -->
    <Dialog
        v-model:visible="showUpload"
        modal
        :header="t('productModel.uploadTitle')"
        :style="{ width: '720px', maxWidth: '90vw' }"
    >
        <!-- 拖拽区域 -->
        <div
            :class="[
                'flex flex-col items-center justify-center rounded-lg border-2 border-dashed p-8 transition-colors cursor-pointer',
                isDragging ? 'border-primary bg-primary/5' : 'border-muted-foreground/30',
            ]"
            @dragover.prevent="isDragging = true"
            @dragleave.prevent="isDragging = false"
            @drop.prevent="handleDrop"
            @click="($refs.fileInputRef as HTMLInputElement)?.click()"
        >
            <Upload class="mb-2 size-8 text-muted-foreground" />
            <p class="text-sm text-muted-foreground">
                {{ t('productModel.dropZoneText') }}
                <span class="text-primary underline">{{ t('productModel.dropZoneClick') }}</span>
            </p>
            <p class="mt-1 text-xs text-muted-foreground">{{ t('productModel.supportedFormats') }}</p>
            <input
                ref="fileInputRef"
                type="file"
                class="hidden"
                multiple
                accept=".ply,.obj,.step,.stp,.iges,.igs,.stl,.glb,.gltf,.pcd"
                @change="handleFileInput"
            />
        </div>

        <!-- 上传队列 -->
        <div v-if="uploadQueue.length > 0" class="mt-2 max-h-64 space-y-2 overflow-y-auto">
            <div v-for="qItem in uploadQueue" :key="qItem.id" class="rounded border p-2">
                <div class="flex items-center justify-between gap-2">
                    <div class="min-w-0 flex-1">
                        <p class="truncate text-sm font-medium">{{ qItem.file.name }}</p>
                        <p class="text-xs text-muted-foreground">
                            {{ formatFileSize(qItem.file.size) }}
                            <span v-if="qItem.state === 'error'" class="ml-1 text-destructive">
                                {{ qItem.errorMessage }}
                            </span>
                            <span
                                v-else-if="qItem.state === 'done' && qItem.conversionState === 'none'"
                                class="ml-1 text-green-500"
                            >
                                {{ t('productModel.uploadDone') }}
                            </span>
                            <span v-else-if="qItem.state === 'cancelled'" class="ml-1 text-muted-foreground">
                                {{ t('productModel.uploadCancelled') }}
                            </span>
                        </p>
                        <Progress
                            v-if="qItem.state === 'uploading'"
                            :value="qItem.progress"
                            :show-value="false"
                            class="mt-1 h-1"
                        />
                    </div>
                    <!-- 转换状态图标（上传完成后显示） -->
                    <div
                        v-if="qItem.state === 'done' && qItem.conversionState !== 'none'"
                        class="shrink-0 flex items-center"
                        :title="
                            qItem.conversionState === 'waiting'
                                ? t('productModel.conversionWaiting')
                                : qItem.conversionState === 'converting'
                                  ? t('productModel.conversionConverting')
                                  : qItem.conversionState === 'success'
                                    ? t('productModel.conversionSuccess')
                                    : t('productModel.conversionFailed')
                        "
                    >
                        <!-- 等待转换：时钟图标 -->
                        <Clock v-if="qItem.conversionState === 'waiting'" class="size-4 text-muted-foreground" />
                        <!-- 转换中：转圈图标 -->
                        <Loader2
                            v-else-if="qItem.conversionState === 'converting'"
                            class="size-4 animate-spin text-primary"
                        />
                        <!-- 转换成功：勾号 -->
                        <CheckCircle v-else-if="qItem.conversionState === 'success'" class="size-4 text-green-500" />
                        <!-- 转换失败：叉号 -->
                        <XCircle v-else-if="qItem.conversionState === 'failed'" class="size-4 text-destructive" />
                    </div>
                    <Button
                        v-if="qItem.state === 'uploading'"
                        text
                        severity="secondary"
                        class="shrink-0"
                        :title="t('productModel.cancelUpload')"
                        @click="cancelUpload(qItem)"
                    >
                        <X class="size-4" />
                    </Button>
                    <Button
                        v-else-if="
                            qItem.state !== 'done' ||
                            qItem.conversionState === 'none' ||
                            qItem.conversionState === 'success' ||
                            qItem.conversionState === 'failed'
                        "
                        text
                        severity="secondary"
                        class="shrink-0"
                        :title="t('productModel.remove')"
                        @click="removeQueueItem(qItem.id)"
                    >
                        <X class="size-4" />
                    </Button>
                </div>
            </div>
        </div>

        <template #footer>
            <Button severity="secondary" outlined size="small" @click="showUpload = false">
                {{ t('productModel.close') }}
            </Button>
            <Button
                size="small"
                :disabled="uploadQueue.filter((i) => i.state === 'idle').length === 0"
                @click="startUploadAll"
            >
                {{ t('productModel.startUpload') }}
            </Button>
        </template>
    </Dialog>

    <!-- ===================== 重命名对话框 ===================== -->
    <Dialog
        v-model:visible="showRename"
        modal
        :header="t('productModel.renameTitle')"
        :style="{ width: '420px', maxWidth: '90vw' }"
    >
        <div class="flex flex-col gap-2 p-3">
            <label class="text-sm">{{ t('productModel.renameLabel') }}</label>
            <InputText
                v-model="renameName"
                :placeholder="t('productModel.renamePlaceholder')"
                maxlength="256"
                size="small"
                class="!text-xs w-full"
                @keydown.enter="submitRename"
            />
        </div>
        <template #footer>
            <Button severity="secondary" outlined size="small" @click="showRename = false">
                {{ t('common.cancel') }}
            </Button>
            <Button size="small" :disabled="renaming || !renameName.trim()" @click="submitRename">
                <RefreshCw v-if="renaming" class="mr-1 size-4 animate-spin" />
                {{ t('common.confirm') }}
            </Button>
        </template>
    </Dialog>

    <!-- ===================== PLY 预览对话框 ===================== -->
    <Dialog
        v-model:visible="showPreview"
        modal
        :header="`${t('productModel.previewTitle')}${previewTarget?.name ?? ''}`"
        :style="{ width: '960px', maxWidth: '95vw' }"
    >
        <div class="relative h-[500px] w-full overflow-hidden rounded-md bg-[#1a1a1a]">
            <div v-if="previewLoading" class="absolute inset-0 flex items-center justify-center text-white">
                {{ t('productModel.previewLoading') }}
            </div>
            <div v-if="previewError" class="absolute inset-0 flex items-center justify-center text-destructive">
                {{ previewError }}
            </div>
            <canvas ref="previewCanvasRef" class="size-full" />
        </div>
        <template #footer>
            <Button severity="secondary" outlined size="small" @click="showPreview = false">
                {{ t('productModel.close') }}
            </Button>
        </template>
    </Dialog>
</template>
