import type { Recordable, UserInfo } from '@vben/types';

import type {
  ApplicationAuthConfigurationDto,
  ApplicationConfigurationDto,
} from '#/api-client';

import { ref } from 'vue';
import { useRouter } from 'vue-router';

import { LOGIN_PATH } from '@vben/constants';
import { preferences } from '@vben/preferences';
import { resetAllStores, useAccessStore, useUserStore } from '@vben/stores';

import { message as Message, notification } from 'ant-design-vue';
import { defineStore } from 'pinia';

import {
  getApiAbpApplicationConfiguration,
  postApiAppAccountLogin,
  postApiAppAccountLogin2Fa,
  postTenantsFind,
} from '#/api-client';
import { useSignalR } from '#/hooks/useSignalR';
import { $t } from '#/locales';

export const useAuthStore = defineStore('auth', () => {
  const accessStore = useAccessStore();
  const userStore = useUserStore();
  const router = useRouter();

  const loginLoading = ref(false);

  /**
   * 异步处理登录操作
   * Asynchronously handle the login process
   * @param params 登录表单数据
   */
  async function authLogin(
    params: Recordable<any>,
    onSuccess?: () => Promise<void> | void,
  ) {
    // 异步处理用户登录操作并获取 accessToken
    let userInfo: null | UserInfo = null;

    try {
      loginLoading.value = true;
      //  判断是否租户登录
      if (params.tenant) {
        const tenantResult = await postTenantsFind({
          body: {
            name: params.tenant,
          },
        });
        if (tenantResult.data?.success) {
          userStore.setTenantInfo(tenantResult.data as any);
        } else {
          Message.error(`${params.tenant}$t('abp.tenant.notExist')`);
          userStore.setTenantInfo(null);
          delete params.tenant;
          return;
        }
      }

      const loginBody = { name: params.name, password: params.password };
      const { data = {} } = params.code
        ? await postApiAppAccountLogin2Fa({
            body: { ...loginBody, code: params.code },
          })
        : await postApiAppAccountLogin({
            body: loginBody,
          });
      // 如果成功获取到 accessToken
      if (data.token) {
        accessStore.setAccessToken(data.token);
        accessStore.setRefreshToken(data.refreshToken);
        userInfo = data as any;
        userStore.setUserInfo(userInfo as any);
        await getApplicationConfiguration();
        if (accessStore.loginExpired) {
          accessStore.setLoginExpired(false);
        } else {
          onSuccess
            ? await onSuccess?.()
            : await router.push(
                userInfo?.homePath || preferences.app.defaultHomePath,
              );
        }

        if (userInfo?.userName) {
          notification.success({
            description: `${$t('authentication.loginSuccessDesc')}:${userInfo?.userName}`,
            duration: 3,
            message: $t('authentication.loginSuccess'),
          });
        }
      }
    } catch {
      userStore.setTenantInfo(null);
      userStore.setUserInfo(null);
    } finally {
      loginLoading.value = false;
    }

    return {
      userInfo,
    };
  }

  async function logout(redirect: boolean = true) {
    try {
      // await logoutApi();
      const { closeConnect } = useSignalR();
      closeConnect();
    } catch {
      // 不做任何处理
    }
    resetAllStores();
    accessStore.setLoginExpired(false);

    // 回登录页带上当前路由地址
    await router.replace({
      path: LOGIN_PATH,
    });
  }

  async function fetchUserInfo() {
    const userInfo: null | UserInfo = null;
    // userInfo = await getUserInfoApi();
    // userStore.setUserInfo(userInfo);
    return userInfo;
  }

  function $reset() {
    loginLoading.value = false;
  }

  async function getApplicationConfiguration() {
    const { data: authData } = await getApiAbpApplicationConfiguration({
      query: { IncludeLocalizationResources: false },
    });
    const { auth, currentUser } = authData as ApplicationConfigurationDto;
    if (!currentUser?.isAuthenticated) {
      await logout(true);
    }
    const accessCodes = Object.keys(
      (auth as ApplicationAuthConfigurationDto)
        .grantedPolicies as unknown as Record<string, any>,
    );
    accessStore.setAccessCodes(accessCodes);
  }
  return {
    $reset,
    authLogin,
    fetchUserInfo,
    loginLoading,
    logout,
    getApplicationConfiguration,
  };
});
