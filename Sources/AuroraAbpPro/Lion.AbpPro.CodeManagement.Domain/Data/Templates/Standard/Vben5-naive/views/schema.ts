import type { VxeGridProps } from '#/adapter/vxe-table';
import { z } from '@vben/common-ui';
import { computed } from 'vue';
import dayjs from 'dayjs';

import { $t } from '#/locales';

export const querySchema = computed(() => [
    {
        component: 'RangePicker',
        fieldName: 'time',
        label: '创建时间',
        componentProps: {
            'value-format': 'YYYY-MM-DD',
        },
        defaultValue: [
            // 最近7天
            dayjs().subtract(7, 'day').format('YYYY-MM-DD'),
            dayjs().subtract(-1, 'day').format('YYYY-MM-DD'),
        ],
    },
]);


export const tableSchema: any = computed((): VxeGridProps['columns'] => [
{ title: $t('common.seq'), type: 'seq', width: 50 },
{{~ for prop in context.EntityModel.Properties ~}}
{
    title: '{{prop.Description}}',
    minWidth: '150',
    {{~ if  prop.IsEnum ~}}
    field: '{{ prop.EnumType.CodeCamelCase }}Description',
    {{~ else ~}}
    field: '{{prop.CodeCamelCase}}',
    {{~ end ~}}
    {{~ if  prop.DataType?.Code=="bool" ~}}
    slots: { default: '{{prop.CodeCamelCase}}' },
    {{~ end ~}}
},
{{~ end ~}}
{
    title: $t('common.action'),
    field: 'action',
    fixed: 'right',
    width: '150',
    slots: { default: 'action' },
},
];


export const addFormSchema = computed(() => [
{{~ for prop in context.EntityModel.Properties ~}}
{
    fieldName: '{{prop.CodeCamelCase}}',
    label: '{{prop.Description}}',
    {{~ if  prop.IsRequired ~}}
    rules: 'required',
    {{~ end ~}}
    {{~ if  prop.IsEnum ~}}
    component: "Select",
    componentProps: {
    options: [
        {{~ for item in prop.EnumType.Properties ~}}
            { label: '{{item.Description}}', value: {{item.Value}} },
        {{~ end ~}}
        ],
    }
    {{~ else if prop.DataType.Code=="bool" }}
    component: "RadioGroup",
    componentProps: {
    options: [
        {
            label: $t('common.yes'),
            value: true,
        },
        {
            label: $t('common.no'),
            value: false,
        },
    ],
},
    {{~ else ~}}
    component: "Input",
    {{~ end ~}}
},
{{~ end ~}}
]);

export const editFormSchema = computed(() => [
    {
        component: 'Input',
        componentProps: {},
        fieldName: 'id',
        label: 'id',
        dependencies: {
            show: () => false,
            triggerFields: ['id'],
        },
    },
    {
      component: 'Input',
      componentProps: {},
      fieldName: 'concurrencyStamp',
      label: 'concurrencyStamp',
      dependencies: {
        show: () => false,
        triggerFields: ['concurrencyStamp'],
      },
    },
    {{~ for prop in context.EntityModel.Properties ~}}
{
    fieldName: '{{prop.CodeCamelCase}}',
    label: '{{prop.Description}}',
    {{~ if  prop.IsRequired ~}}
    rules: 'required',
    {{~ end ~}}
    {{~ if  prop.IsEnum ~}}
    component: "Select",
    componentProps: {
    options: [
        {{~ for item in prop.EnumType.Properties ~}}
    { label: '{{item.Description}}', value: {{item.Value}} },
    {{~ end ~}}
],
}
    {{~ else if prop.DataType?.Code=="bool" }}
    component: "RadioGroup",
    componentProps: {
    options: [
        {
            label: $t('common.yes'),
            value: true,
        },
        {
            label: $t('common.no'),
            value: false,
        },
     ],
    },
    {{~ else ~}}
    component: "Input",
        {{~ end ~}}
},
{{~ end ~}}
]);