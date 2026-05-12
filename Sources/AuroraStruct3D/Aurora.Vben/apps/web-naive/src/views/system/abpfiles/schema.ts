import type { VxeGridProps } from '#/adapter/vxe-table';

import { computed } from 'vue';

import { useUserStore } from '@vben/stores';

import dayjs from 'dayjs';

import { postFilesDelete } from '#/api-client/index';
import { $t } from '#/locales';

const userStore = useUserStore();
export const querySchema = computed(() => [
  {
    component: 'RangePicker',
    fieldName: 'time',
    label: $t('common.createTime'),
    componentProps: {
      'value-format': 'YYYY-MM-DD',
    },
    defaultValue: [
      // 最近7天
      dayjs().subtract(7, 'day').format('YYYY-MM-DD'),
      dayjs().subtract(-1, 'day').format('YYYY-MM-DD'),
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
    componentProps: () => {
      return {
        listType: 'image-card',
        autoUpload: true,

        name: 'files',
        action: `${import.meta.env.VITE_APP_API_ADDRESS}/Files/Upload?access_token=${
          userStore.userInfo?.token
        }`,
        onPreview: () => {},
        onRemove: async (file: any) => {
          await postFilesDelete({
            body: {
              id: file.response[0].id,
            },
          });
        },
      };
    },
    renderComponentContent: () => {
      return {
        default: () => 'NUpload',
      };
    },
  },
]);
