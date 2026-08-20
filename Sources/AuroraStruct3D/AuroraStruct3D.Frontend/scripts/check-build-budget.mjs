import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { gzipSync } from 'node:zlib'

const outputDirectory = resolve(process.argv[2] ?? 'dist')
const html = readFileSync(resolve(outputDirectory, 'index.html'), 'utf8')
const references = new Set(
    [...html.matchAll(/(?:src|href)="([^"]+\.js)"/g)].map((match) => match[1].replace(/^\//, '')),
)
const files = [...references].map((relativePath) => {
    const bytes = readFileSync(resolve(outputDirectory, relativePath))
    return { relativePath, gzipBytes: gzipSync(bytes).byteLength }
})
const totalGzipBytes = files.reduce((sum, file) => sum + file.gzipBytes, 0)
const budgetBytes = 700 * 1024

console.log(`Initial JavaScript: ${(totalGzipBytes / 1024).toFixed(1)} KiB gzip / 700.0 KiB budget`)
for (const file of files.sort((a, b) => b.gzipBytes - a.gzipBytes)) {
    console.log(`  ${(file.gzipBytes / 1024).toFixed(1).padStart(7)} KiB  ${file.relativePath}`)
}

if (totalGzipBytes > budgetBytes) {
    console.error('Initial JavaScript performance budget exceeded.')
    process.exitCode = 1
}
