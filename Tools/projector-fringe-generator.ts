/**
 * 1280×720 固定多尺度二值条纹生成脚本，可直接交给前端使用。
 *
 * 帧序：
 * - 0~9：横条纹，宽度 3、6、12、24、48 px，每种原图 + 互补图
 * - 10~19：竖条纹，宽度 4、8、16、32、64 px，每种原图 + 互补图
 * - 互补图直接逐像素取反，不做偏移
 * - 投影仪方向指令：MD 10
 */

export const PROJECTOR_FRINGE_CONFIG = {
    width: 1280,
    height: 720,
    horizontalStripeWidths: [3, 6, 12, 24, 48],
    verticalStripeWidths: [4, 8, 16, 32, 64],
    horizontalFrameCount: 10,
    totalFrameCount: 20,
    mdCommand: 'MD 10\r\n',
} as const

export type ProjectorFringeOrientation = 'horizontal' | 'vertical'

export interface ProjectorFringeFrame {
    index: number
    orientation: ProjectorFringeOrientation
    stripeWidth: number
    complementary: boolean
    label: string
    /** 横条纹长度 720，竖条纹长度 1280。 */
    pixels: Uint8Array
}

export function generateProjectorFringeFrames(): ProjectorFringeFrame[] {
    const frames: ProjectorFringeFrame[] = []
    appendFrames(
        frames,
        'horizontal',
        PROJECTOR_FRINGE_CONFIG.horizontalStripeWidths,
        PROJECTOR_FRINGE_CONFIG.height
    )
    appendFrames(
        frames,
        'vertical',
        PROJECTOR_FRINGE_CONFIG.verticalStripeWidths,
        PROJECTOR_FRINGE_CONFIG.width
    )
    return frames
}

function appendFrames(
    frames: ProjectorFringeFrame[],
    orientation: ProjectorFringeOrientation,
    stripeWidths: readonly number[],
    coordinateCount: number
): void {
    for (const stripeWidth of stripeWidths) {
        const original = new Uint8Array(coordinateCount)
        const inverse = new Uint8Array(coordinateCount)
        for (let coordinate = 0; coordinate < coordinateCount; coordinate++) {
            original[coordinate] = Math.floor(coordinate / stripeWidth) % 2 === 0 ? 0 : 255
            inverse[coordinate] = 255 - original[coordinate]
        }

        const prefix = orientation === 'horizontal' ? '横条纹' : '竖条纹'
        frames.push({
            index: frames.length,
            orientation,
            stripeWidth,
            complementary: false,
            label: `${prefix} ${stripeWidth}px 原图`,
            pixels: original,
        })
        frames.push({
            index: frames.length,
            orientation,
            stripeWidth,
            complementary: true,
            label: `${prefix} ${stripeWidth}px 互补`,
            pixels: inverse,
        })
    }
}

export function renderProjectorFringeFrame(
    canvas: HTMLCanvasElement,
    frame: ProjectorFringeFrame
): void {
    const { width, height } = PROJECTOR_FRINGE_CONFIG
    canvas.width = width
    canvas.height = height
    const context = canvas.getContext('2d')
    if (!context) throw new Error('无法获取 Canvas 2D context')

    const imageData = context.createImageData(width, height)
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const gray = frame.orientation === 'horizontal' ? frame.pixels[y] : frame.pixels[x]
            const offset = (y * width + x) * 4
            imageData.data[offset] = gray
            imageData.data[offset + 1] = gray
            imageData.data[offset + 2] = gray
            imageData.data[offset + 3] = 255
        }
    }
    context.putImageData(imageData, 0, 0)
}

export function downloadProjectorFringePng(frame: ProjectorFringeFrame): void {
    const canvas = document.createElement('canvas')
    renderProjectorFringeFrame(canvas, frame)
    const anchor = document.createElement('a')
    anchor.download =
        `${String(frame.index).padStart(2, '0')}_`
        + `${frame.orientation === 'horizontal' ? 'H' : 'V'}_`
        + `${frame.stripeWidth}px_${frame.complementary ? 'complement' : 'original'}.png`
    anchor.href = canvas.toDataURL('image/png')
    anchor.click()
}

export function downloadAllProjectorFringePng(): void {
    for (const frame of generateProjectorFringeFrames()) {
        downloadProjectorFringePng(frame)
    }
}
