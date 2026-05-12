import type { VxeGridProps } from '#/adapter/vxe-table';

import { computed } from 'vue';

import { $t } from '#/locales';

export const querySchema = computed(() => [
  {
    component: 'Input',
    fieldName: 'key',
    label: $t('abp.cache.key'),
  },
]);

export const tableSchema: any = computed((): VxeGridProps['columns'] => [
  { title: $t('common.seq'), type: 'seq', width: 50 },
  {
    title: $t('abp.cache.key'),
    minWidth: '150',
    field: 'key',
    align: 'left',
  },
  {
    title: $t('common.action'),
    field: 'action',
    fixed: 'right',
    width: '150',
    slots: { default: 'action' },
  },
]);
