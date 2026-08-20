<script setup lang="ts">
import SparkMD5 from 'spark-md5'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import * as signalR from '@microsoft/signalr'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import Column from 'primevue/column'
import ConfirmDialog from 'primevue/confirmdialog'
import DataTable from 'primevue/datatable'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import ProgressBar from 'primevue/progressbar'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { Eraser, RefreshCw, Search, Upload, X } from '@lucide/vue'
import { AppCard } from '@/components/primevue'
import { useAuthStore } from '@/stores/auth'
import { useAppToast } from '@/composables/useAppToast'
import {
    AiModelConversionPreference,
    type AiModelConversionStateDto,
    AiModelFileConversionStatus,
    AiModelFileRole,
    AiModelLoadStatus,
    AiModelResolvedConversionType,
    abortAiModelUploadAsync,
    checkAiModelFileMd5Async,
    cleanUpOrphanedRecordsAsync,
    completeAiModelUploadAsync,
    deleteAiModelConvertedFileAsync,
    deleteAiModelAsync,
    findAiModelUploadSessionAsync,
    getAiModelIdentifierLookupAsync,
    getAiModelListAsync,
    getAiModelUploadSessionStatusAsync,
    getAiRuntimePlatformInfoAsync,
    initializeAiModelUploadAsync,
    startAiModelConversionAsync,
    type AiModelDto,
    type AiModelFileDto,
    type AiModelFileMd5CheckResultDto,
    type AiModelUploadSessionStatusDto,
    type AiRuntimePlatformInfoDto,
    type GetAiModelListInput,
    updateAiModelAsync,
    uploadAiModelChunkAsync,
} from '@/api/ai-models'

const AI_UPLOAD_CHUNK_SIZE = 16 * 1024 * 1024
const AI_UPLOAD_SESSION_STORAGE_KEY = 'aurora.ai-model.upload-session.v2'
const MAX_MODEL_FILE_COUNT = 2

interface AiModelUploadSessionCacheItem {
    readonly sessionId: string
    readonly originalFileName: string
    readonly fileSizeBytes: number
    readonly md5: string
    readonly chunkSizeBytes: number
    readonly totalChunks: number
}

interface UploadFileState {
    readonly localId: string
    readonly file: File
    md5: string
    checkingMd5: boolean
    md5CheckResult: AiModelFileMd5CheckResultDto | null
    fileRole: AiModelFileRole
    sortOrder: number
}

interface EditFileState {
    readonly id: string
    readonly originalFileName: string
    readonly displayName: string
    readonly fileFormat: string
    readonly fileSizeBytes: number
    readonly md5: string
    readonly md5Verified: boolean
    fileRole: AiModelFileRole
    sortOrder: number
}

const { t } = useI18n()
const confirm = useConfirm()
const authStore = useAuthStore()
const toast = useAppToast()

const loading = ref(false)
const items = ref<AiModelDto[]>([])
const total = ref(0)
const skipCount = ref(0)
const maxResultCount = ref(20)
const filterText = ref('')
const expandedRows = ref<Record<string, boolean>>({})

const uploadVisible = ref(false)
const editVisible = ref(false)
const isDragging = ref(false)
const fileInputRef = ref<HTMLInputElement | null>(null)
const uploadName = ref('')
const uploadDescription = ref('')
const uploadIdentifierNames = ref<string[]>([])
const uploadIdentifierInput = ref('')
const uploadLocationKey = ref('')
const uploadGenerationCondition = ref('')
const uploadConversionPreference = ref(AiModelConversionPreference.Auto)
const uploadFiles = ref<UploadFileState[]>([])
const uploadProgress = ref(0)
const uploading = ref(false)
const uploadAbortController = ref<AbortController | null>(null)
const cleaningUp = ref(false)
const startingConversionModelIds = ref<string[]>([])
const activeConversionStateMap = ref<Record<string, AiModelConversionStateDto>>({})
const deletingConvertedFileIds = ref<string[]>([])
const conversionHubConnected = ref(false)
const conversionListRefreshTimer = ref<number | null>(null)

const editing = ref(false)
const editingModelId = ref<string | null>(null)
const editName = ref('')
const editDescription = ref('')
const editIdentifierNames = ref<string[]>([])
const editIdentifierInput = ref('')
const editVersion = ref('')
const editLocationKey = ref('')
const editGenerationCondition = ref('')
const editConversionPreference = ref(AiModelConversionPreference.Auto)
const editFiles = ref<EditFileState[]>([])

const runtimePlatformInfo = ref<AiRuntimePlatformInfoDto | null>(null)
const loadingRuntimeInfo = ref(false)
const uploadIdentifierSuggestions = ref<string[]>([])
const editIdentifierSuggestions = ref<string[]>([])

let conversionHub: signalR.HubConnection | null = null

const currentPage = computed(() => Math.floor(skipCount.value / maxResultCount.value) + 1)
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / maxResultCount.value)))
const convertingModelIds = computed(() => {
    return Array.from(new Set([...startingConversionModelIds.value, ...Object.keys(activeConversionStateMap.value)]))
})
const supportedExtensionsText = computed(() => {
    if (!runtimePlatformInfo.value?.supportedExtensions.length) {
        return '—'
    }

    return runtimePlatformInfo.value.supportedExtensions.map((item: string) => `.${item}`).join(', ')
})
const uploadAccept = computed(() => {
    return runtimePlatformInfo.value?.supportedExtensions.map((item: string) => `.${item}`).join(',') ?? ''
})
const uploadDuplicateExists = computed(() => uploadFiles.value.some((item) => item.md5CheckResult?.exists))
const uploadCheckingMd5 = computed(() => uploadFiles.value.some((item) => item.checkingMd5))
const uploadHasPendingFingerprint = computed(() => uploadFiles.value.some((item) => !item.md5))
const uploadAllOnnx = computed(() => {
    return (
        uploadFiles.value.length > 0 && uploadFiles.value.every((item) => getFileExtension(item.file.name) === 'onnx')
    )
})
const showUploadConversionPreference = computed(() => {
    return !!runtimePlatformInfo.value?.supportsOnnxConversion && uploadAllOnnx.value
})
const showEditConversionPreference = computed(() => {
    return (
        !!runtimePlatformInfo.value?.supportsOnnxConversion &&
        editFiles.value.length > 0 &&
        editFiles.value.every((item) => item.fileFormat.toLowerCase() === 'onnx')
    )
})
const uploadFileRoleError = computed(() => validateFileRoles(uploadFiles.value, false))
const editFileRoleError = computed(() => validateFileRoles(editFiles.value, true))
const canSubmitUpload = computed(() => {
    return (
        !!uploadName.value.trim() &&
        uploadFiles.value.length > 0 &&
        !uploadDuplicateExists.value &&
        !uploadCheckingMd5.value &&
        !uploadHasPendingFingerprint.value &&
        !uploadFileRoleError.value &&
        !uploading.value &&
        !!runtimePlatformInfo.value?.isSupported
    )
})
const canSubmitEdit = computed(() => {
    return !!editingModelId.value && !!editName.value.trim() && !editing.value && !editFileRoleError.value
})

function getConversionPreferenceOptions(): Array<{ label: string; value: AiModelConversionPreference }> {
    return [
        { label: t('aiModel.conversionPreferenceAuto'), value: AiModelConversionPreference.Auto },
        { label: t('aiModel.conversionPreferenceDirectOnnx'), value: AiModelConversionPreference.DirectOnnx },
        { label: t('aiModel.conversionPreferenceToRknn'), value: AiModelConversionPreference.ToRknn },
    ]
}

function getFileRoleOptions(fileCount: number): Array<{ label: string; value: AiModelFileRole }> {
    if (fileCount <= 1) {
        return [{ label: t('aiModel.fileRoleSingleWholeModel'), value: AiModelFileRole.SingleWholeModel }]
    }

    return [
        { label: t('aiModel.fileRoleSplitEncoder'), value: AiModelFileRole.SplitEncoder },
        { label: t('aiModel.fileRoleSplitDecoder'), value: AiModelFileRole.SplitDecoder },
    ]
}

function loadStatusLabel(status: AiModelLoadStatus): string {
    switch (status) {
        case AiModelLoadStatus.Unloaded:
            return t('aiModel.loadStatusUnloaded')
        case AiModelLoadStatus.Loading:
            return t('aiModel.loadStatusLoading')
        case AiModelLoadStatus.Loaded:
            return t('aiModel.loadStatusLoaded')
        case AiModelLoadStatus.Failed:
            return t('aiModel.loadStatusFailed')
        default:
            return `#${status}`
    }
}

function loadStatusSeverity(status: AiModelLoadStatus): 'secondary' | 'success' | 'danger' | 'info' {
    switch (status) {
        case AiModelLoadStatus.Loaded:
            return 'success'
        case AiModelLoadStatus.Failed:
            return 'danger'
        case AiModelLoadStatus.Loading:
            return 'info'
        default:
            return 'secondary'
    }
}

function conversionPreferenceLabel(value: AiModelConversionPreference): string {
    switch (value) {
        case AiModelConversionPreference.DirectOnnx:
            return t('aiModel.conversionPreferenceDirectOnnx')
        case AiModelConversionPreference.ToRknn:
            return t('aiModel.conversionPreferenceToRknn')
        case AiModelConversionPreference.ToRkllm:
            return t('aiModel.conversionPreferenceToRkllm')
        default:
            return t('aiModel.conversionPreferenceAuto')
    }
}

function resolvedConversionTypeLabel(value: AiModelResolvedConversionType): string {
    switch (value) {
        case AiModelResolvedConversionType.DirectOnnx:
            return t('aiModel.resolvedConversionDirectOnnx')
        case AiModelResolvedConversionType.ToRknn:
            return t('aiModel.resolvedConversionToRknn')
        case AiModelResolvedConversionType.ToRkllm:
            return t('aiModel.resolvedConversionToRkllm')
        default:
            return t('aiModel.resolvedConversionUnknown')
    }
}

function resolvedConversionTypeText(model: AiModelDto): string {
    return model.conversionPreference === AiModelConversionPreference.Auto
        ? resolvedConversionTypeLabel(model.resolvedConversionType)
        : '—'
}

function fileRoleLabel(value: AiModelFileRole): string {
    switch (value) {
        case AiModelFileRole.SplitEncoder:
            return t('aiModel.fileRoleSplitEncoder')
        case AiModelFileRole.SplitDecoder:
            return t('aiModel.fileRoleSplitDecoder')
        default:
            return t('aiModel.fileRoleSingleWholeModel')
    }
}

function conversionStatusLabel(value: AiModelFileConversionStatus): string {
    switch (value) {
        case AiModelFileConversionStatus.Pending:
            return t('aiModel.conversionStatusPending')
        case AiModelFileConversionStatus.Converting:
            return t('aiModel.conversionStatusConverting')
        case AiModelFileConversionStatus.Completed:
            return t('aiModel.conversionStatusCompleted')
        case AiModelFileConversionStatus.Failed:
            return t('aiModel.conversionStatusFailed')
        default:
            return t('aiModel.conversionStatusNone')
    }
}

function conversionStatusSeverity(
    value: AiModelFileConversionStatus
): 'secondary' | 'success' | 'danger' | 'warn' | 'info' {
    switch (value) {
        case AiModelFileConversionStatus.Completed:
            return 'success'
        case AiModelFileConversionStatus.Failed:
            return 'danger'
        case AiModelFileConversionStatus.Converting:
            return 'info'
        case AiModelFileConversionStatus.Pending:
            return 'warn'
        default:
            return 'secondary'
    }
}

function formatFileSize(bytes: number | null | undefined): string {
    if (!bytes || bytes <= 0) return '—'
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
    return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}

function formatDateTime(value: string | null | undefined): string {
    if (!value) {
        return '—'
    }

    return new Date(value).toLocaleString()
}

function normalizeIdentifierNames(values: string[]): string[] {
    const seen = new Set<string>()
    const result: string[] = []

    values.forEach((value) => {
        const normalized = value.trim()
        const key = normalized.toLocaleLowerCase()
        if (!normalized || seen.has(key)) {
            return
        }

        seen.add(key)
        result.push(normalized)
    })

    return result
}

function applyNormalizedIdentifiers(): void {
    uploadIdentifierNames.value = normalizeIdentifierNames(uploadIdentifierNames.value)
}

function parseIdentifierInput(value: string, finalizeLast: boolean): { committedValues: string[]; remainder: string } {
    const normalized = value.replace(/[；\n\r]+/g, ';')
    const parts = normalized.split(';')
    const hasTrailingSeparator = normalized.endsWith(';')

    if (parts.length === 1 && !hasTrailingSeparator) {
        return {
            committedValues: finalizeLast ? [parts[0]] : [],
            remainder: finalizeLast ? '' : parts[0],
        }
    }

    const remainder = finalizeLast || hasTrailingSeparator ? '' : (parts.pop() ?? '')
    return {
        committedValues: parts,
        remainder,
    }
}

function appendIdentifierValues(target: typeof uploadIdentifierNames, values: string[]): void {
    target.value = normalizeIdentifierNames([...target.value, ...values])
}

function commitIdentifierInput(
    target: typeof uploadIdentifierNames,
    input: typeof uploadIdentifierInput,
    finalizeLast: boolean
): void {
    const { committedValues, remainder } = parseIdentifierInput(input.value, finalizeLast)

    if (committedValues.length > 0) {
        appendIdentifierValues(target, committedValues)
    }

    input.value = finalizeLast ? '' : remainder.trimStart()
}

async function requestIdentifierSuggestions(
    query: string,
    target: typeof uploadIdentifierNames,
    suggestions: typeof uploadIdentifierSuggestions
): Promise<void> {
    const filter = query.trim()
    if (!filter) {
        suggestions.value = []
        return
    }

    try {
        const result = await getAiModelIdentifierLookupAsync(filter)
        const selected = new Set(target.value.map((item: string) => item.toLocaleLowerCase()))
        suggestions.value = result.map((item) => item.name).filter((item) => !selected.has(item.toLocaleLowerCase()))
    } catch {
        suggestions.value = []
    }
}

async function handleIdentifierInputChanged(
    target: typeof uploadIdentifierNames,
    input: typeof uploadIdentifierInput,
    suggestions: typeof uploadIdentifierSuggestions
): Promise<void> {
    commitIdentifierInput(target, input, false)
    await requestIdentifierSuggestions(input.value, target, suggestions)
}

function handleIdentifierConfirm(
    target: typeof uploadIdentifierNames,
    input: typeof uploadIdentifierInput,
    suggestions: typeof uploadIdentifierSuggestions
): void {
    commitIdentifierInput(target, input, true)
    suggestions.value = []
}

function handleIdentifierBlur(
    target: typeof uploadIdentifierNames,
    input: typeof uploadIdentifierInput,
    suggestions: typeof uploadIdentifierSuggestions
): void {
    window.setTimeout(() => {
        handleIdentifierConfirm(target, input, suggestions)
    }, 0)
}

function removeIdentifierName(target: typeof uploadIdentifierNames, identifierName: string): void {
    target.value = target.value.filter((item) => item !== identifierName)
}

function selectIdentifierSuggestion(
    target: typeof uploadIdentifierNames,
    input: typeof uploadIdentifierInput,
    suggestions: typeof uploadIdentifierSuggestions,
    identifierName: string
): void {
    appendIdentifierValues(target, [identifierName])
    input.value = ''
    suggestions.value = []
}

async function handleUploadIdentifierInputChanged(): Promise<void> {
    await handleIdentifierInputChanged(uploadIdentifierNames, uploadIdentifierInput, uploadIdentifierSuggestions)
}

function handleUploadIdentifierConfirm(): void {
    handleIdentifierConfirm(uploadIdentifierNames, uploadIdentifierInput, uploadIdentifierSuggestions)
}

function handleUploadIdentifierBlur(): void {
    handleIdentifierBlur(uploadIdentifierNames, uploadIdentifierInput, uploadIdentifierSuggestions)
}

function removeUploadIdentifierName(identifierName: string): void {
    removeIdentifierName(uploadIdentifierNames, identifierName)
}

function selectUploadIdentifierSuggestion(identifierName: string): void {
    selectIdentifierSuggestion(
        uploadIdentifierNames,
        uploadIdentifierInput,
        uploadIdentifierSuggestions,
        identifierName
    )
}

async function handleEditIdentifierInputChanged(): Promise<void> {
    await handleIdentifierInputChanged(editIdentifierNames, editIdentifierInput, editIdentifierSuggestions)
}

function handleEditIdentifierConfirm(): void {
    handleIdentifierConfirm(editIdentifierNames, editIdentifierInput, editIdentifierSuggestions)
}

function handleEditIdentifierBlur(): void {
    handleIdentifierBlur(editIdentifierNames, editIdentifierInput, editIdentifierSuggestions)
}

function removeEditIdentifierName(identifierName: string): void {
    removeIdentifierName(editIdentifierNames, identifierName)
}

function selectEditIdentifierSuggestion(identifierName: string): void {
    selectIdentifierSuggestion(editIdentifierNames, editIdentifierInput, editIdentifierSuggestions, identifierName)
}

async function loadRuntimePlatformInfo(force = false): Promise<void> {
    if (runtimePlatformInfo.value && !force) {
        return
    }

    loadingRuntimeInfo.value = true
    try {
        runtimePlatformInfo.value = await getAiRuntimePlatformInfoAsync()
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('aiModel.platformInfoLoadFailed'))
    } finally {
        loadingRuntimeInfo.value = false
    }
}

async function loadList(): Promise<void> {
    loading.value = true
    try {
        const input: GetAiModelListInput = {
            filter: filterText.value || null,
            sorting: 'CreationTime DESC',
            skipCount: skipCount.value,
            maxResultCount: maxResultCount.value,
        }
        const result = await getAiModelListAsync(input)
        const expandedRowKeys = new Set(Object.keys(expandedRows.value))
        items.value = result.items
        applyActiveStatesToLoadedItems()
        total.value = result.totalCount
        expandedRows.value = result.items.reduce<Record<string, boolean>>((accumulator, item) => {
            if (expandedRowKeys.has(item.id)) {
                accumulator[item.id] = true
            }
            return accumulator
        }, {})
    } catch (error) {
        throw error
    } finally {
        loading.value = false
    }
}

function clearConversionListRefreshTimer(): void {
    if (conversionListRefreshTimer.value != null) {
        window.clearTimeout(conversionListRefreshTimer.value)
        conversionListRefreshTimer.value = null
    }
}

function scheduleConversionListRefresh(): void {
    if (conversionListRefreshTimer.value != null) {
        return
    }

    conversionListRefreshTimer.value = window.setTimeout(() => {
        conversionListRefreshTimer.value = null
        void loadList().catch((error: unknown) => {
            toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
        })
    }, 150)
}

function stopConversionHub(): void {
    conversionHubConnected.value = false

    if (conversionHub) {
        conversionHub.stop().catch(() => {})
        conversionHub = null
    }
}

function normalizeConversionState(state: AiModelConversionStateDto): AiModelConversionStateDto {
    return {
        ...state,
        conversionErrorMessage: state.conversionErrorMessage ?? null,
    }
}

function removeStartingConversionModelId(modelId: string): void {
    startingConversionModelIds.value = startingConversionModelIds.value.filter((value) => value !== modelId)
}

function upsertActiveConversionState(state: AiModelConversionStateDto): void {
    const normalizedState = normalizeConversionState(state)
    activeConversionStateMap.value = {
        ...activeConversionStateMap.value,
        [normalizedState.modelId]: normalizedState,
    }
    applyConversionStateToItems(normalizedState)
}

function removeActiveConversionState(modelId: string): void {
    if (!(modelId in activeConversionStateMap.value)) {
        return
    }

    const nextStateMap = { ...activeConversionStateMap.value }
    delete nextStateMap[modelId]
    activeConversionStateMap.value = nextStateMap
}

function applyActiveConversionsSnapshot(states: AiModelConversionStateDto[]): void {
    const normalizedStates = states.map(normalizeConversionState)
    const hadActiveConversions = Object.keys(activeConversionStateMap.value).length > 0
    activeConversionStateMap.value = normalizedStates.reduce<Record<string, AiModelConversionStateDto>>(
        (accumulator, state) => {
            accumulator[state.modelId] = state
            return accumulator
        },
        {}
    )
    applyActiveStatesToLoadedItems()

    if (hadActiveConversions && normalizedStates.length === 0) {
        scheduleConversionListRefresh()
    }
}

function applyActiveStatesToLoadedItems(): void {
    for (const state of Object.values(activeConversionStateMap.value)) {
        applyConversionStateToItems(state)
    }
}

function applyConversionStateToItems(state: AiModelConversionStateDto): void {
    items.value = items.value.map((item) => {
        if (item.id !== state.modelId) {
            return item
        }

        return {
            ...item,
            files: item.files.map((file) => {
                if (!state.sourceFileIds.includes(file.id)) {
                    return file
                }

                return {
                    ...file,
                    conversionTargetType: state.targetType ?? file.conversionTargetType ?? null,
                    conversionStatus: state.status,
                    conversionErrorMessage: state.conversionErrorMessage,
                    conversionTime: state.lastUpdatedTime ?? file.conversionTime ?? null,
                }
            }),
        }
    })
}

async function startConversionHub(): Promise<void> {
    stopConversionHub()

    conversionHub = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/ai-model-conversion', {
            accessTokenFactory: () => authStore.token ?? '',
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build()

    const handleActiveConversionsSnapshot = (states: AiModelConversionStateDto[]) => {
        applyActiveConversionsSnapshot(states)
    }

    const handleConversionQueued = (state: AiModelConversionStateDto) => {
        removeStartingConversionModelId(state.modelId)
        upsertActiveConversionState(state)
    }

    const handleConversionStarted = (state: AiModelConversionStateDto) => {
        removeStartingConversionModelId(state.modelId)
        upsertActiveConversionState(state)
    }

    const handleConversionFinished = (state: AiModelConversionStateDto) => {
        removeStartingConversionModelId(state.modelId)
        removeActiveConversionState(state.modelId)
        applyConversionStateToItems(normalizeConversionState(state))
        scheduleConversionListRefresh()
    }

    conversionHub.on('ReceiveActiveConversionsSnapshot', handleActiveConversionsSnapshot)
    conversionHub.on('ReceiveActiveConversionsSnapshotAsync', handleActiveConversionsSnapshot)

    conversionHub.on('ReceiveConversionQueued', handleConversionQueued)
    conversionHub.on('ReceiveConversionQueuedAsync', handleConversionQueued)

    conversionHub.on('ReceiveConversionStarted', handleConversionStarted)
    conversionHub.on('ReceiveConversionStartedAsync', handleConversionStarted)

    conversionHub.on('ReceiveConversionFinished', handleConversionFinished)
    conversionHub.on('ReceiveConversionFinishedAsync', handleConversionFinished)

    conversionHub.onreconnecting(() => {
        conversionHubConnected.value = false
    })

    conversionHub.onreconnected(() => {
        conversionHubConnected.value = true
    })

    conversionHub.onclose(() => {
        conversionHubConnected.value = false
    })

    try {
        await conversionHub.start()
        conversionHubConnected.value = true
    } catch {
        conversionHubConnected.value = false
    }
}

function handleSearch(): void {
    skipCount.value = 0
    void loadList()
}

function handleReset(): void {
    filterText.value = ''
    skipCount.value = 0
    void loadList()
}

function goToPage(page: number): void {
    skipCount.value = (page - 1) * maxResultCount.value
    void loadList()
}

function resetUploadState(): void {
    uploadName.value = ''
    uploadDescription.value = ''
    uploadIdentifierNames.value = []
    uploadIdentifierInput.value = ''
    uploadLocationKey.value = ''
    uploadGenerationCondition.value = ''
    uploadConversionPreference.value = AiModelConversionPreference.Auto
    uploadFiles.value = []
    uploadProgress.value = 0
    uploadIdentifierSuggestions.value = []
    uploadAbortController.value = null
    isDragging.value = false
    if (fileInputRef.value) {
        fileInputRef.value.value = ''
    }
}

function resetEditState(): void {
    editingModelId.value = null
    editName.value = ''
    editDescription.value = ''
    editIdentifierNames.value = []
    editIdentifierInput.value = ''
    editVersion.value = ''
    editLocationKey.value = ''
    editGenerationCondition.value = ''
    editConversionPreference.value = AiModelConversionPreference.Auto
    editFiles.value = []
    editIdentifierSuggestions.value = []
}

function getUploadSessionFingerprint(fileName: string, fileSizeBytes: number, md5: string): string {
    return `${fileName}::${fileSizeBytes}::${md5.trim().toLowerCase()}`
}

function readUploadSessionCache(): Record<string, AiModelUploadSessionCacheItem> {
    if (typeof window === 'undefined') {
        return {}
    }

    try {
        const raw = window.localStorage.getItem(AI_UPLOAD_SESSION_STORAGE_KEY)
        if (!raw) {
            return {}
        }

        return JSON.parse(raw) as Record<string, AiModelUploadSessionCacheItem>
    } catch {
        return {}
    }
}

function writeUploadSessionCache(cache: Record<string, AiModelUploadSessionCacheItem>): void {
    if (typeof window === 'undefined') {
        return
    }

    try {
        window.localStorage.setItem(AI_UPLOAD_SESSION_STORAGE_KEY, JSON.stringify(cache))
    } catch {
        // 忽略本地缓存写入失败，避免影响主上传流程
    }
}

function getCachedUploadSession(file: File, md5: string): AiModelUploadSessionCacheItem | null {
    const cache = readUploadSessionCache()
    const fingerprint = getUploadSessionFingerprint(file.name, file.size, md5)
    return cache[fingerprint] ?? null
}

function setCachedUploadSession(
    file: File,
    md5: string,
    session: Pick<AiModelUploadSessionCacheItem, 'sessionId' | 'chunkSizeBytes' | 'totalChunks'>
): void {
    const cache = readUploadSessionCache()
    const fingerprint = getUploadSessionFingerprint(file.name, file.size, md5)
    cache[fingerprint] = {
        sessionId: session.sessionId,
        originalFileName: file.name,
        fileSizeBytes: file.size,
        md5,
        chunkSizeBytes: session.chunkSizeBytes,
        totalChunks: session.totalChunks,
    }
    writeUploadSessionCache(cache)
}

function removeCachedUploadSession(file: File | null | undefined, md5: string | null | undefined): void {
    if (!file || !md5) {
        return
    }

    const cache = readUploadSessionCache()
    const fingerprint = getUploadSessionFingerprint(file.name, file.size, md5)
    if (!cache[fingerprint]) {
        return
    }

    delete cache[fingerprint]
    writeUploadSessionCache(cache)
}

function matchesUploadSession(
    status: AiModelUploadSessionStatusDto,
    file: File,
    md5: string,
    totalChunks: number
): boolean {
    return (
        status.originalFileName === file.name &&
        status.fileSizeBytes === file.size &&
        status.md5.toLowerCase() === md5.toLowerCase() &&
        status.chunkSizeBytes === AI_UPLOAD_CHUNK_SIZE &&
        status.totalChunks === totalChunks
    )
}

function notifyResumeAvailable(status: AiModelUploadSessionStatusDto, file: File): void {
    if (status.uploadedChunks <= 0 || status.isCompleted) {
        return
    }

    toast.info(
        t('aiModel.resumeUploadHint', {
            progress: Math.round((status.uploadedBytes * 100) / Math.max(file.size, 1)),
        })
    )
}

async function resolveUploadSession(
    file: File,
    md5: string,
    fileRole: AiModelFileRole,
    sortOrder: number
): Promise<AiModelUploadSessionStatusDto> {
    const totalChunks = Math.max(1, Math.ceil(file.size / AI_UPLOAD_CHUNK_SIZE))
    const cachedSession = getCachedUploadSession(file, md5)

    if (cachedSession) {
        try {
            const status = await getAiModelUploadSessionStatusAsync(cachedSession.sessionId)
            if (matchesUploadSession(status, file, md5, totalChunks)) {
                notifyResumeAvailable(status, file)
                return status
            }
        } catch {
            removeCachedUploadSession(file, md5)
        }
    }

    const existingSession = await findAiModelUploadSessionAsync({
        originalFileName: file.name,
        md5,
        fileSizeBytes: file.size,
        chunkSizeBytes: AI_UPLOAD_CHUNK_SIZE,
        totalChunks,
    })

    if (existingSession && matchesUploadSession(existingSession, file, md5, totalChunks)) {
        setCachedUploadSession(file, md5, {
            sessionId: existingSession.sessionId,
            chunkSizeBytes: existingSession.chunkSizeBytes,
            totalChunks: existingSession.totalChunks,
        })
        notifyResumeAvailable(existingSession, file)
        return existingSession
    }

    const initialized = await initializeAiModelUploadAsync({
        originalFileName: file.name,
        fileSizeBytes: file.size,
        chunkSizeBytes: AI_UPLOAD_CHUNK_SIZE,
        totalChunks,
        md5,
        fileRole,
        sortOrder,
    })

    setCachedUploadSession(file, md5, {
        sessionId: initialized.sessionId,
        chunkSizeBytes: initialized.chunkSizeBytes,
        totalChunks: initialized.totalChunks,
    })

    return {
        sessionId: initialized.sessionId,
        originalFileName: initialized.originalFileName,
        md5,
        fileSizeBytes: initialized.fileSizeBytes,
        chunkSizeBytes: initialized.chunkSizeBytes,
        totalChunks: initialized.totalChunks,
        uploadedChunks: initialized.uploadedChunks,
        uploadedBytes: initialized.uploadedBytes,
        nextChunkIndex: initialized.uploadedChunks,
        isCompleted: initialized.uploadedChunks >= initialized.totalChunks,
        lastUpdatedTime: new Date().toISOString(),
    }
}

async function openUpload(): Promise<void> {
    uploadVisible.value = true
    resetUploadState()
    await loadRuntimePlatformInfo()
}

function triggerFileSelect(): void {
    fileInputRef.value?.click()
}

function clearSelectedFiles(): void {
    uploadFiles.value = []
    uploadProgress.value = 0
    uploadConversionPreference.value = AiModelConversionPreference.Auto
    if (fileInputRef.value) {
        fileInputRef.value.value = ''
    }
}

function getFileExtension(fileName: string): string {
    const lastDotIndex = fileName.lastIndexOf('.')
    if (lastDotIndex < 0) {
        return ''
    }

    return fileName.slice(lastDotIndex + 1).toLowerCase()
}

function isFileExtensionSupported(file: File): boolean {
    const supportedExtensions = runtimePlatformInfo.value?.supportedExtensions
    if (!supportedExtensions?.length) {
        return false
    }

    const extension = getFileExtension(file.name)
    return !!extension && supportedExtensions.some((item: string) => item.toLowerCase() === extension)
}

function createUploadFileState(file: File, index: number): UploadFileState {
    return {
        localId: `${file.name}-${file.size}-${file.lastModified}-${index}`,
        file,
        md5: '',
        checkingMd5: true,
        md5CheckResult: null,
        fileRole: index === 0 ? AiModelFileRole.SingleWholeModel : AiModelFileRole.SplitDecoder,
        sortOrder: index,
    }
}

function initializeUploadFileRoles(): void {
    uploadFiles.value.forEach((item, index) => {
        item.sortOrder = index
    })

    if (uploadFiles.value.length <= 1) {
        uploadFiles.value.forEach((item) => {
            item.fileRole = AiModelFileRole.SingleWholeModel
        })
        return
    }

    const encoderCount = uploadFiles.value.filter((item) => item.fileRole === AiModelFileRole.SplitEncoder).length
    const decoderCount = uploadFiles.value.filter((item) => item.fileRole === AiModelFileRole.SplitDecoder).length
    const singleCount = uploadFiles.value.filter((item) => item.fileRole === AiModelFileRole.SingleWholeModel).length
    if (encoderCount === 1 && decoderCount === 1 && singleCount === 0) {
        return
    }

    uploadFiles.value[0].fileRole = AiModelFileRole.SplitEncoder
    uploadFiles.value[1].fileRole = AiModelFileRole.SplitDecoder
}

function refreshUploadFileSortOrders(): void {
    uploadFiles.value.forEach((item, index) => {
        item.sortOrder = index
    })
}

function refreshEditFileSortOrders(): void {
    editFiles.value.forEach((item, index) => {
        item.sortOrder = index
    })
}

function validateFileRoles(files: Array<{ fileRole: AiModelFileRole }>, allowEmpty: boolean): string | null {
    if (!files.length) {
        return allowEmpty ? null : t('aiModel.chooseFile')
    }

    if (files.length > MAX_MODEL_FILE_COUNT) {
        return t('aiModel.maxFilesExceeded', { count: MAX_MODEL_FILE_COUNT })
    }

    if (files.length === 1) {
        return files[0].fileRole === AiModelFileRole.SingleWholeModel ? null : t('aiModel.fileRoleInvalidSingle')
    }

    const encoderCount = files.filter((item) => item.fileRole === AiModelFileRole.SplitEncoder).length
    const decoderCount = files.filter((item) => item.fileRole === AiModelFileRole.SplitDecoder).length
    const singleCount = files.filter((item) => item.fileRole === AiModelFileRole.SingleWholeModel).length
    return encoderCount === 1 && decoderCount === 1 && singleCount === 0 ? null : t('aiModel.fileRoleInvalidSplit')
}

async function computeFileMd5(file: File): Promise<string> {
    const chunkSize = 4 * 1024 * 1024
    const spark = new SparkMD5.ArrayBuffer()

    for (let offset = 0; offset < file.size; offset += chunkSize) {
        const chunk = file.slice(offset, offset + chunkSize)
        const buffer = await chunk.arrayBuffer()
        spark.append(buffer)
    }

    return spark.end().toUpperCase()
}

function dedupeSelectedFiles(files: File[]): File[] {
    const seen = new Set<string>()
    const result: File[] = []

    files.forEach((file) => {
        const key = `${file.name}::${file.size}::${file.lastModified}`
        if (seen.has(key)) {
            return
        }

        seen.add(key)
        result.push(file)
    })

    return result
}

async function inspectSelectedFiles(selectedFiles: File[]): Promise<void> {
    const nextFiles = dedupeSelectedFiles(selectedFiles)
    if (!nextFiles.length) {
        return
    }

    if (nextFiles.length > MAX_MODEL_FILE_COUNT) {
        toast.warning(t('aiModel.maxFilesExceeded', { count: MAX_MODEL_FILE_COUNT }))
    }

    const files = nextFiles.slice(0, MAX_MODEL_FILE_COUNT)
    await loadRuntimePlatformInfo()
    if (!runtimePlatformInfo.value?.isSupported) {
        toast.warning(runtimePlatformInfo.value?.unsupportedReason || t('aiModel.runtimeUnsupported'))
        return
    }

    if (files.some((file) => !isFileExtensionSupported(file))) {
        toast.warning(t('aiModel.platformUnsupportedUpload', { extensions: supportedExtensionsText.value }))
        return
    }

    uploadFiles.value = files.map((file, index) => createUploadFileState(file, index))
    initializeUploadFileRoles()
    uploadProgress.value = 0
    if (!showUploadConversionPreference.value) {
        uploadConversionPreference.value = AiModelConversionPreference.Auto
    }

    try {
        for (const state of uploadFiles.value) {
            state.checkingMd5 = true
            const md5 = await computeFileMd5(state.file)
            state.md5 = md5
            state.md5CheckResult = await checkAiModelFileMd5Async({ md5 })

            if (state.md5CheckResult.exists) {
                toast.warning(
                    t('aiModel.md5DuplicateHint', {
                        name: state.md5CheckResult.existingModelName || '—',
                        fileName: state.md5CheckResult.existingOriginalFileName || '—',
                    })
                )
            }

            state.checkingMd5 = false
        }
    } catch (error) {
        clearSelectedFiles()
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        uploadFiles.value.forEach((item) => {
            item.checkingMd5 = false
        })
    }
}

function handleFileInput(event: Event): void {
    const input = event.target as HTMLInputElement
    const selectedFiles = Array.from(input.files ?? [])
    if (!selectedFiles.length) {
        return
    }

    void inspectSelectedFiles(selectedFiles)
}

function handleDrop(event: DragEvent): void {
    isDragging.value = false
    const droppedFiles = Array.from(event.dataTransfer?.files ?? [])
    if (!droppedFiles.length) {
        return
    }

    void inspectSelectedFiles(droppedFiles)
}

function removeSelectedUploadFile(localId: string): void {
    uploadFiles.value = uploadFiles.value.filter((item) => item.localId !== localId)
    if (uploadFiles.value.length <= 1) {
        initializeUploadFileRoles()
    } else {
        refreshUploadFileSortOrders()
    }
    if (!showUploadConversionPreference.value) {
        uploadConversionPreference.value = AiModelConversionPreference.Auto
    }
}

function handleUploadFileRoleChanged(): void {
    refreshUploadFileSortOrders()
}

function handleEditFileRoleChanged(): void {
    refreshEditFileSortOrders()
}

function updateOverallUploadProgress(uploadedBytes: number, totalBytes: number): void {
    uploadProgress.value = Math.min(100, Math.round((uploadedBytes * 100) / Math.max(totalBytes, 1)))
}

async function submitUpload(): Promise<void> {
    handleIdentifierConfirm(uploadIdentifierNames, uploadIdentifierInput, uploadIdentifierSuggestions)
    applyNormalizedIdentifiers()

    if (!uploadName.value.trim()) {
        toast.warning(t('aiModel.nameRequired'))
        return
    }

    if (!uploadFiles.value.length) {
        toast.warning(t('aiModel.chooseFile'))
        return
    }

    if (uploadFileRoleError.value) {
        toast.warning(uploadFileRoleError.value)
        return
    }

    if (uploadDuplicateExists.value) {
        toast.warning(t('aiModel.uploadBlockedDuplicate'))
        return
    }

    if (!runtimePlatformInfo.value?.isSupported) {
        toast.warning(runtimePlatformInfo.value?.unsupportedReason || t('aiModel.runtimeUnsupported'))
        return
    }

    const abortController = new AbortController()
    uploadAbortController.value = abortController
    uploading.value = true
    uploadProgress.value = 0

    const totalBytes = uploadFiles.value.reduce((sum, item) => sum + item.file.size, 0)
    const activeSessionIds = new Set<string>()
    const uploadedFiles = new Array<{
        sessionId: string
        originalFileName: string
        md5: string
        fileRole: AiModelFileRole
        sortOrder: number
    }>()

    try {
        let completedBytes = 0

        for (let index = 0; index < uploadFiles.value.length; index += 1) {
            const state = uploadFiles.value[index]
            const sessionStatus = await resolveUploadSession(state.file, state.md5, state.fileRole, index)
            activeSessionIds.add(sessionStatus.sessionId)

            let uploadedBytesForCurrentFile = sessionStatus.uploadedBytes
            updateOverallUploadProgress(completedBytes + uploadedBytesForCurrentFile, totalBytes)

            for (
                let chunkIndex = sessionStatus.nextChunkIndex;
                chunkIndex < sessionStatus.totalChunks;
                chunkIndex += 1
            ) {
                const start = chunkIndex * AI_UPLOAD_CHUNK_SIZE
                const end = Math.min(start + AI_UPLOAD_CHUNK_SIZE, state.file.size)
                const chunk = state.file.slice(start, end)

                const result = await uploadAiModelChunkAsync(
                    sessionStatus.sessionId,
                    chunkIndex,
                    chunk,
                    (loaded, totalChunkBytes) => {
                        const currentUploadedBytes = Math.min(
                            uploadedBytesForCurrentFile + Math.min(loaded, totalChunkBytes),
                            state.file.size
                        )
                        updateOverallUploadProgress(completedBytes + currentUploadedBytes, totalBytes)
                    },
                    abortController.signal
                )

                uploadedBytesForCurrentFile = result.uploadedBytes
                updateOverallUploadProgress(completedBytes + uploadedBytesForCurrentFile, totalBytes)
            }

            completedBytes += state.file.size
            updateOverallUploadProgress(completedBytes, totalBytes)
            uploadedFiles.push({
                sessionId: sessionStatus.sessionId,
                originalFileName: state.file.name,
                md5: state.md5,
                fileRole: state.fileRole,
                sortOrder: index,
            })
        }

        await completeAiModelUploadAsync({
            name: uploadName.value.trim(),
            description: uploadDescription.value || null,
            conversionPreference: showUploadConversionPreference.value
                ? uploadConversionPreference.value
                : AiModelConversionPreference.Auto,
            resolvedConversionType: AiModelResolvedConversionType.Unknown,
            identifierNames: uploadIdentifierNames.value,
            locationKey: uploadLocationKey.value || null,
            generationCondition: uploadGenerationCondition.value || null,
            files: uploadedFiles,
        })

        uploadFiles.value.forEach((item) => {
            removeCachedUploadSession(item.file, item.md5)
        })
        updateOverallUploadProgress(totalBytes, totalBytes)

        toast.success(t('aiModel.uploadSuccess'))
        uploadVisible.value = false
        resetUploadState()
        await loadList()
    } catch (error) {
        if (abortController.signal.aborted) {
            await Promise.all(
                Array.from(activeSessionIds).map((sessionId) =>
                    abortAiModelUploadAsync(sessionId).catch(() => undefined)
                )
            )
            uploadFiles.value.forEach((item) => {
                removeCachedUploadSession(item.file, item.md5)
            })
            toast.warning(t('common.cancel'))
            return
        }

        toast.warning(t('aiModel.uploadInterruptedResumeReady'))
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        uploadAbortController.value = null
        uploading.value = false
    }
}

function cancelUpload(): void {
    uploadAbortController.value?.abort()
}

function getSortedFiles(files: AiModelFileDto[]): AiModelFileDto[] {
    return [...files].sort((left, right) => left.sortOrder - right.sortOrder)
}

function getOriginalFiles(files: AiModelFileDto[]): AiModelFileDto[] {
    return getSortedFiles(files).filter((file) => file.isOriginalFile && !file.isConvertedFile)
}

function getConvertedFiles(files: AiModelFileDto[]): AiModelFileDto[] {
    return getSortedFiles(files).filter((file) => file.isConvertedFile)
}

function getModelCurrentConversionStatus(item: AiModelDto): AiModelFileConversionStatus | null {
    const activeState = activeConversionStateMap.value[item.id]
    if (activeState) {
        return activeState.status
    }

    if (startingConversionModelIds.value.includes(item.id)) {
        return AiModelFileConversionStatus.Pending
    }

    const activeFile = getOriginalFiles(item.files).find(
        (file) =>
            file.conversionStatus === AiModelFileConversionStatus.Pending ||
            file.conversionStatus === AiModelFileConversionStatus.Converting
    )

    return activeFile?.conversionStatus ?? null
}

function getModelConvertButtonLabel(item: AiModelDto): string {
    const status = getModelCurrentConversionStatus(item)
    return status == null ? t('aiModel.actionConvert') : conversionStatusLabel(status)
}

function getModelCurrentConversionStatusLabel(item: AiModelDto): string {
    const status = getModelCurrentConversionStatus(item)
    return status == null ? t('aiModel.conversionStatusNone') : conversionStatusLabel(status)
}

function getModelCurrentConversionStatusSeverity(
    item: AiModelDto
): 'secondary' | 'success' | 'danger' | 'warn' | 'info' {
    const status = getModelCurrentConversionStatus(item)
    return status == null ? 'secondary' : conversionStatusSeverity(status)
}

function getModelConvertButtonSeverity(item: AiModelDto): 'info' | 'warn' {
    const status = getModelCurrentConversionStatus(item)
    return status === AiModelFileConversionStatus.Pending ? 'warn' : 'info'
}

function hasItemActiveConversionStatus(item: AiModelDto): boolean {
    return getOriginalFiles(item.files).some(
        (file) =>
            file.conversionStatus === AiModelFileConversionStatus.Pending ||
            file.conversionStatus === AiModelFileConversionStatus.Converting
    )
}

function canStartConversion(item: AiModelDto): boolean {
    if (!runtimePlatformInfo.value?.supportsOnnxConversion) {
        return false
    }

    if (convertingModelIds.value.includes(item.id)) {
        return false
    }

    if (hasItemActiveConversionStatus(item)) {
        return false
    }

    if (
        item.conversionPreference === AiModelConversionPreference.DirectOnnx ||
        item.conversionPreference === AiModelConversionPreference.ToRkllm
    ) {
        return false
    }

    const originalFiles = getOriginalFiles(item.files)
    return originalFiles.length > 0 && originalFiles.every((file) => file.fileFormat.toLowerCase() === 'onnx')
}

function hasConvertedFiles(item: AiModelDto): boolean {
    return getConvertedFiles(item.files).length > 0
}

function isDeletingConvertedFile(fileId: string): boolean {
    return deletingConvertedFileIds.value.includes(fileId)
}

function conversionTargetTypeText(file: AiModelFileDto): string {
    return file.conversionTargetType != null
        ? resolvedConversionTypeLabel(file.conversionTargetType)
        : t('aiModel.resolvedConversionUnknown')
}

async function handleStartConversion(item: AiModelDto): Promise<void> {
    if (!canStartConversion(item)) {
        toast.warning(t('aiModel.convertUnavailable'))
        return
    }

    startingConversionModelIds.value = [...startingConversionModelIds.value, item.id]
    let shouldRefreshList = false
    try {
        const result = await startAiModelConversionAsync(item.id)
        if (result.queued) {
            toast.success(result.message || t('aiModel.convertQueued'))
            shouldRefreshList = !conversionHubConnected.value
        } else if (result.canConvert) {
            toast.info(result.message || t('aiModel.convertAcceptedPending'))
        } else if (result.resolvedConversionType === AiModelResolvedConversionType.ToRkllm) {
            toast.warning(result.message || t('aiModel.rkllmUploadRequired'))
        } else {
            toast.warning(result.message || t('aiModel.convertSkipped'))
        }

        if (
            !result.queued &&
            (result.status === AiModelFileConversionStatus.Pending ||
                result.status === AiModelFileConversionStatus.Converting)
        ) {
            shouldRefreshList = true
        }

        if (shouldRefreshList) {
            await loadList()
        }
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        removeStartingConversionModelId(item.id)
    }
}

function handleDeleteConvertedFile(model: AiModelDto, file: AiModelFileDto): void {
    confirm.require({
        message: t('aiModel.deleteConvertedFileConfirm', {
            name: file.originalFileName,
            target: conversionTargetTypeText(file),
        }),
        header: t('common.confirm'),
        acceptClass: 'p-button-danger',
        accept: async () => {
            deletingConvertedFileIds.value = [...deletingConvertedFileIds.value, file.id]
            try {
                await deleteAiModelConvertedFileAsync(model.id, file.id)
                toast.success(t('aiModel.deleteConvertedFileSuccess'))
                await loadList()
            } catch (error) {
                toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
            } finally {
                deletingConvertedFileIds.value = deletingConvertedFileIds.value.filter((value) => value !== file.id)
            }
        },
    })
}

function openEdit(item: AiModelDto): void {
    editingModelId.value = item.id
    editName.value = item.name
    editDescription.value = item.description || ''
    editIdentifierNames.value = normalizeIdentifierNames(item.modelIdentifiers.map((identifier) => identifier.name))
    editIdentifierInput.value = ''
    editVersion.value = item.version || ''
    editLocationKey.value = item.locationKey || ''
    editGenerationCondition.value = item.generationCondition || ''
    editConversionPreference.value =
        item.conversionPreference === AiModelConversionPreference.ToRkllm
            ? AiModelConversionPreference.Auto
            : item.conversionPreference
    editFiles.value = getOriginalFiles(item.files).map((file) => ({
        id: file.id,
        originalFileName: file.originalFileName,
        displayName: file.displayName,
        fileFormat: file.fileFormat,
        fileSizeBytes: file.fileSizeBytes,
        md5: file.md5,
        md5Verified: file.md5Verified,
        fileRole: file.fileRole,
        sortOrder: file.sortOrder,
    }))
    refreshEditFileSortOrders()
    editIdentifierSuggestions.value = []
    editVisible.value = true
}

async function submitEdit(): Promise<void> {
    if (!editingModelId.value) {
        return
    }

    handleIdentifierConfirm(editIdentifierNames, editIdentifierInput, editIdentifierSuggestions)

    if (!editName.value.trim()) {
        toast.warning(t('aiModel.nameRequired'))
        return
    }

    if (editFileRoleError.value) {
        toast.warning(editFileRoleError.value)
        return
    }

    editing.value = true
    try {
        await updateAiModelAsync(editingModelId.value, {
            name: editName.value.trim(),
            description: editDescription.value || null,
            conversionPreference: showEditConversionPreference.value
                ? editConversionPreference.value
                : AiModelConversionPreference.Auto,
            resolvedConversionType: AiModelResolvedConversionType.Unknown,
            identifierNames: editIdentifierNames.value,
            version: editVersion.value || null,
            locationKey: editLocationKey.value || null,
            generationCondition: editGenerationCondition.value || null,
            files: editFiles.value.map((item, index) => ({
                id: item.id,
                fileRole: item.fileRole,
                sortOrder: index,
            })),
        })

        toast.success(t('aiModel.editSuccess'))
        editVisible.value = false
        resetEditState()
        await loadList()
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        editing.value = false
    }
}

function handleDelete(item: AiModelDto): void {
    confirm.require({
        message: t('aiModel.deleteConfirm', { name: item.name }),
        header: t('common.confirm'),
        acceptClass: 'p-button-danger',
        accept: async () => {
            try {
                await deleteAiModelAsync(item.id)
                toast.success(t('aiModel.deleteSuccess'))
                await loadList()
            } catch (error) {
                toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
            }
        },
    })
}

function openCleanUpConfirm(): void {
    confirm.require({
        message: t('aiModel.cleanUpConfirm'),
        header: t('aiModel.cleanUpTitle'),
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
        toast.success(t('aiModel.cleanUpSuccess', { count }))
        await loadList()
    } catch (error) {
        toast.error(error instanceof Error ? error.message : t('common.operationFailed'))
    } finally {
        cleaningUp.value = false
    }
}

onMounted(() => {
    void startConversionHub()
    void loadList()
    void loadRuntimePlatformInfo()
})

onBeforeUnmount(() => {
    clearConversionListRefreshTimer()
    stopConversionHub()
})
</script>

<template>
    <div class="space-y-4">
        <h1 class="text-2xl font-bold tracking-tight">{{ t('aiModel.title') }}</h1>

        <AppCard :beam="true">
            <div class="flex items-center gap-2 border-b border-border/40 px-4 py-3">
                <Button severity="secondary" outlined size="small" :disabled="loading" @click="loadList">
                    <RefreshCw :class="['size-4', loading && 'animate-spin']" />
                </Button>
                <Button severity="secondary" outlined size="small" :disabled="cleaningUp" @click="openCleanUpConfirm">
                    <Eraser class="mr-1 size-4" />
                    {{ t('aiModel.cleanUp') }}
                </Button>
                <Button size="small" @click="openUpload">
                    <Upload class="mr-1 size-4" />
                    {{ t('aiModel.uploadButton') }}
                </Button>
            </div>

            <div class="flex flex-col gap-3 border-b border-border/40 px-3 py-2">
                <div class="grid items-center gap-x-3 gap-y-2 sm:grid-cols-[5.5rem_minmax(0,1fr)]">
                    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ t('aiModel.name') }}</span>
                    <InputText
                        v-model="filterText"
                        size="small"
                        class="!text-xs w-full"
                        :placeholder="t('aiModel.searchPlaceholder')"
                        @keydown.enter="handleSearch"
                    />
                </div>
                <div class="flex items-center gap-2">
                    <Button severity="secondary" outlined size="small" @click="handleSearch">
                        <Search class="mr-1 size-4" />
                        {{ t('common.search') }}
                    </Button>
                    <Button text severity="secondary" size="small" @click="handleReset">{{ t('common.reset') }}</Button>
                    <span class="ml-auto text-xs text-muted-foreground">
                        {{ t('management.totalRecords', { total }) }}
                    </span>
                </div>
            </div>

            <DataTable
                v-model:expandedRows="expandedRows"
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
                    <div class="py-6 text-center text-sm text-muted-foreground">{{ t('aiModel.noData') }}</div>
                </template>
                <template #loading>
                    <div class="py-6 text-center text-sm text-muted-foreground">{{ t('common.loading') }}</div>
                </template>
                <template #expansion="{ data }">
                    <div class="border-t border-border/30 bg-muted/15 px-6 py-4">
                        <div class="mb-3 flex items-center justify-between gap-3">
                            <div class="text-sm font-medium text-foreground">{{ t('aiModel.fileList') }}</div>
                            <div class="flex flex-wrap items-center justify-end gap-2 text-xs text-muted-foreground">
                                <span>{{ t('aiModel.fileCountSummary', { count: data.fileCount }) }}</span>
                                <Tag
                                    v-if="hasConvertedFiles(data)"
                                    :value="
                                        t('aiModel.convertedProductCount', {
                                            count: getConvertedFiles(data.files).length,
                                        })
                                    "
                                    severity="contrast"
                                    class="!text-xs"
                                />
                            </div>
                        </div>
                        <div class="space-y-3">
                            <div
                                v-for="file in getSortedFiles(data.files)"
                                :key="file.id"
                                class="overflow-hidden rounded-xl border border-border/60 bg-card shadow-sm"
                            >
                                <div class="flex items-start justify-between gap-3 border-b border-border/50 px-4 py-3">
                                    <div class="min-w-0 space-y-1">
                                        <div class="flex flex-wrap items-center gap-2">
                                            <div class="truncate font-medium text-foreground">
                                                {{ file.originalFileName }}
                                            </div>
                                            <Tag
                                                :value="
                                                    file.isConvertedFile
                                                        ? t('aiModel.convertedFileTag')
                                                        : t('aiModel.originalFileTag')
                                                "
                                                :severity="file.isConvertedFile ? 'contrast' : 'secondary'"
                                                class="!text-xs"
                                            />
                                            <Tag
                                                v-if="!file.isConvertedFile"
                                                :value="fileRoleLabel(file.fileRole)"
                                                severity="secondary"
                                                class="!text-xs"
                                            />
                                            <Tag
                                                v-if="file.conversionTargetType != null"
                                                :value="conversionTargetTypeText(file)"
                                                severity="info"
                                                class="!text-xs"
                                            />
                                            <Tag
                                                v-if="file.conversionStatus !== AiModelFileConversionStatus.None"
                                                :value="conversionStatusLabel(file.conversionStatus)"
                                                :severity="conversionStatusSeverity(file.conversionStatus)"
                                                class="!text-xs"
                                            />
                                            <Tag
                                                :value="
                                                    file.md5Verified
                                                        ? t('aiModel.md5Verified')
                                                        : t('aiModel.md5Unverified')
                                                "
                                                :severity="file.md5Verified ? 'success' : 'warn'"
                                                class="!text-xs"
                                            />
                                        </div>
                                        <div class="text-xs text-muted-foreground">
                                            {{ file.displayName || '—' }}
                                        </div>
                                        <div v-if="file.conversionErrorMessage" class="text-xs text-red-500">
                                            {{ file.conversionErrorMessage }}
                                        </div>
                                    </div>
                                    <div class="flex flex-col items-end gap-2">
                                        <div class="text-xs text-muted-foreground">#{{ file.sortOrder + 1 }}</div>
                                        <Button
                                            v-if="file.isConvertedFile"
                                            severity="danger"
                                            outlined
                                            size="small"
                                            :loading="isDeletingConvertedFile(file.id)"
                                            :disabled="isDeletingConvertedFile(file.id)"
                                            @click="handleDeleteConvertedFile(data, file)"
                                        >
                                            {{ t('aiModel.actionDeleteConvertedFile') }}
                                        </Button>
                                    </div>
                                </div>
                                <div class="grid gap-3 px-4 py-3 text-sm md:grid-cols-2 xl:grid-cols-4">
                                    <div>
                                        <div class="text-xs text-muted-foreground">{{ t('aiModel.fileFormat') }}</div>
                                        <div class="mt-1">{{ file.fileFormat }}</div>
                                    </div>
                                    <div>
                                        <div class="text-xs text-muted-foreground">{{ t('aiModel.fileSize') }}</div>
                                        <div class="mt-1">{{ formatFileSize(file.fileSizeBytes) }}</div>
                                    </div>
                                    <div class="xl:col-span-2">
                                        <div class="text-xs text-muted-foreground">{{ t('aiModel.md5') }}</div>
                                        <div class="mt-1 break-all font-mono text-xs text-foreground">
                                            {{ file.md5 }}
                                        </div>
                                    </div>
                                    <div v-if="file.isConvertedFile">
                                        <div class="text-xs text-muted-foreground">
                                            {{ t('aiModel.conversionTime') }}
                                        </div>
                                        <div class="mt-1">{{ formatDateTime(file.conversionTime) }}</div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </template>

                <Column expander style="width: 3rem" />
                <Column :header="t('aiModel.name')" style="min-width: 16rem">
                    <template #body="{ data }">
                        <div class="space-y-1 py-1">
                            <div class="flex flex-wrap items-center gap-2">
                                <div class="font-medium text-foreground">{{ data.name }}</div>
                                <Tag
                                    v-if="getModelCurrentConversionStatus(data) != null"
                                    :severity="getModelCurrentConversionStatusSeverity(data)"
                                    :value="getModelCurrentConversionStatusLabel(data)"
                                    class="!text-xs"
                                />
                            </div>
                            <div v-if="data.description" class="line-clamp-2 text-xs text-muted-foreground">
                                {{ data.description }}
                            </div>
                        </div>
                    </template>
                </Column>
                <Column :header="t('aiModel.modelIdentifier')" style="min-width: 12rem">
                    <template #body="{ data }">
                        <div v-if="data.modelIdentifiers.length" class="flex flex-wrap gap-1 py-1">
                            <Tag
                                v-for="identifier in data.modelIdentifiers"
                                :key="identifier.id"
                                :value="identifier.name"
                                severity="secondary"
                                class="!text-xs"
                            />
                        </div>
                        <span v-else class="text-xs text-muted-foreground">—</span>
                    </template>
                </Column>
                <Column :header="t('aiModel.conversionPreference')" style="min-width: 11rem">
                    <template #body="{ data }">
                        <div class="space-y-1 py-1 text-xs">
                            <div>
                                <span class="text-muted-foreground">{{ t('aiModel.conversionPreference') }}：</span>
                                <span>{{ conversionPreferenceLabel(data.conversionPreference) }}</span>
                            </div>
                            <div>
                                <span class="text-muted-foreground">{{ t('aiModel.resolvedConversionType') }}：</span>
                                <span>{{ resolvedConversionTypeText(data) }}</span>
                            </div>
                        </div>
                    </template>
                </Column>
                <Column :header="t('aiModel.fileCount')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <div class="py-1 text-xs">{{ t('aiModel.fileCountSummary', { count: data.fileCount }) }}</div>
                    </template>
                </Column>
                <Column field="loadStatus" :header="t('aiModel.loadStatus')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <Tag
                            :severity="loadStatusSeverity(data.loadStatus)"
                            :value="loadStatusLabel(data.loadStatus)"
                            class="!text-xs"
                        />
                    </template>
                </Column>
                <Column field="creatorUserName" :header="t('aiModel.creatorUserName')" style="min-width: 8rem">
                    <template #body="{ data }">
                        <span class="text-xs">{{ data.creatorUserName || '—' }}</span>
                    </template>
                </Column>
                <Column field="creationTime" :header="t('aiModel.creationTime')" style="min-width: 11rem">
                    <template #body="{ data }">
                        <span class="text-xs">{{ formatDateTime(data.creationTime) }}</span>
                    </template>
                </Column>
                <Column :header="t('common.actions')" style="min-width: 10rem">
                    <template #body="{ data }">
                        <div class="flex flex-wrap items-center gap-1">
                            <Button
                                :severity="getModelConvertButtonSeverity(data)"
                                outlined
                                size="small"
                                :loading="
                                    getModelCurrentConversionStatus(data) === AiModelFileConversionStatus.Converting
                                "
                                :disabled="!canStartConversion(data)"
                                @click="handleStartConversion(data)"
                            >
                                {{ getModelConvertButtonLabel(data) }}
                            </Button>
                            <Button severity="secondary" outlined size="small" @click="openEdit(data)">
                                {{ t('common.edit') }}
                            </Button>
                            <Button severity="danger" outlined size="small" @click="handleDelete(data)">
                                {{ t('aiModel.actionDelete') }}
                            </Button>
                        </div>
                    </template>
                </Column>
            </DataTable>

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

        <Dialog
            v-model:visible="uploadVisible"
            modal
            :header="t('aiModel.uploadDialogTitle')"
            :closable="!uploading"
            :dismissable-mask="!uploading"
            style="width: min(64rem, 96vw)"
            @hide="!uploading && resetUploadState()"
        >
            <div class="space-y-4 p-4">
                <div class="rounded-xl border border-border/60 bg-muted/20 p-4">
                    <div class="flex flex-wrap items-center gap-2">
                        <span class="text-sm font-medium">{{ t('aiModel.runtimePlatformLabel') }}</span>
                        <Tag
                            :severity="runtimePlatformInfo?.isSupported ? 'success' : 'danger'"
                            :value="
                                runtimePlatformInfo?.isSupported
                                    ? t('aiModel.runtimeSupported')
                                    : t('aiModel.runtimeUnsupported')
                            "
                            class="!text-xs"
                        />
                        <span v-if="loadingRuntimeInfo" class="text-xs text-muted-foreground">
                            {{ t('common.loading') }}
                        </span>
                    </div>
                    <div
                        v-if="runtimePlatformInfo"
                        class="mt-3 grid gap-3 text-sm text-muted-foreground md:grid-cols-2"
                    >
                        <div>
                            <div class="font-medium text-foreground">{{ runtimePlatformInfo.platformName }}</div>
                            <div class="text-xs">
                                {{ runtimePlatformInfo.operatingSystem }} / {{ runtimePlatformInfo.architecture }}
                            </div>
                            <div v-if="runtimePlatformInfo.compatibilityDescription" class="mt-1 text-xs">
                                {{ runtimePlatformInfo.compatibilityDescription }}
                            </div>
                            <div v-if="runtimePlatformInfo.unsupportedReason" class="mt-1 text-xs text-red-500">
                                {{ runtimePlatformInfo.unsupportedReason }}
                            </div>
                        </div>
                        <div class="space-y-1 text-xs">
                            <div>
                                <span class="text-foreground">{{ t('aiModel.supportedExtensions') }}:</span>
                                {{ supportedExtensionsText }}
                            </div>
                            <div>
                                <span class="text-foreground">{{ t('aiModel.onnxConversion') }}:</span>
                                {{
                                    runtimePlatformInfo.supportsOnnxConversion
                                        ? t('aiModel.onnxConversionSupported')
                                        : t('aiModel.onnxConversionUnsupported')
                                }}
                            </div>
                            <div>{{ t('aiModel.multiFileUploadHint') }}</div>
                        </div>
                    </div>
                </div>

                <input
                    ref="fileInputRef"
                    type="file"
                    class="hidden"
                    multiple
                    :accept="uploadAccept"
                    @change="handleFileInput"
                />

                <div
                    class="rounded-2xl border border-dashed px-6 py-8 transition"
                    :class="isDragging ? 'border-primary bg-primary/5' : 'border-border/70 bg-muted/20'"
                    @dragenter.prevent="isDragging = true"
                    @dragover.prevent="isDragging = true"
                    @dragleave.prevent="isDragging = false"
                    @drop.prevent="handleDrop"
                >
                    <div class="flex flex-col items-center gap-3 text-center">
                        <div class="text-sm font-medium text-foreground">{{ t('aiModel.dragFileHere') }}</div>
                        <div class="text-xs text-muted-foreground">{{ supportedExtensionsText }}</div>
                        <div class="flex flex-wrap items-center justify-center gap-2">
                            <Button size="small" @click="triggerFileSelect">
                                {{ t('aiModel.clickToChooseFile') }}
                            </Button>
                            <Button
                                v-if="uploadFiles.length"
                                severity="secondary"
                                outlined
                                size="small"
                                @click="triggerFileSelect"
                            >
                                {{ t('aiModel.replaceSelectedFile') }}
                            </Button>
                            <Button
                                v-if="uploadFiles.length"
                                severity="secondary"
                                text
                                size="small"
                                @click="clearSelectedFiles"
                            >
                                {{ t('common.reset') }}
                            </Button>
                        </div>
                    </div>
                </div>

                <div class="grid gap-4 md:grid-cols-2">
                    <div>
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.name') }}</label>
                        <InputText
                            v-model="uploadName"
                            size="small"
                            class="w-full"
                            :placeholder="t('aiModel.modelNamePlaceholder')"
                        />
                    </div>
                    <div v-if="showUploadConversionPreference">
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.conversionPreference') }}</label>
                        <Select
                            v-model="uploadConversionPreference"
                            class="w-full"
                            :options="getConversionPreferenceOptions()"
                            option-label="label"
                            option-value="value"
                            size="small"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                        <div class="mt-1 text-xs text-muted-foreground">
                            {{ t('aiModel.conversionPreferenceHint') }}
                        </div>
                    </div>
                    <div class="md:col-span-2">
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.description') }}</label>
                        <Textarea v-model="uploadDescription" rows="3" class="w-full" />
                    </div>
                    <div class="md:col-span-2">
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.modelIdentifier') }}</label>
                        <div class="space-y-2">
                            <div
                                v-if="uploadIdentifierNames.length"
                                class="flex flex-wrap gap-2 rounded-xl border border-border/60 bg-muted/20 p-2"
                            >
                                <div
                                    v-for="identifierName in uploadIdentifierNames"
                                    :key="identifierName"
                                    class="inline-flex items-center gap-1 rounded-full bg-primary/10 px-3 py-1 text-sm text-foreground"
                                >
                                    <span>{{ identifierName }}</span>
                                    <Button
                                        size="small"
                                        text
                                        severity="secondary"
                                        type="button"
                                        class="inline-flex size-4 items-center justify-center rounded-full text-muted-foreground transition hover:bg-black/5 hover:text-foreground"
                                        @click="removeUploadIdentifierName(identifierName)"
                                    >
                                        ×
                                    </Button>
                                </div>
                            </div>
                            <div class="relative">
                                <InputText
                                    v-model="uploadIdentifierInput"
                                    class="w-full"
                                    :placeholder="t('aiModel.modelIdentifierPlaceholder')"
                                    size="small"
                                    @input="void handleUploadIdentifierInputChanged()"
                                    @keydown.enter.prevent="handleUploadIdentifierConfirm"
                                    @blur="handleUploadIdentifierBlur"
                                />
                                <div
                                    v-if="uploadIdentifierSuggestions.length"
                                    class="absolute z-20 mt-1 max-h-56 w-full overflow-auto rounded-xl border border-border/60 bg-card p-1 shadow-lg"
                                >
                                    <Button
                                        size="small"
                                        text
                                        severity="secondary"
                                        type="button"
                                        v-for="suggestion in uploadIdentifierSuggestions"
                                        :key="suggestion"
                                        class="block w-full rounded-lg px-3 py-2 text-left text-sm transition hover:bg-muted"
                                        @mousedown.prevent
                                        @click="selectUploadIdentifierSuggestion(suggestion)"
                                    >
                                        {{ suggestion }}
                                    </Button>
                                </div>
                            </div>
                        </div>
                        <div class="mt-1 text-xs text-muted-foreground">{{ t('aiModel.identifierHint') }}</div>
                    </div>
                    <div>
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.locationKey') }}</label>
                        <InputText
                            size="small"
                            v-model="uploadLocationKey"
                            class="w-full"
                            :placeholder="t('aiModel.locationPlaceholder')"
                        />
                        <div class="mt-1 text-xs text-muted-foreground">{{ t('aiModel.locationKeyHint') }}</div>
                    </div>
                    <div>
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.generationCondition') }}</label>
                        <InputText
                            size="small"
                            v-model="uploadGenerationCondition"
                            class="w-full"
                            :placeholder="t('aiModel.generationConditionPlaceholder')"
                        />
                        <div class="mt-1 text-xs text-muted-foreground">{{ t('aiModel.generationConditionHint') }}</div>
                    </div>
                </div>

                <div class="space-y-3">
                    <div class="flex items-center justify-between gap-3">
                        <div class="text-sm font-medium text-foreground">{{ t('aiModel.selectedFiles') }}</div>
                        <div class="text-xs text-muted-foreground">
                            {{ t('aiModel.fileCountSummary', { count: uploadFiles.length }) }}
                        </div>
                    </div>

                    <div
                        v-if="!uploadFiles.length"
                        class="rounded-xl border border-border/60 bg-muted/20 px-4 py-6 text-sm text-muted-foreground"
                    >
                        {{ t('aiModel.noSelectedFiles') }}
                    </div>

                    <div
                        v-for="file in uploadFiles"
                        :key="file.localId"
                        class="overflow-hidden rounded-xl border border-border/60 bg-card shadow-sm"
                    >
                        <div class="flex items-start justify-between gap-3 border-b border-border/50 px-4 py-3">
                            <div class="min-w-0 space-y-1">
                                <div class="truncate font-medium text-foreground">{{ file.file.name }}</div>
                                <div class="text-xs text-muted-foreground">
                                    {{ formatFileSize(file.file.size) }} · {{ getFileExtension(file.file.name) || '—' }}
                                </div>
                            </div>
                            <Button
                                severity="secondary"
                                text
                                size="small"
                                :disabled="uploading"
                                @click="removeSelectedUploadFile(file.localId)"
                            >
                                <X class="size-4" />
                            </Button>
                        </div>
                        <div class="grid gap-3 px-4 py-3 md:grid-cols-2 xl:grid-cols-4">
                            <div class="space-y-1 xl:col-span-2">
                                <label class="mb-1 block text-xs font-medium text-muted-foreground">
                                    {{ t('aiModel.fileMd5') }}
                                </label>
                                <div
                                    class="flex h-7 items-center rounded-md border border-border/60 bg-muted/20 px-3 font-mono text-xs leading-none text-foreground"
                                >
                                    <span class="break-all">
                                        {{ file.checkingMd5 ? t('aiModel.md5Checking') : file.md5 }}
                                    </span>
                                </div>
                            </div>
                            <div class="space-y-1">
                                <label class="mb-1 block text-xs font-medium text-muted-foreground">
                                    {{ t('aiModel.fileRole') }}
                                </label>
                                <Select
                                    v-model="file.fileRole"
                                    class="w-full"
                                    size="small"
                                    :options="getFileRoleOptions(uploadFiles.length)"
                                    option-label="label"
                                    option-value="value"
                                    :pt="{
                                        root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                        label: {
                                            class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                        },
                                        dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                                    }"
                                    :disabled="uploadFiles.length <= 1"
                                    @change="handleUploadFileRoleChanged"
                                />
                            </div>
                            <div class="space-y-1">
                                <label class="mb-1 block text-xs font-medium text-muted-foreground">
                                    {{ t('aiModel.md5') }}
                                </label>
                                <div class="flex h-7 items-center">
                                    <Tag
                                        v-if="file.md5CheckResult"
                                        :value="
                                            file.md5CheckResult.exists
                                                ? t('aiModel.md5Duplicate')
                                                : t('aiModel.md5Unique')
                                        "
                                        :severity="file.md5CheckResult.exists ? 'warn' : 'success'"
                                        class="!text-xs w-full h-full"
                                    />
                                    <span v-else class="text-xs text-muted-foreground">—</span>
                                </div>
                            </div>
                        </div>
                        <div
                            v-if="file.md5CheckResult?.exists"
                            class="border-t border-border/40 bg-amber-500/10 px-4 py-3 text-xs text-amber-700"
                        >
                            {{
                                t('aiModel.md5DuplicateHint', {
                                    name: file.md5CheckResult.existingModelName || '—',
                                    fileName: file.md5CheckResult.existingOriginalFileName || '—',
                                })
                            }}
                        </div>
                    </div>

                    <div
                        v-if="uploadFileRoleError"
                        class="rounded-xl border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm text-amber-700"
                    >
                        {{ uploadFileRoleError }}
                    </div>
                </div>

                <div v-if="uploading" class="space-y-2">
                    <div class="text-sm font-medium">{{ t('aiModel.uploadingProgress') }}</div>
                    <ProgressBar :value="uploadProgress" :show-value="true" />
                </div>
            </div>

            <template #footer>
                <Button text severity="secondary" :disabled="uploading" @click="uploadVisible = false">
                    {{ t('common.cancel') }}
                </Button>
                <Button v-if="uploading" severity="secondary" outlined @click="cancelUpload">
                    {{ t('aiModel.cancelUpload') }}
                </Button>
                <Button :loading="uploading" :disabled="!canSubmitUpload" @click="submitUpload">
                    {{ t('aiModel.uploadModel') }}
                </Button>
            </template>
        </Dialog>

        <Dialog
            v-model:visible="editVisible"
            modal
            :header="t('aiModel.editDialogTitle')"
            :closable="!editing"
            :dismissable-mask="!editing"
            style="width: min(56rem, 96vw)"
            @hide="!editing && resetEditState()"
        >
            <div class="space-y-4 p-4">
                <div class="grid gap-4 md:grid-cols-2">
                    <div>
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.name') }}</label>
                        <InputText v-model="editName" size="small" class="w-full" />
                    </div>
                    <div>
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.version') }}</label>
                        <InputText v-model="editVersion" size="small" class="w-full" />
                    </div>
                    <div v-if="showEditConversionPreference">
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.conversionPreference') }}</label>
                        <Select
                            v-model="editConversionPreference"
                            class="w-full"
                            :options="getConversionPreferenceOptions()"
                            option-label="label"
                            option-value="value"
                            size="small"
                            :pt="{
                                root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                label: {
                                    class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                },
                                dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                            }"
                        />
                        <div class="mt-1 text-xs text-muted-foreground">
                            {{ t('aiModel.conversionPreferenceHint') }}
                        </div>
                    </div>
                    <div class="md:col-span-2">
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.description') }}</label>
                        <Textarea v-model="editDescription" rows="3" class="w-full" />
                    </div>
                    <div class="md:col-span-2">
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.modelIdentifier') }}</label>
                        <div class="space-y-2">
                            <div
                                v-if="editIdentifierNames.length"
                                class="flex flex-wrap gap-2 rounded-xl border border-border/60 bg-muted/20 p-2"
                            >
                                <div
                                    v-for="identifierName in editIdentifierNames"
                                    :key="identifierName"
                                    class="inline-flex items-center gap-1 rounded-full bg-primary/10 px-3 py-1 text-sm text-foreground"
                                >
                                    <span>{{ identifierName }}</span>
                                    <Button
                                        size="small"
                                        text
                                        severity="secondary"
                                        type="button"
                                        class="inline-flex size-4 items-center justify-center rounded-full text-muted-foreground transition hover:bg-black/5 hover:text-foreground"
                                        @click="removeEditIdentifierName(identifierName)"
                                    >
                                        ×
                                    </Button>
                                </div>
                            </div>
                            <div class="relative">
                                <InputText
                                    v-model="editIdentifierInput"
                                    class="w-full"
                                    size="small"
                                    :placeholder="t('aiModel.modelIdentifierPlaceholder')"
                                    @input="void handleEditIdentifierInputChanged()"
                                    @keydown.enter.prevent="handleEditIdentifierConfirm"
                                    @blur="handleEditIdentifierBlur"
                                />
                                <div
                                    v-if="editIdentifierSuggestions.length"
                                    class="absolute z-20 mt-1 max-h-56 w-full overflow-auto rounded-xl border border-border/60 bg-card p-1 shadow-lg"
                                >
                                    <Button
                                        size="small"
                                        text
                                        severity="secondary"
                                        type="button"
                                        v-for="suggestion in editIdentifierSuggestions"
                                        :key="suggestion"
                                        class="block w-full rounded-lg px-3 py-2 text-left text-sm transition hover:bg-muted"
                                        @mousedown.prevent
                                        @click="selectEditIdentifierSuggestion(suggestion)"
                                    >
                                        {{ suggestion }}
                                    </Button>
                                </div>
                            </div>
                        </div>
                        <div class="mt-1 text-xs text-muted-foreground">{{ t('aiModel.identifierHint') }}</div>
                    </div>
                    <div>
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.locationKey') }}</label>
                        <InputText
                            v-model="editLocationKey"
                            size="small"
                            class="w-full"
                            :placeholder="t('aiModel.locationPlaceholder')"
                        />
                        <div class="mt-1 text-xs text-muted-foreground">{{ t('aiModel.locationKeyHint') }}</div>
                    </div>
                    <div>
                        <label class="mb-1 block text-sm font-medium">{{ t('aiModel.generationCondition') }}</label>
                        <InputText
                            v-model="editGenerationCondition"
                            size="small"
                            class="w-full"
                            :placeholder="t('aiModel.generationConditionPlaceholder')"
                        />
                        <div class="mt-1 text-xs text-muted-foreground">{{ t('aiModel.generationConditionHint') }}</div>
                    </div>
                </div>

                <div class="space-y-3">
                    <div class="flex items-center justify-between gap-3">
                        <div class="text-sm font-medium text-foreground">{{ t('aiModel.fileList') }}</div>
                        <div class="text-xs text-muted-foreground">
                            {{ t('aiModel.fileCountSummary', { count: editFiles.length }) }}
                        </div>
                    </div>
                    <div
                        v-for="file in editFiles"
                        :key="file.id"
                        class="overflow-hidden rounded-xl border border-border/60 bg-card shadow-sm"
                    >
                        <div class="flex items-start justify-between gap-3 border-b border-border/50 px-4 py-3">
                            <div class="min-w-0 space-y-1">
                                <div class="truncate font-medium text-foreground">{{ file.originalFileName }}</div>
                                <div class="text-xs text-muted-foreground">
                                    {{ file.displayName || '—' }} · {{ file.fileFormat }} ·
                                    {{ formatFileSize(file.fileSizeBytes) }}
                                </div>
                            </div>
                            <Tag
                                :value="file.md5Verified ? t('aiModel.md5Verified') : t('aiModel.md5Unverified')"
                                :severity="file.md5Verified ? 'success' : 'warn'"
                                class="!text-xs"
                            />
                        </div>
                        <div class="grid gap-3 px-4 py-3 md:grid-cols-2 xl:grid-cols-4">
                            <div class="space-y-1 xl:col-span-2">
                                <label class="mb-1 block text-xs font-medium text-muted-foreground">
                                    {{ t('aiModel.md5') }}
                                </label>
                                <div
                                    class="flex h-7 items-center rounded-md border border-border/60 bg-muted/20 px-3 font-mono text-xs leading-none text-foreground"
                                >
                                    <span class="break-all">{{ file.md5 }}</span>
                                </div>
                            </div>
                            <div class="space-y-1">
                                <label class="mb-1 block text-xs font-medium text-muted-foreground">
                                    {{ t('aiModel.fileRole') }}
                                </label>
                                <Select
                                    v-model="file.fileRole"
                                    class="w-full"
                                    :options="getFileRoleOptions(editFiles.length)"
                                    option-label="label"
                                    option-value="value"
                                    :disabled="editFiles.length <= 1"
                                    @change="handleEditFileRoleChanged"
                                    size="small"
                                    :pt="{
                                        root: { class: '!py-0 !px-2 !text-xs !h-7 !flex !items-center' },
                                        label: {
                                            class: '!text-xs !py-0 !leading-none !truncate !flex-1 !flex !items-center !h-full',
                                        },
                                        dropdown: { class: '!w-6 !flex !items-center !justify-center' },
                                    }"
                                />
                            </div>
                            <div class="space-y-1">
                                <label class="mb-1 block text-xs font-medium text-muted-foreground">
                                    {{ t('aiModel.fileOrder') }}
                                </label>
                                <div
                                    class="flex h-7 items-center rounded-md border border-border/60 bg-muted/20 px-3 text-xs leading-none text-foreground"
                                >
                                    #{{ file.sortOrder + 1 }}
                                </div>
                            </div>
                        </div>
                    </div>
                    <div
                        v-if="editFileRoleError"
                        class="rounded-xl border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm text-amber-700"
                    >
                        {{ editFileRoleError }}
                    </div>
                </div>
            </div>

            <template #footer>
                <Button text severity="secondary" :disabled="editing" @click="editVisible = false">
                    {{ t('common.cancel') }}
                </Button>
                <Button :loading="editing" :disabled="!canSubmitEdit" @click="submitEdit">
                    {{ t('common.save') }}
                </Button>
            </template>
        </Dialog>

        <ConfirmDialog />
    </div>
</template>
