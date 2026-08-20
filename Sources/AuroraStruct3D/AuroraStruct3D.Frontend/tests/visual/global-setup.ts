import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { request, type FullConfig } from '@playwright/test'

export default async function globalSetup(config: FullConfig): Promise<void> {
    const username = process.env.AURORA_E2E_USERNAME
    const password = process.env.AURORA_E2E_PASSWORD
    if (!username || !password) {
        throw new Error('Set AURORA_E2E_USERNAME and AURORA_E2E_PASSWORD before running visual tests.')
    }

    const baseURL = config.projects[0]?.use.baseURL as string
    const context = await request.newContext({ baseURL })
    const response = await context.post('/api/app/account/login', { data: { name: username, password } })
    if (!response.ok()) throw new Error(`Visual-test login failed: HTTP ${response.status()}`)
    const payload = (await response.json()) as { token: string }
    await context.dispose()

    const authDir = path.resolve('tests/visual/.auth')
    await mkdir(authDir, { recursive: true })
    await writeFile(
        path.join(authDir, 'admin.json'),
        JSON.stringify({
            cookies: [],
            origins: [{ origin: new URL(baseURL).origin, localStorage: [{ name: 'aurora.token', value: payload.token }] }],
        })
    )
}
