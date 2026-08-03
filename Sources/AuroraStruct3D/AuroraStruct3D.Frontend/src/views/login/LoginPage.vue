<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import { SparklesText } from '@/components/ui/sparkles-text'
import { AppCard } from '@/components/primevue'
import LoginTopBar from '@/layouts/LoginTopBar.vue'
import { FlickeringGrid } from '@/components/ui/flickering-grid'
import { loginAsync } from '@/api/auth'
import { getApplicationConfigurationAsync } from '@/api/abp-application'
import { useAuthStore } from '@/stores/auth'
import { useAppToast } from '@/composables/useAppToast'

const { t } = useI18n()
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const toast = useAppToast()

const username = ref('')
const password = ref('')
const tenantName = ref('')
const submitting = ref(false)
const usernameError = ref('')
const passwordError = ref('')

async function handleSubmit(): Promise<void> {
    if (submitting.value) return
    usernameError.value = ''
    passwordError.value = ''
    if (!username.value) {
        usernameError.value = t('login.usernameRequired')
        toast.warn(usernameError.value)
        return
    }
    if (!password.value) {
        passwordError.value = t('login.passwordRequired')
        toast.warn(passwordError.value)
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
    <!-- 顶部导航栏（含设备状态徽章、故障横幅、语言/主题切换） -->
    <div class="absolute inset-x-0 top-0 z-[2]">
        <LoginTopBar />
    </div>

    <!-- FlickeringGrid 全屏背景 -->
    <FlickeringGrid
        class="absolute inset-0 z-[0]"
        color="#6366f1"
        :square-size="4"
        :grid-gap="6"
        :flicker-chance="0.2"
        :max-opacity="0.15"
    />

    <!-- 登录卡片 -->
    <div class="relative z-[1] flex h-full w-full flex-col items-center justify-center gap-8 px-4">
        <!-- 顶部 Glitch 大标题（保留 Inspira UI 视觉效果） -->
        <SparklesText :text="t('login.title')" class="!text-4xl md:!text-5xl" />

        <AppCard class="w-full max-w-md shadow-2xl" :beam-size="160">
            <template #subtitle>
                <span class="block text-center">{{ t('login.subtitle') }}</span>
            </template>

            <template #content>
                <form id="login-form" class="space-y-4" novalidate @submit.prevent="handleSubmit">
                    <!-- 租户 -->
                    <div class="flex flex-col gap-2">
                        <label for="login-tenant" class="text-sm font-medium">{{ t('login.tenant') }}</label>
                        <InputText
                            id="login-tenant"
                            v-model="tenantName"
                            size="small"
                            :placeholder="t('login.tenantPlaceholder')"
                            autocomplete="organization"
                            class="w-full"
                        />
                    </div>
                    <!-- 用户名 -->
                    <div class="flex flex-col gap-2">
                        <label for="login-username" class="text-sm font-medium">{{ t('login.username') }}</label>
                        <InputText
                            id="login-username"
                            v-model="username"
                            size="small"
                            autocomplete="username"
                            class="w-full"
                            autofocus
                            :invalid="!!usernameError"
                            :aria-invalid="!!usernameError"
                            aria-describedby="login-username-error"
                            @update:model-value="usernameError = ''"
                        />
                        <small v-if="usernameError" id="login-username-error" class="text-xs text-destructive" role="alert">{{ usernameError }}</small>
                    </div>
                    <!-- 密码 -->
                    <div class="flex flex-col gap-2">
                        <label for="login-password" class="text-sm font-medium">{{ t('login.password') }}</label>
                        <Password
                            input-id="login-password"
                            v-model="password"
                            size="small"
                            :feedback="false"
                            :prompt-label="t('login.password')"
                            toggle-mask
                            autocomplete="current-password"
                            input-class="w-full"
                            class="w-full"
                            :invalid="!!passwordError"
                            :input-props="{ 'aria-invalid': !!passwordError, 'aria-describedby': 'login-password-error' }"
                            @update:model-value="passwordError = ''"
                        />
                        <small v-if="passwordError" id="login-password-error" class="text-xs text-destructive" role="alert">{{ passwordError }}</small>
                    </div>
                </form>
            </template>

            <template #footer>
                <Button
                    type="submit"
                    form="login-form"
                    class="w-full"
                    :label="submitting ? t('login.signingIn') : t('login.signIn')"
                    size="small"
                    :loading="submitting"
                    :disabled="submitting"
                />
            </template>
        </AppCard>
    </div>
</template>
