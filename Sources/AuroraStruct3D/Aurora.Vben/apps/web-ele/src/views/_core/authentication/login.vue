<script lang="ts" setup>
import type { VbenFormSchema } from '@vben/common-ui';

import { computed, onBeforeMount, ref } from 'vue';

import { AuthenticationLogin, z } from '@vben/common-ui';
import { $t } from '@vben/locales';
import { useUserStore } from '@vben/stores';

import { getApiAppAbpProBasicApplicationConfiguration } from '#/api-client/index';
import { useAuthStore } from '#/store';

defineOptions({ name: 'Login' });

const authStore = useAuthStore();
const showThirdPartyLogin = ref(false);
const thirdPartLoginList = ref([]);
const showTenant = ref(false);
const formSchema = computed((): VbenFormSchema[] => {
  return [
    {
      component: 'VbenInput',
      componentProps: {
        placeholder: $t('abp.login.selectTenant'),
      },
      fieldName: 'tenant',
      label: $t('abp.tenant.tenant'),
    },
    {
      component: 'VbenInput',
      componentProps: {
        placeholder: $t('authentication.usernameTip'),
      },

      fieldName: 'name',
      label: $t('authentication.username'),
      rules: z
        .string()
        .min(1, { message: $t('authentication.usernameTip') })
        .default('admin'),
    },

    {
      component: 'VbenInputPassword',
      componentProps: {
        placeholder: $t('authentication.password'),
      },
      fieldName: 'password',
      label: $t('authentication.password'),
      rules: z
        .string()
        .min(1, { message: $t('authentication.passwordTip') })
        .default('1q2w3E*'),
    },
    {
      component: 'VbenInput',
      componentProps: {
        placeholder: $t('abp.login.inputCode'),
      },
      fieldName: 'code',
      label: $t('abp.user.code'),
    },
  ];
});

onBeforeMount(async () => {
  const result = await getApiAppAbpProBasicApplicationConfiguration();
  showThirdPartyLogin.value = result.data?.oidcConfiguration
    ?.enabled as boolean;
  thirdPartLoginList.value = result.data?.oidcConfiguration
    ?.oidcConfiguration as [];
  showTenant.value = result.data?.multiTenancy?.isEnabled ?? false;
  const userStore = useUserStore();
  userStore.setAbpProApplicationConfiguration({
    encryptionEnabled: result.data?.encryption?.enabled as boolean,
    encryptionPublicKey: result.data?.encryption?.publicKey as string,
  });
});
</script>

<template>
  <AuthenticationLogin
    :form-schema="formSchema"
    :loading="authStore.loginLoading"
    :show-tenant-login="showTenant"
    :show-third-party-login="showThirdPartyLogin"
    :third-part-login-list="thirdPartLoginList as any"
    @submit="authStore.authLogin"
  />
</template>
