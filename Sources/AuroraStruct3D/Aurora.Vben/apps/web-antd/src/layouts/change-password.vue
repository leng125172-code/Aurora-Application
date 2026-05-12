<script setup lang="ts">
import { defineProps } from 'vue';

import { z } from '@vben/common-ui';

import { message as Message } from 'ant-design-vue';

import { useVbenForm } from '#/adapter/form';
import { postUsersChangePassword } from '#/api-client';
import { $t } from '#/locales';

interface Props {
  message: string;
}
defineOptions({
  name: 'ChangePassword',
});
const props = withDefaults(defineProps<Props>(), {});
const emit = defineEmits<{
  close: any;
}>();
const [ResetPasswordForm, resetPasswordApi] = useVbenForm({
  // 默认展开
  collapsed: false,
  // 所有表单项共用，可单独在表单内覆盖
  commonConfig: {
    // 所有表单项
    componentProps: {
      class: 'w-4/5',
    },
  },
  // 提交函数
  handleSubmit: async () => {
    // 表单校验
    const { valid } = await resetPasswordApi.validate();
    if (!valid) return;
    const formValues = await resetPasswordApi.getValues();

    if (formValues.currentPassword === formValues.confirmPassword) {
      Message.warn($t('abp.user.newPasswordAndCurrentPasswordNotAlike'));
      return;
    }
    if (formValues.newPassword !== formValues.confirmPassword) {
      Message.warn($t('abp.user.newPasswordAndConfirmPasswordNotMatch'));
      return;
    }
    await postUsersChangePassword({ body: formValues });
    Message.success($t('abp.user.changePassword') + $t('common.success'));
    await resetPasswordApi.resetForm();
    emit('close');
  },
  layout: 'horizontal',
  schema: [
    {
      component: 'VbenInputPassword',
      componentProps: {
        placeholder: $t('common.pleaseInput') + $t('abp.user.currentPassword'),
      },
      fieldName: 'currentPassword',
      label: $t('abp.user.currentPassword'),
      rules: z.string().min(1, {
        message: $t('common.pleaseInput') + $t('abp.user.currentPassword'),
      }),
    },
    {
      component: 'VbenInputPassword',
      componentProps: {
        placeholder: $t('common.pleaseInput') + $t('abp.user.newPassword'),
      },
      fieldName: 'newPassword',
      label: $t('abp.user.newPassword'),
      rules: z.string().min(1, {
        message: $t('common.pleaseInput') + $t('abp.user.newPassword'),
      }),
    },
    {
      component: 'VbenInputPassword',
      componentProps: {
        placeholder: $t('common.pleaseInput') + $t('abp.user.comfirmPassword'),
      },
      fieldName: 'confirmPassword',
      label: $t('abp.user.comfirmPassword'),
      rules: z.string().min(1, {
        message: $t('common.pleaseInput') + $t('abp.user.comfirmPassword'),
      }),
    },
  ],
  resetButtonOptions: {
    show: false,
  },
  submitButtonOptions: {
    content: '确认',
  },
  wrapperClass: 'grid-cols-1',
});
</script>

<template>
  <div>
    <div
      class="flex w-full justify-center"
      style="margin-bottom: 20px; color: red"
    >
      {{ message }}
    </div>
    <ResetPasswordForm />
  </div>
</template>
