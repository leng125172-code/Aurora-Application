import { defineConfig } from '@playwright/test'

const baseURL = process.env.AURORA_FRONTEND_URL ?? 'http://127.0.0.1:5173'

export default defineConfig({
    testDir: './tests/visual',
    globalSetup: './tests/visual/global-setup.ts',
    outputDir: './test-results/visual',
    snapshotPathTemplate: '{testDir}/__screenshots__/{projectName}/{arg}{ext}',
    timeout: 45_000,
    expect: { timeout: 10_000, toHaveScreenshot: { animations: 'disabled', maxDiffPixelRatio: 0.015 } },
    fullyParallel: false,
    workers: 1,
    reporter: [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
    use: {
        baseURL,
        storageState: './tests/visual/.auth/admin.json',
        channel: 'chrome',
        colorScheme: 'light',
        reducedMotion: 'reduce',
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure',
    },
    projects: [
        { name: 'desktop-1920', use: { viewport: { width: 1920, height: 1080 } } },
        { name: 'desktop-1366', use: { viewport: { width: 1366, height: 768 } } },
        { name: 'tablet-768', use: { viewport: { width: 768, height: 1024 } } },
        { name: 'mobile-390', use: { viewport: { width: 390, height: 844 } } },
    ],
})
