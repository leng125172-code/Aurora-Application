import { defineStore } from 'pinia'
import { ref } from 'vue'
import {
    type ConnectSerialPortDto,
    type GetSerialPortListDto,
    type SerialPortConfigDto,
    type SerialPortRawResponseDto,
    type SerialPortRawSendDto,
    type SerialPortScanResultDto,
    type UpdateSerialPortConfigDto,
    connectSerialPort,
    disconnectSerialPort,
    getSerialPort,
    getSerialPortList,
    scanSystemPorts,
    sendSerialPortRaw,
    updateSerialPort,
} from '@/api/serial-ports'

export const useSerialPortStore = defineStore('serialPort', () => {
    const ports = ref<SerialPortConfigDto[]>([])
    const selectedPort = ref<SerialPortConfigDto | null>(null)
    const loading = ref(false)

    function applyUpdate(updated: SerialPortConfigDto) {
        const index = ports.value.findIndex((port) => port.id === updated.id)
        if (index >= 0) {
            ports.value[index] = updated
        } else {
            ports.value.push(updated)
        }
        if (selectedPort.value?.id === updated.id) {
            selectedPort.value = updated
        }
    }

    async function fetchList(params: GetSerialPortListDto = {}) {
        loading.value = true
        try {
            const result = await getSerialPortList({ maxResultCount: 100, ...params })
            ports.value = [...result.items]
        } finally {
            loading.value = false
        }
    }

    async function refresh(id: string): Promise<SerialPortConfigDto> {
        const updated = await getSerialPort(id)
        applyUpdate(updated)
        return updated
    }

    function select(id: string) {
        selectedPort.value = ports.value.find((port) => port.id === id) ?? null
    }

    async function scan(): Promise<SerialPortScanResultDto> {
        const result = await scanSystemPorts()
        ports.value = [...result.items]
        return result
    }

    async function update(id: string, dto: UpdateSerialPortConfigDto): Promise<SerialPortConfigDto> {
        const updated = await updateSerialPort(id, dto)
        applyUpdate(updated)
        return updated
    }

    async function connect(id: string, dto: ConnectSerialPortDto): Promise<SerialPortConfigDto> {
        const updated = await connectSerialPort(id, dto)
        applyUpdate(updated)
        return updated
    }

    async function disconnect(id: string): Promise<SerialPortConfigDto> {
        const updated = await disconnectSerialPort(id)
        applyUpdate(updated)
        return updated
    }

    async function sendRaw(id: string, dto: SerialPortRawSendDto): Promise<SerialPortRawResponseDto> {
        return await sendSerialPortRaw(id, dto)
    }

    return {
        ports,
        selectedPort,
        loading,
        fetchList,
        refresh,
        select,
        scan,
        update,
        connect,
        disconnect,
        sendRaw,
    }
})
