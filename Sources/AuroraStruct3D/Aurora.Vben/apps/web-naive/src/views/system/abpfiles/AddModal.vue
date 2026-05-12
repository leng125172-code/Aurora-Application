<script lang="ts" setup>
import { useVbenModal } from '@vben/common-ui';

import { useVbenForm } from '#/adapter/form';
import { $t } from '#/locales';

import { addFormSchema } from './schema';

const emit = defineEmits(['reload']);
const [Form, formApi] = useVbenForm({
  // 所有表单项共用，可单独在表单内覆盖
  commonConfig: {
    // 所有表单项
    componentProps: {
      class: 'w-full',
    },
  },
  showDefaultActions: false,
  // 垂直布局，label和input在不同行，值为vertical
  // 水平布局，label和input在同一行
  layout: 'horizontal',
  schema: addFormSchema.value,
  wrapperClass: 'grid-cols-1',
});
const [Modal, modalApi] = useVbenModal({
  onCancel() {
    modalApi.close();
  },
  async onConfirm() {
    emit('reload');
    modalApi.close();
  },
});
</script>
<template>
  <Modal :title="$t('common.upload')">
    <Form />
  </Modal>
</template>
