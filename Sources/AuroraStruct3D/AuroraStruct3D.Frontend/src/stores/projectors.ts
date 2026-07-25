/**
 * 投影仪 Pinia Store
 *
 * 负责：
 *  1. 维护投影仪设备列表响应式数据
 *  2. 通过 SignalR Hub（/signalr-hubs/projector）接收实时状态推送
 *  3. 封装所有控制操作（连接/断开/LED/显示等）
 */
import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import {
    type ProjectorDeviceDto,
    type GetProjectorListDto,
    type UpdateProjectorDeviceDto,
    type SetProjectorLightDto,
    type SetProjectorDisplayModeDto,
    type SetProjectorColorDto,
    type SetProjectorFlipDto,
    type SetProjectorTriggerModeDto,
    type SetProjectorBootImageDto,
    type SetProjectorCheckerboardDto,
    type SetProjectorRgbDto,
    type TriggerProjectorDto,
    type NextProjectorFrameDto,
    type WriteProjectorRegisterDto,
    getProjectorList,
    getProjector,
    scanProjectors,
    updateProjector,
    connectProjector,
    disconnectProjector,
    projectorLedOn,
    projectorLedOff,
    setProjectorLight,
    setProjectorDisplayMode,
    setProjectorColor,
    setProjectorFlip,
    setProjectorTriggerMode,
    setProjectorBootImage,
    setProjectorCheckerboard,
    setProjectorRgb,
    triggerProjectorOnce,
    nextFrameProjector,
    projectorSoftReset,
    projectorSaveParams,
    readProjectorRegister,
    writeProjectorRegister,
} from '@/api/projectors'

export const useProjectorStore = defineStore('projector', () => {
    // ─── 状态 ─────────────────────────────────────────────────────────────────

    /** 投影仪列表 */
    const projectors = ref<ProjectorDeviceDto[]>([])
    /** 当前选中的投影仪（控制页使用） */
    const selectedProjector = ref<ProjectorDeviceDto | null>(null)
    /** 是否正在加载 */
    const loading = ref(false)
    /** SignalR 连接状态 */
    const hubConnected = ref(false)

    let connection: signalR.HubConnection | null = null

    // ─── 辅助函数 ─────────────────────────────────────────────────────────────

    /** 用最新数据更新列表中某台投影仪 */
    function _applyUpdate(updated: ProjectorDeviceDto) {
        const idx = projectors.value.findIndex((p) => p.id === updated.id)
        if (idx >= 0) {
            projectors.value[idx] = updated
        } else {
            projectors.value.push(updated)
        }
        // 同步更新选中的设备
        if (selectedProjector.value?.id === updated.id) {
            selectedProjector.value = updated
        }
    }

    // ─── SignalR ──────────────────────────────────────────────────────────────

    /** 启动 SignalR Hub 连接 */
    async function startHub() {
        if (connection) return

        connection = new signalR.HubConnectionBuilder()
            .withUrl('/signalr-hubs/projector', { skipNegotiation: false })
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build()

        // 接收完整投影仪状态推送
        connection.on('ReceiveProjectorStateAsync', (projector: ProjectorDeviceDto) => {
            _applyUpdate(projector)
        })

        // 接收连接状态变更事件
        connection.on('ReceiveProjectorConnectionChangedAsync', (id: string, _status: string) => {
            const p = projectors.value.find((x) => x.id === id)
            if (p) {
                // 触发一次完整查询以刷新数据
                void refreshProjector(id)
            }
        })

        // 接收 LED 状态变更事件
        connection.on('ReceiveProjectorLedChangedAsync', (id: string, _ledStatus: string) => {
            void refreshProjector(id)
        })

        connection.onclose(() => {
            hubConnected.value = false
        })
        connection.onreconnected(() => {
            hubConnected.value = true
        })

        try {
            await connection.start()
            hubConnected.value = true
        } catch {
            hubConnected.value = false
        }
    }

    /** 停止 SignalR Hub 连接 */
    async function stopHub() {
        if (connection) {
            await connection.stop()
            connection = null
            hubConnected.value = false
        }
    }

    // ─── 查询 ─────────────────────────────────────────────────────────────────

    /** 加载全部投影仪列表 */
    async function fetchList(params: GetProjectorListDto = {}) {
        loading.value = true
        try {
            const result = await getProjectorList({ maxResultCount: 100, ...params })
            projectors.value = [...result.items]
        } finally {
            loading.value = false
        }
    }

    /** 刷新单台投影仪 */
    async function refreshProjector(id: string) {
        const updated = await getProjector(id)
        _applyUpdate(updated)
    }

    /** 选中投影仪（切换控制目标） */
    function selectProjector(id: string) {
        selectedProjector.value = projectors.value.find((p) => p.id === id) ?? null
    }

    // ─── CRUD ─────────────────────────────────────────────────────────────────

    /** 扫描 USB HID 投影仪，自动同步数据库记录，返回检测到的数量 */
    async function scan(): Promise<number> {
        const count = await scanProjectors()
        // 扫描完成后刷新列表
        await fetchList()
        return count
    }

    async function update(id: string, dto: UpdateProjectorDeviceDto): Promise<ProjectorDeviceDto> {
        const updated = await updateProjector(id, dto)
        _applyUpdate(updated)
        return updated
    }

    // ─── 连接控制 ─────────────────────────────────────────────────────────────

    async function connect(id: string) {
        await connectProjector(id)
        await refreshProjector(id)
    }

    async function disconnect(id: string) {
        await disconnectProjector(id)
        await refreshProjector(id)
    }

    // ─── LED 控制 ─────────────────────────────────────────────────────────────

    async function ledOn(id: string): Promise<boolean> {
        const ok = await projectorLedOn(id)
        if (ok) await refreshProjector(id)
        return ok
    }

    async function ledOff(id: string): Promise<boolean> {
        const ok = await projectorLedOff(id)
        if (ok) await refreshProjector(id)
        return ok
    }

    async function setLight(dto: SetProjectorLightDto): Promise<boolean> {
        const ok = await setProjectorLight(dto)
        if (ok) await refreshProjector(dto.projectorDeviceId)
        return ok
    }

    // ─── 显示控制 ─────────────────────────────────────────────────────────────

    async function setDisplayMode(dto: SetProjectorDisplayModeDto): Promise<boolean> {
        const ok = await setProjectorDisplayMode(dto)
        if (ok) await refreshProjector(dto.projectorDeviceId)
        return ok
    }

    async function setColor(dto: SetProjectorColorDto): Promise<boolean> {
        const ok = await setProjectorColor(dto)
        if (ok) await refreshProjector(dto.projectorDeviceId)
        return ok
    }

    async function setFlip(dto: SetProjectorFlipDto): Promise<boolean> {
        const ok = await setProjectorFlip(dto)
        if (ok) await refreshProjector(dto.projectorDeviceId)
        return ok
    }

    async function setTriggerMode(dto: SetProjectorTriggerModeDto): Promise<boolean> {
        const ok = await setProjectorTriggerMode(dto)
        if (ok) await refreshProjector(dto.projectorDeviceId)
        return ok
    }

    async function setBootImage(dto: SetProjectorBootImageDto): Promise<boolean> {
        const ok = await setProjectorBootImage(dto)
        if (ok) await refreshProjector(dto.projectorDeviceId)
        return ok
    }

    async function setCheckerboard(dto: SetProjectorCheckerboardDto): Promise<boolean> {
        const ok = await setProjectorCheckerboard(dto)
        if (ok) await refreshProjector(dto.projectorDeviceId)
        return ok
    }

    async function setRgb(dto: SetProjectorRgbDto): Promise<boolean> {
        const ok = await setProjectorRgb(dto)
        if (ok) await refreshProjector(dto.projectorDeviceId)
        return ok
    }

    // ─── 触发 ─────────────────────────────────────────────────────────────────

    async function triggerOnce(dto: TriggerProjectorDto): Promise<boolean> {
        return await triggerProjectorOnce(dto)
    }

    async function nextFrame(dto: NextProjectorFrameDto): Promise<boolean> {
        return await nextFrameProjector(dto)
    }

    // ─── 高级 ─────────────────────────────────────────────────────────────────

    async function softReset(id: string): Promise<boolean> {
        return await projectorSoftReset(id)
    }

    async function saveParams(id: string): Promise<boolean> {
        return await projectorSaveParams(id)
    }

    async function readRegister(id: string, address: number): Promise<string | null> {
        return await readProjectorRegister(id, address)
    }

    async function writeRegister(dto: WriteProjectorRegisterDto): Promise<boolean> {
        return await writeProjectorRegister(dto)
    }

    return {
        // 状态
        projectors,
        selectedProjector,
        loading,
        hubConnected,
        // Hub
        startHub,
        stopHub,
        // 查询
        fetchList,
        refreshProjector,
        selectProjector,
        // CRUD
        scan,
        update,
        // 连接
        connect,
        disconnect,
        // LED
        ledOn,
        ledOff,
        setLight,
        // 显示
        setDisplayMode,
        setColor,
        setFlip,
        setTriggerMode,
        setBootImage,
        setCheckerboard,
        setRgb,
        // 触发
        triggerOnce,
        nextFrame,
        // 高级
        softReset,
        saveParams,
        readRegister,
        writeRegister,
    }
})
