import type { VxeGridProps } from '#/adapter/vxe-table';

import { computed } from 'vue';

import { z } from '@vben/common-ui';

import { $t } from '#/locales';

export const querySchema = computed(() => [
  {
    component: 'Input',
    fieldName: 'filter',
    label: $t('abp.role.roleName'),
  },
]);

export const tableSchema: any = computed((): VxeGridProps['columns'] => [
  { title: $t('common.seq'), type: 'seq', width: 50 },
  {
    field: 'name',
    title: $t('abp.role.roleName'),
    minWidth: '150',
    sortable: true,
  },
  {
    field: 'isDefault',
    title: $t('abp.role.isDefault'),
    minWidth: '75',
    slots: { default: 'isDefault' },
  },
  {
    title: $t('common.action'),
    field: 'action',
    fixed: 'right',
    width: '250',
    slots: { default: 'action' },
  },
]);

export const addRoleFormSchema = computed(() => [
  {
    component: 'Input',
    componentProps: {
      placeholder: $t('common.pleaseInput') + $t('abp.role.roleName'),
    },
    fieldName: 'name',
    label: $t('abp.role.roleName'),
    rules: z
      .string()
      .min(1, { message: $t('common.pleaseInput') + $t('abp.role.roleName') }),
  },
  {
    component: 'RadioGroup',
    componentProps: {
      options: [
        {
          label: $t('common.yes'),
          value: 1,
        },
        {
          label: $t('common.no'),
          value: 0,
        },
      ],
    },
    defaultValue: 0,
    fieldName: 'isDefault',
    label: $t('abp.role.isDefault'),
    rules: 'required',
  },
]);
