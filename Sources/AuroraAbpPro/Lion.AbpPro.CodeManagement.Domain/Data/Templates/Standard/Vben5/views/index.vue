<script lang="ts" setup>
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { Page, useVbenModal } from '@vben/common-ui';

import { Button, Modal, Space, Tag } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { post{{ context.EntityModel.AggregateCodePluralized }}Delete, post{{ context.EntityModel.AggregateCodePluralized }}Page } from '#/api-client/index';
import { $t } from '#/locales';
import { TableAction } from '#/components/table-action';

// 新增modal
import AddModal from './AddModal.vue';
// 编辑modal
import EditModal from './EditModal.vue';
import { querySchema, tableSchema } from './schema';
//import fileRequest from '#/api-client-config/index-blob';

const formOptions: VbenFormProps = {
  // 默认展开
  collapsed: false,
  schema: querySchema,
  // 控制表单是否显示折叠按钮
  showCollapseButton: true,
  // 按下回车时是否提交表单
  submitOnEnter: false,
};

const gridOptions: VxeGridProps<any> = {
  checkboxConfig: {
    highlight: true,
    labelField: 'name',
  },
  columns: tableSchema.value,
  keepSource: true,
  height: 'auto',
  pagerConfig: {},
  toolbarConfig: {
    custom: true,
  },
  customConfig: {
    storage: true,
  },
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        if (formValues?.time?.length === 2) {
          formValues = {
            ...formValues,
            startCreationTime: formValues.time[0],
            endCreationTime: formValues.time[1],
          };
        }
        const { data } = await post{{ context.EntityModel.AggregateCodePluralized }}Page({
          body: {
            pageIndex: page.currentPage,
            pageSize: page.pageSize,
            ...formValues,
          },
        });
        return data;
      },
    },
  },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

const [AddVbenModal, addModalApi] = useVbenModal({
  // 连接抽离的组件
  connectedComponent: AddModal,
});
const [EditVbenModal, editModalApi] = useVbenModal({
  // 连接抽离的组件
  connectedComponent: EditModal,
});
const handleAdd = () => {
  addModalApi.open();
};

const handleEdit = (row: Record<string, any>) => {
  editModalApi.setData({
    row,
  });
  editModalApi.open();
};

const handleDelete = async (row: any) => {
  await post{{ context.EntityModel.AggregateCodePluralized }}Delete({
    body: {
      id: row.id,
    },
  });
  gridApi.reload();
};

// const exportData = async () => {
//   gridApi.setLoading(true);
//   try {
//     const formValues = await gridApi.formApi.getValues();
//     const {
//       pager: { currentPage, pageSize },
//     } = await gridApi.grid.getProxyInfo();
//     const pagination = { pageIndex: currentPage, pageSize };
//     const { data } = await fileRequest.post(
//         '/{{ context.EntityModel.AggregateCodePluralized }}/Export',
//         { ...formValues, ...pagination },
//         { responseType: 'blob' },
//     );
//     const url = window.URL.createObjectURL(new Blob([data]));
//     const link = document.createElement('a');
//     link.href = url;
//     link.setAttribute('download', '{{context.EntityModel.Description}}导出.xlsx');
//     document.body.append(link);
//     link.click();
//     link.remove();
//     window.URL.revokeObjectURL(url);
//   } finally {
//     gridApi.setLoading(false);
//   }
// };
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-actions>
        <TableAction
            :actions="[
            {
              label: $t('common.add'),
              type: 'primary',
              icon: 'ant-design:plus-outlined',
              onClick: handleAdd.bind(null)
            },
            // {
            //   label: $t('common.export'),
            //   type: 'primary',
            //   icon: 'ant-design:download-outlined',
            //   onClick: exportData.bind(null)
            // },
          ]"
        />
      </template>
  {{~ for prop in context.EntityModel.Properties ~}}
    {{~ if  prop.DataType?.Code=="bool" ~}}
      <template #{{prop.CodeCamelCase}}="{ row }">
        <Tag v-if="row.{{prop.CodeCamelCase}}" color="green">
          是
        </Tag>
        <Tag v-if="!row.{{prop.CodeCamelCase}}" color="red">
          否
        </Tag>
      </template>
    {{~ end ~}}
  {{~ end ~}}
      <template #action="{ row }">
        <TableAction
            :actions="[
            {
              label: $t('common.edit'),
              type: 'link',
              size: 'small',
              onClick: handleEdit.bind(null, row),
            },
          ]"
            :drop-down-actions="[    
            {
              label: $t('common.delete'),
              icon: 'ant-design:delete-outlined',
              type: 'primary',
              popConfirm: {
                title: $t('common.askConfirmDelete'),
                confirm: handleDelete.bind(null, row),
              },
            },
          ]"
        />
      </template>
    </Grid>
    <AddVbenModal @reload="gridApi.reload" />
    <EditVbenModal @reload="gridApi.reload" />
  </Page>
</template>
<style scoped></style>