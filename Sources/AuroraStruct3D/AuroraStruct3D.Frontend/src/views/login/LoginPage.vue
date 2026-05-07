<script setup lang="ts">
    import { ref } from 'vue'
    import { useRoute, useRouter } from 'vue-router'
    import { useI18n } from 'vue-i18n'
    import { toast } from 'vue-sonner'
    import { Button } from '@/components/ui/button'
    import { Input } from '@/components/ui/input'
    import { Label } from '@/components/ui/label'
    import {
        Card,
        CardHeader,
        CardDescription,
        CardContent,
        CardFooter,
    } from '@/components/ui/card'
    import { AuroraBackground } from '@/components/ui/aurora-background'
    import { BorderBeam } from '@/components/ui/border-beam'
    import { TextGlitch } from '@/components/ui/text-glitch'
    import ThemeToggle from '@/components/ThemeToggle.vue'
    import LangSwitcher from '@/components/LangSwitcher.vue'
    import { loginAsync } from '@/api/auth'
    import { getApplicationConfigurationAsync } from '@/api/abp-application'
    import { useAuthStore } from '@/stores/auth'

    const { t } = useI18n()
    const router = useRouter()
    const route = useRoute()
    const auth = useAuthStore()

    const username = ref('')
    const password = ref('')
    const tenantName = ref('')
    const submitting = ref(false)

    async function handleSubmit(): Promise<void> {
        if (!username.value) {
            toast.warning(t('login.usernameRequired'))
            return
        }
        if (!password.value) {
            toast.warning(t('login.passwordRequired'))
            return
        }

        submitting.value = true
        try {
            auth.setTenantId(tenantName.value || null)

            const tokenResp = await loginAsync({
                username: username.value,
                password: password.value,
                tenantId: tenantName.value || null,
            })
            auth.setToken(tokenResp.token)

            const cfg = await getApplicationConfigurationAsync()
            if (cfg.currentUser.isAuthenticated) {
                auth.setCurrentUser({
                    id: cfg.currentUser.id ?? '',
                    userName: cfg.currentUser.userName ?? username.value,
                    email: cfg.currentUser.email,
                    tenantId: cfg.currentUser.tenantId,
                    roles: cfg.currentUser.roles ?? [],
                })
            }

            toast.success(t('login.loginSuccess'))
            const redirect = (route.query.redirect as string) || '/'
            await router.push(redirect)
        } catch {
            // axios 拦截器已处理
        } finally {
            submitting.value = false
        }
    }
</script>

<template>
    <AuroraBackground class="min-h-screen w-full">
        <!-- 主题/语言切换按钮 -->
        <div class="absolute right-4 top-4 z-[2] flex items-center gap-2">
            <LangSwitcher />
            <ThemeToggle />
        </div>

        <!-- 登录卡片：relative z-[1] 确保内容在极光层之上 -->
        <div class="relative z-[1] flex w-full flex-col items-center justify-center gap-8 px-4">
            <!-- 顶部 Glitch 大标题（官方 banner 风格：黑底白字 + RGB 偏移） -->
            <TextGlitch
                :text="t('login.title')"
                :speed="1"
                :enable-shadows="true"
                class="!text-4xl md:!text-5xl"
            />

            <Card class="w-full max-w-md shadow-2xl backdrop-blur-sm bg-background/80">
                <BorderBeam :size="160" :duration="10" />
                <CardHeader class="space-y-2 text-center">
                    <CardDescription>{{ t('login.subtitle') }}</CardDescription>
                </CardHeader>
                <CardContent class="space-y-4">
                    <div class="space-y-2">
                        <Label for="login-tenant">{{ t('login.tenant') }}</Label>
                        <Input id="login-tenant"
                               v-model="tenantName"
                               :placeholder="t('login.tenantPlaceholder')"
                               autocomplete="organization" />
                    </div>
                    <div class="space-y-2">
                        <Label for="login-username">{{ t('login.username') }}</Label>
                        <Input id="login-username"
                               v-model="username"
                               autocomplete="username"
                               @keyup.enter="handleSubmit" />
                    </div>
                    <div class="space-y-2">
                        <Label for="login-password">{{ t('login.password') }}</Label>
                        <Input id="login-password"
                               v-model="password"
                               type="password"
                               autocomplete="current-password"
                               @keyup.enter="handleSubmit" />
                    </div>
                </CardContent>
                <CardFooter>
                    <Button class="w-full" :disabled="submitting" @click="handleSubmit">
                        {{ submitting ? t('login.signingIn') : t('login.signIn') }}
                    </Button>
                </CardFooter>
            </Card>
        </div>
    </AuroraBackground>
</template>
