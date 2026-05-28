/**
 * useDeviceSession Composable
 *
 * 封装设备独占会话的常用操作，供各设备页面组件使用。
 * 提供：
 *  - 占用状态查询（isOccupied / isOccupiedByMe / occupant）
 *  - 挂载时自动获取（acquireOnMount）
 *  - 卸载时自动释放（releaseOnUnmount）
 *  - 强制接管（forceAcquire）
 */
import { computed, onMounted, onUnmounted, type ComputedRef } from 'vue'
import { useDeviceSessionStore } from '@/stores/deviceSession'
import { type DeviceSessionDto, DeviceType } from '@/api/device-sessions'

export interface UseDeviceSessionReturn {
    /** 设备是否已被任何人占用 */
    isOccupied: ComputedRef<boolean>
    /** 设备是否被当前标签页占用 */
    isOccupiedByMe: ComputedRef<boolean>
    /** 当前占用者信息（未被占用时为 undefined） */
    occupant: ComputedRef<DeviceSessionDto | undefined>
    /** 强制接管设备独占权 */
    forceAcquire: () => Promise<void>
    /** 手动释放设备独占权 */
    release: () => Promise<void>
}

/**
 * @param deviceId 设备 ID（字符串 UUID）
 * @param deviceType 设备类型
 * @param options.autoAcquireOnMount 是否在组件挂载时自动获取独占权（默认 true）
 * @param options.autoReleaseOnUnmount 是否在组件卸载时自动释放独占权（默认 true）
 * @param options.onOccupied 获取独占权被拒时的回调（可选，用于弹出提示）
 */
export function useDeviceSession(
    deviceId: string,
    deviceType: DeviceType,
    options: {
        autoAcquireOnMount?: boolean
        autoReleaseOnUnmount?: boolean
        onOccupied?: (occupant: DeviceSessionDto) => void
    } = {}
): UseDeviceSessionReturn {
    const { autoAcquireOnMount = true, autoReleaseOnUnmount = true, onOccupied } = options
    const store = useDeviceSessionStore()

    const isOccupied = computed(() => store.isOccupied(deviceId))
    const isOccupiedByMe = computed(() => store.isOccupiedByMe(deviceId))
    const occupant = computed(() => store.getOccupant(deviceId))

    async function tryAcquire(): Promise<void> {
        try {
            await store.acquire(deviceId, deviceType)
        } catch (err: unknown) {
            // HTTP 409：设备被他人占用
            const currentOccupant = store.getOccupant(deviceId)
            if (currentOccupant && onOccupied) {
                onOccupied(currentOccupant)
            }
        }
    }

    async function forceAcquire(): Promise<void> {
        await store.forceAcquire(deviceId, deviceType)
    }

    async function release(): Promise<void> {
        await store.release(deviceId)
    }

    if (autoAcquireOnMount) {
        onMounted(() => void tryAcquire())
    }

    if (autoReleaseOnUnmount) {
        onUnmounted(() => void release())
    }

    return { isOccupied, isOccupiedByMe, occupant, forceAcquire, release }
}
