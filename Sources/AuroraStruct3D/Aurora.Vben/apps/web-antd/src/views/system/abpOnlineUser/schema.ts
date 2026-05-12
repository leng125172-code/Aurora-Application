import type { VxeGridProps } from '#/adapter/vxe-table';

import { computed } from 'vue';

import dayjs from 'dayjs';

import { $t } from '#/locales';

export const querySchema = computed(() => [
  {
    component: 'Input',
    fieldName: 'userName',
    label: $t('abp.log.userName'),
  },
]);

export const tableSchema: any = computed((): VxeGridProps['columns'] => [
  { title: $t('common.seq'), type: 'seq', width: 50 },
  {
    field: 'userName',
    title: $t('abp.user.userName'),
  },
  { field: 'ip', title: 'IP' },
  {
    field: 'loginTime',
    title: $t('abp.log.loginTime'),
    formatter: ({ cellValue }) => {
      return dayjs(cellValue).format('YYYY-MM-DD HH:mm:ss');
    },
  },
  { field: 'deviceInfo', title: $t('abp.log.deviceInfo') },
  {
    title: $t('common.action'),
    field: 'action',
    fixed: 'right',
    width: '150',
    slots: { default: 'action' },
  },
]);
