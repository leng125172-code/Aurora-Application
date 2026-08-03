export interface PlcValueDetail {
    label: string
    value: string
}

export interface PlcValuePresentation {
    summary: string
    details: PlcValueDetail[]
    rawJson?: string
}

const protocolMetadataKeys = new Set([
    'typeId', 'encoding', 'binaryEncodingId', 'xmlEncodingId', 'jsonEncodingId', 'innerNodeId',
    'namespaceIndex', 'idType', 'identifier', 'namespaceUri', 'serverIndex', 'isNull',
    'isAbsolute', 'isNullNodeId',
])

const labels: Record<string, string> = {
    startTime: '启动时间', currentTime: '当前时间', state: '状态', buildInfo: '产品信息',
    productUri: '产品 URI', manufacturerName: '厂商', productName: '产品名称',
    softwareVersion: '软件版本', buildNumber: '构建编号', buildDate: '构建日期',
    secondsTillShutdown: '距关闭秒数', shutdownReason: '关闭原因',
}

const serverStates: Record<number, string> = {
    0: '运行中', 1: '故障', 2: '未配置', 3: '已暂停', 4: '正在关闭', 5: '测试中', 6: '通信故障', 7: '未知',
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function formatScalar(value: unknown, key?: string, serverStatus = false): string {
    if (value === null || value === undefined) return '—'
    if (key === 'state' && serverStatus && typeof value === 'number') return serverStates[value] ?? `未知 (${value})`
    if (typeof value === 'boolean') return value ? '是' : '否'
    if (typeof value === 'string') {
        const date = new Date(value)
        if (!Number.isNaN(date.getTime()) && /^\d{4}-\d{2}-\d{2}T/.test(value)) return date.toLocaleString()
        return value || '—'
    }
    return String(value)
}

function visibleEntries(value: Record<string, unknown>): [string, unknown][] {
    return Object.entries(value).filter(([key, item]) => !protocolMetadataKeys.has(key) && item !== null)
}

function unwrap(value: Record<string, unknown>): Record<string, unknown> {
    return isRecord(value.body) ? value.body : value
}

function flattenDetails(value: Record<string, unknown>, prefix = '', depth = 0, serverStatus = false): PlcValueDetail[] {
    if (depth > 2) return []
    return visibleEntries(value).flatMap(([key, item]) => {
        const label = prefix ? `${prefix} · ${labels[key] ?? key}` : (labels[key] ?? key)
        if (Array.isArray(item)) return [{ label, value: `${item.length} 项：${item.map((x) => formatScalar(x)).join(', ')}` }]
        if (isRecord(item)) return flattenDetails(item, label, depth + 1, serverStatus)
        return [{ label, value: formatScalar(item, key, serverStatus) }]
    })
}

export function presentPlcValue(value: unknown): PlcValuePresentation {
    if (value === null || value === undefined) return { summary: '—', details: [] }
    if (Array.isArray(value)) {
        return { summary: `${value.length} 项`, details: value.map((item, index) => ({ label: `[${index}]`, value: formatScalar(item) })), rawJson: JSON.stringify(value, null, 2) }
    }
    if (!isRecord(value)) return { summary: formatScalar(value), details: [] }

    const payload = unwrap(value)
    const isServerStatus = 'startTime' in payload && 'buildInfo' in payload
    const details = flattenDetails(payload, '', 0, isServerStatus)
    const product = isRecord(payload.buildInfo) ? payload.buildInfo.productName : undefined
    const state = isServerStatus ? formatScalar(payload.state, 'state', true) : undefined
    return {
        summary: [product, state].filter((item): item is string => typeof item === 'string' && item.length > 0).join(' · ') || '复杂数据',
        details,
        rawJson: JSON.stringify(value, null, 2),
    }
}
