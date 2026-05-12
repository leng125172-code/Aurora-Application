<script lang="ts" setup>
import { useVbenModal } from '@vben/common-ui';

import { Button, message } from 'ant-design-vue';

import { useVbenForm } from '#/adapter/form';
import {
  putImportRecordsDownloadExcelTemplate,
  putImportRecordsImportExcel,
} from '#/api-client/index';
import { $t } from '#/locales';

import { addFormSchema, contributors } from './schema';

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
    const { valid } = await formApi.validate();
    if (!valid) return;
    try {
      modalApi.setState({ loading: true, confirmLoading: true });
      // 获取表单值
      const values = await formApi.getValues();

      const excelFiles = values.file?.map((file: any) => file.originFileObj);
      let file;
      if (excelFiles?.length > 0) {
        file = excelFiles[0];
      }
      const submitData: any = {
        ContributorName: values.contributorName,
        File: file,
        Remark: values.remark,
      };
      // 编辑
      await putImportRecordsImportExcel({ body: submitData });
      message.success($t('abp.import.successMessage'));
      emit('reload');
      modalApi.close();
    } finally {
      modalApi.setState({ loading: false, confirmLoading: false });
    }
  },
});

const downloadFile = async () => {
  const values = await formApi.getValues();
  if (!values.contributorName) return;
  const contributor = contributors.value.find(
    (item) => item.contributor === values.contributorName,
  );
  const { data } = await putImportRecordsDownloadExcelTemplate({
    body: { contributor: values.contributorName },
    responseType: 'blob',
  });
  const url = window.URL.createObjectURL(new Blob([data as Blob]));
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', `${contributor.name}.xlsx`);
  document.body.append(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
};
</script>
<template>
  <Modal :title="$t('common.add')">
    <Form>
      <template #template="slotProps">
        <Button @click="downloadFile">
          {{ $t('abp.import.downloadTemplate') }}
        </Button>
      </template>
    </Form>
  </Modal>
</template>
