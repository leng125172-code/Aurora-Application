import type { VxeGridProps } from '#/adapter/vxe-table';

import { computed } from 'vue';

import dayjs from 'dayjs';

import { $t } from '#/locales';

export const logQuerySchema = computed(() => [
  {
    component: 'DatePicker',
    fieldName: 'time',
    label: $t('abp.log.executionTime'),
    componentProps: {
      type: 'daterange', // 添加这个属性来启用日期范围选择
      startPlaceholder: '开始日期', // 添加占位符
      endPlaceholder: '结束日期', // 添加占位符
      valueFormat: 'YYYY-MM-DD',
    },

    defaultValue: [
      dayjs().subtract(0, 'day').format('YYYY-MM-DD'),
      dayjs().format('YYYY-MM-DD'),
    ],
  },
  {
    component: 'Input',
    fieldName: 'userName',
    label: $t('abp.log.userName'),
  },
  {
    component: 'Input',
    fieldName: 'correlationId',
    label: 'CorrelationId',
  },
]);

export const logTableSchema: any = computed((): VxeGridProps['columns'] => [
  { title: $t('common.seq'), type: 'seq', width: 50 },
  {
    field: 'applicationName',
    title: $t('abp.log.applicationName'),
    minWidth: '150',
  },
  { field: 'identity', title: $t('abp.log.loginMode'), minWidth: '150' },
  { field: 'action', title: $t('abp.log.loginUrl'), minWidth: '150' },
  { field: 'userName', title: $t('abp.log.userName'), minWidth: '150' },
  { field: 'correlationId', title: 'CorrelationId', minWidth: '150' },
  { field: 'clientIpAddress', title: $t('abp.log.clientIp'), minWidth: '150' },
  {
    field: 'creationTime',
    title: $t('common.createTime'),
    minWidth: '150',
    formatter: ({ cellValue }) => {
      return dayjs(cellValue).format('YYYY-MM-DD HH:mm:ss');
    },
  },
]);

export const auditLogQuerySchema = computed(() => [
  {
    component: 'DatePicker',
    fieldName: 'time',
    label: $t('abp.log.loginTime'),
    componentProps: {
      type: 'daterange', // 添加这个属性来启用日期范围选择
      startPlaceholder: '开始日期', // 添加占位符
      endPlaceholder: '结束日期', // 添加占位符
      valueFormat: 'YYYY-MM-DD',
    },
    defaultValue: [
      dayjs().subtract(0, 'day').format('YYYY-MM-DD'),
      dayjs().format('YYYY-MM-DD'),
    ],
  },
  {
    component: 'Input',
    fieldName: 'userName',
    label: $t('abp.log.userName'),
  },
  {
    component: 'Input',
    fieldName: 'correlationId',
    label: 'CorrelationId',
  },
  {
    component: 'Input',
    fieldName: 'url',
    label: 'Url',
  },
]);

export const auditLogTableSchema: any = computed(
  (): VxeGridProps['columns'] => [
    { title: $t('common.seq'), type: 'seq', width: 50 },
    {
      field: 'url',
      title: 'Url',
      minWidth: '250',
      align: 'left',
      slots: { default: 'url' },
    },
    // { field: 'tenantName', title: $t('abp.log.tenant'), minWidth: '150' },
    {
      field: 'httpStatusCode',
      title: $t('abp.log.httpStatusCode'),
      width: '100',
      slots: { default: 'httpStatusCode' },
    },
    { field: 'userName', title: $t('abp.log.userName'), width: '125' },
    {
      field: 'executionTime',
      title: $t('abp.log.executionTime'),
      width: '150',
      formatter: ({ cellValue }) => {
        return dayjs(cellValue).format('YYYY-MM-DD HH:mm:ss');
      },
    },
    {
      field: 'executionDuration',
      title: $t('abp.log.responseTime'),
      width: '125',
    },
    {
      field: 'clientIpAddress',
      title: $t('abp.log.clientIp'),
      minWidth: '100',
    },
    { field: 'correlationId', title: 'CorrelationId', minWidth: '150' },
    { field: 'exceptions', title: $t('abp.log.exception'), minWidth: '150' },
    {
      field: 'action',
      fixed: 'right',
      slots: { default: 'action' },
      title: '操作',
      width: 120,
    },
  ],
);
