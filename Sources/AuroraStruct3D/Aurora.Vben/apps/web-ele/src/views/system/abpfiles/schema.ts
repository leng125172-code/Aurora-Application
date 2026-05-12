import type { VxeGridProps } from '#/adapter/vxe-table';

import { computed } from 'vue';

import { useUserStore } from '@vben/stores';

import dayjs from 'dayjs';

import { $t } from '#/locales';

const userStore = useUserStore();
export const querySchema = computed(() => [
  {
    component: 'DatePicker',
    fieldName: 'time',
    label: $t('abp.log.executionTime'),
    componentProps: {
      type: 'daterange',
      'range-separator': '至',
      'value-format': 'YYYY-MM-DD',
    },
    defaultValue: [
      dayjs().subtract(7, 'day').format('YYYY-MM-DD'),
      dayjs().format('YYYY-MM-DD'),
    ],
  },
  {
    component: 'Input',
    fieldName: 'fileName',
    label: $t('abp.file.name'),
  },
]);

export const tableSchema: any = computed((): VxeGridProps['columns'] => [
  { title: $t('common.seq'), type: 'seq', width: 50 },
  {
    title: $t('abp.file.name'),
    minWidth: '150',
    field: 'fileName',
  },
  {
    title: $t('abp.file.size'),
    minWidth: '150',
    field: 'beautifySize',
  },
  {
    title: $t('abp.file.contentType'),
    minWidth: '150',
    field: 'contentType',
  },
  {
    field: 'creationTime',
    title: $t('common.createTime'),
    minWidth: '150',
    formatter: ({ cellValue }) => {
      return dayjs(cellValue).format('YYYY-MM-DD HH:mm:ss');
    },
  },
  {
    title: $t('common.action'),
    field: 'action',
    fixed: 'right',
    width: '150',
    slots: { default: 'action' },
  },
]);

export const addFormSchema = computed(() => [
  {
    component: 'Upload',
    fieldName: 'files',
    label: $t('abp.file.file'),
    rules: 'required',
    componentProps: () => {
      return {
        listType: 'picture-card',
        autoUpload: false,
        multiple: true,
      };
    },
    renderComponentContent: () => {
      return {
        default: () => 'Upload',
      };
    },
  },
]);
