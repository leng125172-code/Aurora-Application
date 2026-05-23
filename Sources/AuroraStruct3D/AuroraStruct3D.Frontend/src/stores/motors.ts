import { defineStore } from 'pinia'
import { ref } from 'vue'
import {
    type GetMotorAxisListDto,
    type MotorAxisDto,
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
        const result = await scanMotorDevices(input)
        await fetchList({ refreshHardware: false })
        return result
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
