import { expect, test } from '@playwright/test'

const routes = [
    '/dashboard', '/projects', '/embed/swagger', '/embed/cap', '/embed/hangfire', '/embed/profiler',
    '/system-info', '/workflow-ide', '/device-state/faults', '/device-state/logs', '/projectors',
    '/projectors/logs', '/cameras', '/cameras/logs', '/serial-ports', '/serial-ports/logs', '/motors',
    '/motors/logs', '/plcs', '/product-models', '/product-models/logs', '/ai-models', '/ai-models/logs',
    '/calibration/projects',
] as const

const screenshotRoutes = new Set(['/dashboard', '/projects', '/system-info', '/device-state/faults', '/cameras', '/ai-models', '/workflow-ide'])
const variants = [
    { locale: 'zh-CN', theme: 'light' },
    { locale: 'zh-CN', theme: 'dark' },
    { locale: 'en-US', theme: 'light' },
    { locale: 'en-US', theme: 'dark' },
] as const

for (const variant of variants) {
    test.describe(`${variant.locale}-${variant.theme}`, () => {
        test.use({ colorScheme: variant.theme })

        for (const route of routes) {
            test(`${route} has a stable responsive layout`, async ({ page }, testInfo) => {
                await page.addInitScript(({ locale, theme }) => {
                    localStorage.setItem('aurora.culture', locale)
                    localStorage.setItem('aurora.theme', theme)
                }, variant)
                const consoleErrors: string[] = []
                page.on('console', (message) => {
                    if (message.type() === 'error') consoleErrors.push(message.text())
                })

                await page.goto(route, { waitUntil: 'domcontentloaded' })
                await expect(page.getByTestId('app-shell')).toBeVisible()
                await expect(page.getByTestId('page-content')).toBeVisible()

                const layout = await page.evaluate(() => ({
                    viewportWidth: document.documentElement.clientWidth,
                    documentWidth: document.documentElement.scrollWidth,
                    contentWidth: document.querySelector<HTMLElement>('[data-testid="page-content"]')?.scrollWidth ?? 0,
                    contentClientWidth: document.querySelector<HTMLElement>('[data-testid="page-content"]')?.clientWidth ?? 0,
                }))
                expect(layout.documentWidth, 'The application shell must not overflow the viewport').toBeLessThanOrEqual(layout.viewportWidth + 1)
                expect(layout.contentClientWidth, 'The routed content must have a measurable width').toBeGreaterThan(0)
                expect(consoleErrors, 'Page emitted console errors').toEqual([])

                if (screenshotRoutes.has(route)) {
                    await expect(page).toHaveScreenshot(`${variant.locale}-${variant.theme}-${route.replaceAll('/', '_') || 'root'}.png`, {
                        fullPage: false,
                        mask: [page.locator('canvas'), page.locator('video'), page.locator('[data-visual-dynamic]')],
                    })
                }

                await testInfo.attach('layout-metrics', { body: JSON.stringify(layout, null, 2), contentType: 'application/json' })
            })
        }
    })
}
