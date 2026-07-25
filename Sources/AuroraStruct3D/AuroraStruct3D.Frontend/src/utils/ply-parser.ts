export interface PlyHeader {
    format: string
    vertexCount: number
    hasColor: boolean
    propertyTypes: string[]
}

export interface PlyPoint {
    x: number
    y: number
    z: number
    r?: number
    g?: number
    b?: number
}

export function parsePlyHeader(data: Uint8Array): PlyHeader {
    const headerEnd = findHeaderEnd(data)
    if (headerEnd === -1) {
        throw new Error('PLY header not found')
    }

    const headerStr = new TextDecoder().decode(data.slice(0, headerEnd))
    const lines = headerStr.split('\n').filter((l) => l.trim())

    let format = ''
    let vertexCount = 0
    let hasColor = false
    const propertyTypes: string[] = []

    for (const line of lines) {
        const parts = line.trim().split(/\s+/)
        if (parts[0] === 'format') {
            format = parts[1]
        } else if (parts[0] === 'element' && parts[1] === 'vertex') {
            vertexCount = parseInt(parts[2], 10)
        } else if (parts[0] === 'property') {
            propertyTypes.push(parts[1])
            if (parts[2] === 'red' || parts[2] === 'green' || parts[2] === 'blue') {
                hasColor = true
            }
        }
    }

    return { format, vertexCount, hasColor, propertyTypes }
}

export function parsePlyData(data: Uint8Array, header: PlyHeader): Float32Array {
    const headerEnd = findHeaderEnd(data)
    const binaryData = data.slice(headerEnd)

    const isBinary = header.format.startsWith('binary')
    const isBigEndian = header.format.includes('big_endian')

    const stride = header.hasColor ? 6 : 3
    const result = new Float32Array(header.vertexCount * stride)

    if (isBinary) {
        const view = new DataView(binaryData.buffer, binaryData.byteOffset, binaryData.byteLength)
        let offset = 0

        for (let i = 0; i < header.vertexCount; i++) {
            const baseIdx = i * stride

            if (header.propertyTypes[0] === 'float' || header.propertyTypes[0] === 'float32') {
                result[baseIdx] = view.getFloat32(offset, !isBigEndian)
                offset += 4
                result[baseIdx + 1] = view.getFloat32(offset, !isBigEndian)
                offset += 4
                result[baseIdx + 2] = view.getFloat32(offset, !isBigEndian)
                offset += 4
            } else {
                result[baseIdx] = view.getFloat64(offset, !isBigEndian)
                offset += 8
                result[baseIdx + 1] = view.getFloat64(offset, !isBigEndian)
                offset += 8
                result[baseIdx + 2] = view.getFloat64(offset, !isBigEndian)
                offset += 8
            }

            if (header.hasColor) {
                const r = view.getUint8(offset++)
                const g = view.getUint8(offset++)
                const b = view.getUint8(offset++)
                result[baseIdx + 3] = r / 255
                result[baseIdx + 4] = g / 255
                result[baseIdx + 5] = b / 255
            }
        }
    } else {
        const dataStr = new TextDecoder().decode(binaryData)
        const lines = dataStr.split('\n').filter((l) => l.trim())

        for (let i = 0; i < Math.min(lines.length, header.vertexCount); i++) {
            const parts = lines[i].trim().split(/\s+/)
            const baseIdx = i * stride

            result[baseIdx] = parseFloat(parts[0])
            result[baseIdx + 1] = parseFloat(parts[1])
            result[baseIdx + 2] = parseFloat(parts[2])

            if (header.hasColor && parts.length >= 6) {
                result[baseIdx + 3] = parseInt(parts[3], 10) / 255
                result[baseIdx + 4] = parseInt(parts[4], 10) / 255
                result[baseIdx + 5] = parseInt(parts[5], 10) / 255
            }
        }
    }

    return result
}

function findHeaderEnd(data: Uint8Array): number {
    const headerStr = new TextDecoder().decode(data)
    const endHeaderIndex = headerStr.indexOf('end_header')
    if (endHeaderIndex === -1) {
        return -1
    }
    const afterEndHeader = headerStr.indexOf('\n', endHeaderIndex + 'end_header'.length)
    if (afterEndHeader === -1) {
        return endHeaderIndex + 'end_header'.length
    }
    return afterEndHeader + 1
}