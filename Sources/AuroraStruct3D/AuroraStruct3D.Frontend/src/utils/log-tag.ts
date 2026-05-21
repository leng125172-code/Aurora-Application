/**
 * 日志标签颜色映射
 *
 * 后端各设备模块在日志消息首部附加 [Tag] 前缀，
 * 前端根据此映射将其渲染为彩色徽章。
 */

export interface LogTagMeta {
    /** 标签文字，如 "Servos" */
    readonly label: string
    /** 前景色（十六进制） */
    readonly color: string
}

/** 标签前缀 → 颜色映射 */
export const LOG_TAG_COLORS: ReadonlyMap<string, string> = new Map([
    ['[Servos]', '#F76B73'],
    ['[Serial port]', '#6E7480'],
    ['[Servo drive]', '#F76B73'],
    ['[Cameras]', '#57CC99'],
    ['[Projector]', '#3A86FF'],
])

/**
 * 从日志消息首部提取 [Tag] 前缀。
 * 若消息以已知标签开头，返回 { tag, rest }；否则返回 null。
 */
export function extractLogTag(message: string): { tag: string; color: string; rest: string } | null {
    for (const [tag, color] of LOG_TAG_COLORS) {
        if (message.startsWith(tag)) {
            return { tag, color, rest: message.slice(tag.length).trimStart() }
        }
    }
    return null
}

/**
 * 获取指定标签对应的颜色，找不到时返回默认灰色。
 */
export function getTagColor(tag: string): string {
    return LOG_TAG_COLORS.get(tag) ?? '#6E7480'
}
