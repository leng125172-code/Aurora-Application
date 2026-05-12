<script lang="ts" setup>
import { onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';

import { LOGIN_PATH } from '@vben/constants';
import { preferences } from '@vben/preferences';
import { useAccessStore, useUserStore } from '@vben/stores';

import { notification, Spin } from 'ant-design-vue';

import { postApiAppAccountLoginOidc } from '#/api-client';
import { $t } from '#/locales';
import { useAuthStore } from '#/store';

defineOptions({ name: 'OidcLogin' });
const loading = ref(true);
const tip = ref($t('abp.login.oidcTip'));
const router = useRouter();
const { currentRoute } = useRouter();
const accessStore = useAccessStore();
const userStore = useUserStore();
const code = currentRoute.value.query.code as string;
const state = currentRoute.value.query.state as string;
const authStore = useAuthStore();
onMounted(async () => {
  try {
    // oidc登录
    const result = await postApiAppAccountLoginOidc({ body: { code, state } });
    accessStore.setAccessToken(result.data?.token as string);
    userStore.setUserInfo(result.data as any);

    await authStore.getApplicationConfiguration();
    await router.push(
      userStore.userInfo?.homePath || preferences.app.defaultHomePath,
    );
    if (result.data?.userName) {
      notification.success({
        description: `${$t('authentication.loginSuccessDesc')}:${result.data?.userName}`,
        duration: 3,
        message: $t('authentication.loginSuccess'),
      });
    }
  } catch {
    await router.push(LOGIN_PATH);
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div>
    <Spin :spinning="loading" :tip="tip" />
  </div>
</template>
