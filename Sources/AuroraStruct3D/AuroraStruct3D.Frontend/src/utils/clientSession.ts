/**
 * 客户端会话 ID 工具
 *
 * 为每个浏览器标签页生成唯一的 per-tab UUID，存储于 sessionStorage。
 * 不同标签页拥有不同的 ID，即使是同一用户在同一台电脑上打开多个标签页。
 * 刷新后 ID 保持不变；关闭标签页后 ID 清除。
 *
 * 该 ID 作为设备独占会话（Soft-Exclusive Session）的 ownership key，
 * 通过 HTTP Header X-Client-Session-Id 和 SignalR Query 参数传递给后端。
 */

const SESSION_STORAGE_KEY = 'clientSessionId'

/**
 * 获取（或创建）当前标签页的唯一会话 ID。
 * 第一次调用时自动生成并持久化到 sessionStorage。
 */
export function getClientSessionId(): string {
    let id = sessionStorage.getItem(SESSION_STORAGE_KEY)
    if (!id) {
        id = generateUUID()
        sessionStorage.setItem(SESSION_STORAGE_KEY, id)
    }
    return id
}

/**
 * 生成 UUID v4，按兼容性从高到低依次降级：
 * 1. crypto.randomUUID()     —— 需要安全上下文（HTTPS/localhost），Chrome 92+
 * 2. crypto.getRandomValues() —— 兼容 HTTP，Chrome 11+，Firefox 21+
 * 3. Math.random()            —— 最终降级，随机性较弱，仅用于会话区分非安全场景
 */
function generateUUID(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID()
    }
    if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
        const bytes = new Uint8Array(16)
        crypto.getRandomValues(bytes)
        // 设置 version=4、variant=10xx
        bytes[6] = (bytes[6] & 0x0f) | 0x40
        bytes[8] = (bytes[8] & 0x3f) | 0x80
        const hex = Array.from(bytes)
            .map((b) => b.toString(16).padStart(2, '0'))
            .join('')
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-` + `${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
    }
    // 最终降级：Math.random（不依赖 Crypto API，用于 HTTP 非安全上下文）
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0
        const v = c === 'x' ? r : (r & 0x3) | 0x8
        return v.toString(16)
    })
}
