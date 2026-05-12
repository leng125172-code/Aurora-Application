import type { VxeGridProps } from '#/adapter/vxe-table';

import { computed, ref } from 'vue';

import { $t } from '@vben/locales';

import dayjs from 'dayjs';

import { postImportRecordsImportExcelContributor } from '#/api-client';

export const contributors = ref<any[]>([]);

async function getImportRecordsImportExcelContributor() {
  const response = await postImportRecordsImportExcelContributor();
  contributors.value = response.data as any;
  return response.data;
}

export const importExcelQuerySchema = computed(() => [
  {
    component: 'ApiSelect',
    fieldName: 'name',
    label: $t('abp.import.type'),
    componentProps: {
      allowClear: true,
      showSearch: true,
      api: getImportRecordsImportExcelContributor,
      labelField: 'name',
      valueField: 'name',
    },
  },
  {
    component: 'Input',
    fieldName: 'blobName',
    label: $t('abp.import.file'),
  },
]);

export const importExcelTableSchema: any = computed(
  (): VxeGridProps['columns'] => [
    { title: $t('common.seq'), type: 'seq', width: 50 },
    {
      field: 'name',
      title: $t('abp.import.type'),
      minWidth: '150',
    },
    {
      field: 'blobName',
      title: $t('abp.import.file'),
      minWidth: '150',
      slots: { default: 'blobName' },
    },
    {
      field: 'importStatusDescription',
      title: $t('abp.import.status'),
      width: '90',
      slots: { default: 'status' },
    },
    {
      field: 'blobErrorName',
      title: $t('abp.import.errorFile'),
      minWidth: '150',
      slots: { default: 'blobErrorName' },
    },
    {
      field: 'creationTime',
      title: $t('abp.import.importTime'),
      minWidth: '150',
      formatter: ({ cellValue }) => {
        return dayjs(cellValue).format('YYYY-MM-DD HH:mm:ss');
      },
    },
    {
      field: 'remark',
      title: $t('abp.import.remark'),
      minWidth: '150',
    },
  ],
);

export const addFormSchema = computed(() => [
  {
    component: 'ApiSelect',
    fieldName: 'contributorName',
    label: $t('abp.import.type'),
    rules: 'required',
    componentProps: {
      allowClear: true,
      showSearch: true,
      api: getImportRecordsImportExcelContributor,
      labelField: 'name',
      valueField: 'contributor',
    },
  },
  {
    component: 'Input',
    fieldName: 'template',
    label: $t('abp.import.template'),
  },
  {
    component: 'Upload',
    fieldName: 'file',
    label: $t('abp.file.file'),
    rules: 'required',
    componentProps: () => {
      return {
        listType: 'picture-card',
        autoUpload: false,
        multiple: false,
        maxCount: 1,
        customRequest: () => {
          return true;
        },
        locale: {
          uploading: $t('abp.file.uploadingTip'),
        },
      };
    },
    renderComponentContent: () => {
      return {
        default: () => 'Upload',
      };
    },
  },
  {
    component: 'Input',
    fieldName: 'remark',
    label: $t('abp.import.remark'),
  },
]);
