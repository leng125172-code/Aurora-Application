import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import {
    type GetMotorAxisListDto,
    type MotorAxisDto,
    type MotorScanProgressDto,
    MotorScanProgressKind,
    type MoveMotorInput,
    type ReadHoldingRegistersInput,
    type RegisterReadResultDto,
    type ScanMotorDevicesInput,
    type ScanMotorDevicesResultDto,
    type SetMotorRotationAngleLimitFromCurrentDto,
    type SetMotorRotationAngleRangeDto,
    type UpdateMotorAxisDto,
    type WriteSingleRegisterInput,
    clearMotorFault,
    disableMotor,
    emergencyStopMotor,
    enableMotor,
    getMotorAxis,
    getMotorAxisList,
    homeMotor,
    moveMotorAbsolute,
    moveMotorRelative,
    readMotorHoldingRegisters,
    refreshMotorStatus,
    scanMotorDevices,
    setMotorRotationAngleLimitFromCurrent,
    setMotorRotationAngleRange,
    stopMotor,
    updateMotorAxis,
    writeMotorSingleRegister,
} from '@/api/motors'

export const useMotorStore = defineStore('motor', () => {
    const motors = ref<MotorAxisDto[]>([])
    const selectedMotor = ref<MotorAxisDto | null>(null)
    const loading = ref(false)

    // ─── 扫描进度（SignalR 推送） ─────────────────────────────────────────────
    /** 扫描进度事件列表（按时间顺序追加，最多保留 500 条） */
    const scanProgress = ref<MotorScanProgressDto[]>([])
    /** SignalR 连接状态 */
    const scanHubConnected = ref(false)
    /** 当前是否正在执行扫描（由前端控制，便于 UI 展示） */
    const isScanning = ref(false)

    const MAX_PROGRESS_ITEMS = 500
    let scanConnection: signalR.HubConnection | null = null

    /** 启动扫描进度 SignalR Hub 连接 */
    async function startScanHub() {
        if (scanConnection) return
        scanConnection = new signalR.HubConnectionBuilder()
            .withUrl('/signalr-hubs/motor-scan', { skipNegotiation: false })
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build()

        scanConnection.on('ReceiveMotorScanProgressAsync', (progress: MotorScanProgressDto) => {
            scanProgress.value.push(progress)
            // 控制最大条数，避免内存膨胀
            if (scanProgress.value.length > MAX_PROGRESS_ITEMS) {
                scanProgress.value.splice(0, scanProgress.value.length - MAX_PROGRESS_ITEMS)
            }
            // 进度推送到完成态时同步本地标识
            if (progress.kind === MotorScanProgressKind.Completed) {
                isScanning.value = false
            }
        })

        scanConnection.onclose(() => {
            scanHubConnected.value = false
        })
        scanConnection.onreconnected(() => {
            scanHubConnected.value = true
        })

        try {
            await scanConnection.start()
            scanHubConnected.value = true
        } catch {
            scanHubConnected.value = false
        }
    }

    /** 停止扫描进度 SignalR Hub 连接 */
    async function stopScanHub() {
        if (scanConnection) {
            try {
                await scanConnection.stop()
            } finally {
                scanConnection = null
                scanHubConnected.value = false
            }
        }
    }

    /** 清空扫描进度，常用于一次新的扫描开始前 */
    function clearScanProgress() {
        scanProgress.value = []
    }

    function applyUpdate(updated: MotorAxisDto) {
        const index = motors.value.findIndex((motor) => motor.id === updated.id)
        if (index >= 0) {
            motors.value[index] = updated
        } else {
            motors.value.push(updated)
        }
        if (selectedMotor.value?.id === updated.id) {
            selectedMotor.value = updated
        }
    }

    async function fetchList(params: GetMotorAxisListDto = {}) {
        loading.value = true
        try {
            const result = await getMotorAxisList({ maxResultCount: 100, refreshHardware: true, ...params })
            motors.value = [...result.items]
        } finally {
            loading.value = false
        }
    }

    async function refresh(id: string): Promise<MotorAxisDto> {
        const updated = await getMotorAxis(id)
        applyUpdate(updated)
        return updated
    }

    function select(id: string) {
        selectedMotor.value = motors.value.find((motor) => motor.id === id) ?? null
    }

    async function scan(input: ScanMotorDevicesInput): Promise<ScanMotorDevicesResultDto> {
        clearScanProgress()
        isScanning.value = true
        try {
            const result = await scanMotorDevices(input)
            await fetchList({ refreshHardware: false })
            return result
        } finally {
            isScanning.value = false
        }
    }

    async function update(id: string, dto: UpdateMotorAxisDto): Promise<MotorAxisDto> {
        const updated = await updateMotorAxis(id, dto)
        applyUpdate(updated)
        return updated
    }

    async function setRotationAngleRange(id: string, dto: SetMotorRotationAngleRangeDto): Promise<MotorAxisDto> {
        const updated = await setMotorRotationAngleRange(id, dto)
        applyUpdate(updated)
        return updated
    }

    async function setRotationAngleLimitFromCurrent(
        id: string,
        dto: SetMotorRotationAngleLimitFromCurrentDto
    ): Promise<MotorAxisDto> {
        const updated = await setMotorRotationAngleLimitFromCurrent(id, dto)
        applyUpdate(updated)
        return updated
    }

    async function refreshStatus(id: string): Promise<MotorAxisDto> {
        const updated = await refreshMotorStatus(id)
        applyUpdate(updated)
        return updated
    }

    async function enable(id: string): Promise<MotorAxisDto> {
        const updated = await enableMotor(id)
        applyUpdate(updated)
        return updated
    }

    async function disable(id: string): Promise<MotorAxisDto> {
        const updated = await disableMotor(id)
        applyUpdate(updated)
        return updated
    }

    async function moveAbsolute(id: string, dto: MoveMotorInput): Promise<MotorAxisDto> {
        const updated = await moveMotorAbsolute(id, dto)
        applyUpdate(updated)
        return updated
    }

    async function moveRelative(id: string, dto: MoveMotorInput): Promise<MotorAxisDto> {
        const updated = await moveMotorRelative(id, dto)
        applyUpdate(updated)
        return updated
    }

    async function stop(id: string): Promise<MotorAxisDto> {
        const updated = await stopMotor(id)
        applyUpdate(updated)
        return updated
    }

    async function emergencyStop(id: string): Promise<MotorAxisDto> {
        const updated = await emergencyStopMotor(id)
        applyUpdate(updated)
        return updated
    }

    async function home(id: string): Promise<MotorAxisDto> {
        const updated = await homeMotor(id)
        applyUpdate(updated)
        return updated
    }

    async function clearFault(id: string): Promise<MotorAxisDto> {
        const updated = await clearMotorFault(id)
        applyUpdate(updated)
        return updated
    }

    async function readRegisters(id: string, dto: ReadHoldingRegistersInput): Promise<RegisterReadResultDto> {
        return await readMotorHoldingRegisters(id, dto)
    }

    async function writeRegister(id: string, dto: WriteSingleRegisterInput): Promise<boolean> {
        return await writeMotorSingleRegister(id, dto)
    }

    return {
        motors,
        selectedMotor,
        loading,
        scanProgress,
        scanHubConnected,
        isScanning,
        startScanHub,
        stopScanHub,
        clearScanProgress,
        fetchList,
        refresh,
        select,
        scan,
        update,
        setRotationAngleRange,
        setRotationAngleLimitFromCurrent,
        refreshStatus,
        enable,
        disable,
        moveAbsolute,
        moveRelative,
        stop,
        emergencyStop,
        home,
        clearFault,
        readRegisters,
        writeRegister,
    }
})
