import { useAccessStore, useUserStore } from '@vben/stores';
import {
  decryptAES,
  encryptAES,
  encryptRSA,
  generateAESKey,
} from '@vben/utils';

import { message as Message } from 'ant-design-vue';

import { postApiAppAccountRefreshToken } from '#/api-client';
import { $t } from '#/locales';
import { antdLocale } from '#/locales/index';
import { useAuthStore } from '#/store';

import { client } from '../api-client/services.gen';

client.setConfig({
  baseURL: import.meta.env.DEV
    ? '/proxy/'
    : import.meta.env.VITE_APP_API_ADDRESS,
  timeout: 1000 * 60,
  responseType: 'json',
  throwOnError: true,
});

// 是否正在刷新token
let isRefreshing = false;
// 添加重试计数器
let refreshRetryCount = 0;
const MAX_REFRESH_RETRY = 1; // 最大重试次数
// 刷新token队列
let refreshTokenQueue: ((token: string) => void)[] = [];
client.instance.interceptors.request.use((request) => {
  // 全局拦截请求发送前提交的参数
  const userStore = useUserStore();
  const accessStore = useAccessStore();
  const token = accessStore.getAccessToken();

  // 设置请求头
  if (request.headers) {
    request.headers.__tenant = userStore.tenant?.tenantId;
    // todo vben5 没有提供统一获取当前语言的方式
    request.headers['accept-language'] = antdLocale.value.locale;
  }

  shouldEncryptRequestBody(request);

  // 如果token过期，则跳转到登录页面
  if (
    request.url !== undefined &&
    request.url.includes('/api/app/account/login')
  ) {
    return request;
  }

  // 设置请求头
  if (request.headers) {
    request.headers.Authorization = `Bearer ${token}`;
  }

  return request;
});

client.instance.interceptors.response.use(
  (response) => {
    response.data = shouldDecryptResponseBody(response);
    return Promise.resolve(response);
  },
  async (error) => {
    let message = $t('common.mesage500');
    const responseBody = shouldDecryptResponseBody(error.response);
    switch (error.status) {
      case 400: {
        message = responseBody.error?.validationErrors[0].message;
        break;
      }
      case 401: {
        message = $t('common.mesage401');
        const { config } = error;
        const originalRequest = config;
        if (refreshRetryCount >= MAX_REFRESH_RETRY) {
          // 超过最大重试次数，直接登出
          refreshTokenQueue = [];
          isRefreshing = false;
          refreshRetryCount = 0;
          const authStore = useAuthStore();
          authStore.logout();

          break;
        }
        if (isRefreshing) {
          return new Promise((resolve) => {
            refreshTokenQueue.push((token) => {
              originalRequest.headers.Authorization = `Bearer ${token}`;
              resolve(client.request(originalRequest));
            });
          });
        } else {
          isRefreshing = true;
          refreshRetryCount++; // 增加重试计数
          try {
            const newToken = await refreshTokenAsync();
            // 处理队列中的请求
            refreshTokenQueue.forEach((callback) => callback(newToken));
            // 清空队列
            refreshTokenQueue = [];
            return client.request(originalRequest);
          } catch (refreshError) {
            // 如果刷新 token 失败，处理错误（如强制登出或跳转登录页面）
            message = $t('common.mesage401');
            refreshTokenQueue = [];
            const authStore = useAuthStore();
            refreshRetryCount = 0;
            console.error(refreshError);
            authStore.logout();
          } finally {
            isRefreshing = false;
          }
        }
        break;
      }
      case 403: {
        message = $t('common.mesage403');
        break;
      }
      case 500: {
        message = responseBody.error?.message;
        break;
      }
      default: {
        if (message.includes('Request failed with status code')) {
          message = $t('common.mesage500');
        }
      }
    }
    Message.error(message);
    throw error;
  },
);

async function refreshTokenAsync(): Promise<string> {
  try {
    const userStore = useUserStore();
    const accessStore = useAccessStore();
    const refreshToken = accessStore.getRefreshToken();
    if (!refreshToken) return '';
    const res = await postApiAppAccountRefreshToken({
      body: {
        userId: userStore.userInfo?.id,
        refreshToken,
      },
    });
    if (res?.data?.success) {
      accessStore.setAccessToken(res.data.token as string);
      accessStore.setRefreshToken(res.data.refreshToken as string);
      return res.data.token as string;
    } else {
      throw new Error('get refreshToken error');
    }
  } catch {
    throw new Error($t('common.mesage401'));
  }
}

/**
 * 是否加密请求参数
 */
function shouldEncryptRequestBody(request: any) {
  const userStore = useUserStore();

  // 判断是否需要加密
  if (
    !userStore.abpProApplicationConfiguration?.encryptionEnabled ||
    request.method?.toUpperCase() !== 'POST' ||
    !request.data
  ) {
    return;
  }

  // 获取公钥
  const publicKey =
    userStore.abpProApplicationConfiguration.encryptionPublicKey;

  if (!publicKey) return;

  // 1.生成 AES 密钥
  const aesKey = generateAESKey();
  // 2.加密 body
  const encryptedBody = encryptAES(JSON.stringify(request.data), aesKey);
  // 3.RSA 加密 AES 密钥
  const encryptedAESKey = encryptRSA(aesKey, publicKey);
  // 4.替换原始 body
  request.data = { data: encryptedBody, key: encryptedAESKey };
}

/**
 * 响应数据解密
 */
function shouldDecryptResponseBody(response: any) {
  const userStore = useUserStore();
  // 判断是否需要解密
  if (
    !userStore.abpProApplicationConfiguration?.encryptionEnabled ||
    response.config.method?.toUpperCase() !== 'POST' ||
    response.status === 204 ||
    !response.data
  ) {
    return response.data;
  }

  try {
    const { data, key } = response.data;
    const decrypted = decryptAES(data, decodeURIComponent(atob(key)));
    const responseBody = JSON.parse(decrypted);
    return responseBody;
  } catch {
    return response.data;
  }
}
export default client;
