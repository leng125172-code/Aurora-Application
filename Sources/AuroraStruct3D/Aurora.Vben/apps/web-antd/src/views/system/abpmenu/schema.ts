import type { VxeGridProps } from '#/adapter/vxe-table';

import { computed, h } from 'vue';

import dayjs from 'dayjs';

import { postMenusTree } from '#/api-client/index';
import { Icon } from '#/components/icon';
import { $t } from '#/locales';

async function getMenusTree() {
  const response = await postMenusTree();
  return response.data;
}
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
]);

export const tableSchema: any = computed((): VxeGridProps['columns'] => [
  {
    field: 'title',
    title: '菜单名称',
    treeNode: true,
    align: 'left',
    slots: {
      default: ({ row }) => {
        return row.icon
          ? h('span', {}, [
              h(Icon, {
                icon: row.icon,
              }),
              h(
                'span',
                {
                  style: {
                    paddingLeft: '6px',
                  },
                },
                row.localizationTitle,
              ),
            ])
          : h('span', {}, row.localizationTitle);
      },
    },
  },
  {
    field: 'name',
    title: $t('abp.dynamicMenu.name'),
  },

  { field: 'path', title: $t('abp.dynamicMenu.path') },
  { field: 'component', title: $t('abp.dynamicMenu.component') },
  {
    field: 'policy',
    title: $t('abp.dynamicMenu.policy'),
  },
  {
    field: 'menuType',
    title: $t('abp.dynamicMenu.menuType'),
    width: '100',
    slots: { default: 'menuType' },
  },
  {
    field: 'enabled',
    title: $t('abp.dynamicMenu.enabled'),
    width: '100',
    slots: { default: 'enabled' },
  },
  {
    field: 'hideInMenu',
    title: $t('abp.dynamicMenu.hideInMenu'),
    slots: { default: 'hideInMenu' },
    width: '100',
  },
  {
    field: 'keepAlive',
    title: $t('abp.dynamicMenu.keepAlive'),
    slots: { default: 'keepAlive' },
    width: '100',
  },

  {
    field: 'order',
    title: $t('abp.dynamicMenu.order'),
    minWidth: '75',
    width: '75',
  },
  {
    title: $t('common.action'),
    field: 'action',
    fixed: 'right',
    width: '200',
    slots: { default: 'action' },
  },
]);

export const addFormSchema = computed(() => [
  {
    fieldName: 'menuType',
    label: $t('abp.dynamicMenu.menuType'),
    rules: 'required',
    component: 'Select',
    componentProps: {
      options: [
        { label: $t('abp.dynamicMenu.folder'), value: 10 },
        { label: $t('abp.dynamicMenu.menu'), value: 20 },
      ],
    },
  },
  {
    fieldName: 'openType',
    label: $t('abp.dynamicMenu.openType'),
    rules: 'required',
    component: 'Select',
    componentProps: {
      options: [
        { label: $t('abp.dynamicMenu.componentType'), value: 20 },
        { label: $t('abp.dynamicMenu.internalLink'), value: 30 },
        { label: $t('abp.dynamicMenu.externalLink'), value: 40 },
      ],
    },
    dependencies: {
      triggerFields: ['menuType'],
      disabled: (values) => {
        // 打开方式只有菜单==2才需配置
        values.component = values.menuType === 10 ? '' : '';
        values.openType = 20;
        return values.menuType !== 20;
      },
    },
  },
  {
    fieldName: 'parentId',
    label: $t('abp.dynamicMenu.parentId'),
    component: 'ApiTreeSelect',
    componentProps: {
      childrenField: 'children',
      allowClear: true,
      api: getMenusTree,
      labelField: 'meta.title',
      valueField: 'id',
    },
  },
  {
    fieldName: 'name',
    label: $t('abp.dynamicMenu.name'),
    rules: 'required',
    help: 'route.name',
    component: 'Input',
  },
  {
    fieldName: 'path',
    label: $t('abp.dynamicMenu.path'),
    rules: 'required',
    component: 'Input',
    help: 'route.path',
  },
  {
    fieldName: 'policy',
    label: $t('abp.dynamicMenu.policy'),
    help: 'route.policy',
    component: 'Input',
    dependencies: {
      triggerFields: ['menuType'],
      disabled: (values) => {
        if (values.menuType === 10) {
          values.policy = '';
          return true;
        }
      },
    },
  },
  {
    fieldName: 'title',
    label: $t('abp.dynamicMenu.title'),
    help: 'route.title',
    rules: 'required',
    component: 'Input',
  },
  {
    fieldName: 'displayTitle',
    label: $t('abp.dynamicMenu.displayTitle'),
    help: 'route.displayTitle',
    component: 'Input',
  },

  {
    fieldName: 'component',
    label: $t('abp.dynamicMenu.component'),
    help: 'route.component',
    component: 'Input',
    dependencies: {
      triggerFields: ['openType', 'menuType'],
      disabled: (values) => {
        if (
          values.openType === 30 ||
          values.openType === 40 ||
          values.menuType === 10
        ) {
          values.component = values.menuType === 10 ? '' : 'IFrameView';

          return true;
        } else {
          values.component = '';
        }
      },
    },
  },
  {
    fieldName: 'icon',
    label: $t('abp.dynamicMenu.icon'),
    component: 'IconPicker',
  },

  {
    fieldName: 'url',
    label: $t('abp.dynamicMenu.url'),
    component: 'Input',
    dependencies: {
      triggerFields: ['openType'],
      disabled: (values) => {
        if (values.openType === 20) {
          return true;
        }
      },
    },
  },
  {
    fieldName: 'keepAlive',
    label: $t('abp.dynamicMenu.keepAlive'),
    defaultValue: false,
    help: 'meta.keepAlive',
    component: 'RadioGroup',
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
  },
  {
    fieldName: 'hideInMenu',
    label: $t('abp.dynamicMenu.hideInMenu'),
    help: 'meta.hideInMenu',
    defaultValue: false,
    component: 'RadioGroup',
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
  },
  {
    fieldName: 'enabled',
    label: $t('abp.dynamicMenu.enabled'),
    defaultValue: true,
    component: 'RadioGroup',
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
  },
  {
    fieldName: 'order',
    label: $t('abp.dynamicMenu.order'),
    defaultValue: 1,
    component: 'InputNumber',
  },
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
    fieldName: 'parentId',
    label: 'parentId',
    dependencies: {
      show: () => false,
      triggerFields: ['parentId'],
    },
  },
  {
    fieldName: 'menuType',
    label: $t('abp.dynamicMenu.menuType'),
    rules: 'required',
    component: 'Select',
    componentProps: {
      options: [
        { label: $t('abp.dynamicMenu.folder'), value: 10 },
        { label: $t('abp.dynamicMenu.menu'), value: 20 },
      ],
    },
  },
  {
    fieldName: 'openType',
    label: $t('abp.dynamicMenu.openType'),
    rules: 'required',
    component: 'Select',
    componentProps: {
      options: [
        { label: $t('abp.dynamicMenu.componentType'), value: 20 },
        { label: $t('abp.dynamicMenu.internalLink'), value: 30 },
        { label: $t('abp.dynamicMenu.externalLink'), value: 40 },
      ],
    },
  },
  {
    fieldName: 'parentId',
    label: $t('abp.dynamicMenu.parentId'),
    component: 'ApiTreeSelect',
    componentProps: {
      childrenField: 'children',
      allowClear: true,
      api: getMenusTree,
      labelField: 'meta.title',
      valueField: 'id',
    },
  },
  {
    fieldName: 'name',
    label: $t('abp.dynamicMenu.name'),
    rules: 'required',
    help: 'route.name',
    component: 'Input',
  },
  {
    fieldName: 'path',
    label: $t('abp.dynamicMenu.path'),
    rules: 'required',
    component: 'Input',
    help: 'route.path',
  },
  {
    fieldName: 'policy',
    label: $t('abp.dynamicMenu.policy'),
    help: 'route.policy',
    component: 'Input',
    dependencies: {
      triggerFields: ['menuType'],
      disabled: (values) => {
        if (values.menuType === 10) {
          values.policy = '';
          return true;
        }
      },
    },
  },
  {
    fieldName: 'title',
    label: $t('abp.dynamicMenu.title'),
    help: 'route.title',
    rules: 'required',
    component: 'Input',
  },
  {
    fieldName: 'displayTitle',
    label: $t('abp.dynamicMenu.displayTitle'),
    help: 'route.displayTitle',
    component: 'Input',
  },

  {
    fieldName: 'component',
    label: $t('abp.dynamicMenu.component'),
    help: 'route.component',
    component: 'Input',
  },
  {
    fieldName: 'icon',
    label: $t('abp.dynamicMenu.icon'),
    component: 'IconPicker',
  },

  {
    fieldName: 'url',
    label: $t('abp.dynamicMenu.url'),
    component: 'Input',
  },
  {
    fieldName: 'keepAlive',
    label: $t('abp.dynamicMenu.keepAlive'),
    defaultValue: false,
    help: 'meta.keepAlive',
    component: 'RadioGroup',
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
  },
  {
    fieldName: 'hideInMenu',
    label: $t('abp.dynamicMenu.hideInMenu'),
    help: 'meta.hideInMenu',
    defaultValue: true,
    component: 'RadioGroup',
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
  },
  {
    fieldName: 'enabled',
    label: $t('abp.dynamicMenu.enabled'),
    defaultValue: true,
    component: 'RadioGroup',
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
  },
  {
    fieldName: 'order',
    label: $t('abp.dynamicMenu.order'),
    defaultValue: 1,
    component: 'InputNumber',
  },
]);
