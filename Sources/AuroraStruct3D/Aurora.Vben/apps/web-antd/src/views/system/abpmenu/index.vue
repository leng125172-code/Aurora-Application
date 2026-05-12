<script lang="ts" setup>
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page, useVbenModal } from '@vben/common-ui';

import { Tag } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { postMenusDelete, postMenusPage } from '#/api-client/index';
import { TableAction } from '#/components/table-action';
import { $t } from '#/locales';

// 新增modal
import AddModal from './AddModal.vue';
// 编辑modal
import EditModal from './EditModal.vue';
import { tableSchema } from './schema';

const gridOptions: VxeGridProps<any> = {
  checkboxConfig: {
    highlight: true,
    labelField: 'name',
  },
  columns: tableSchema.value,
  keepSource: true,
  height: 'auto',
  pagerConfig: {
    enabled: false,
  },
  toolbarConfig: {
    custom: true,
  },
  customConfig: {
    storage: true,
  },
  treeConfig: {
    transform: true, // 指定表格为树形表格
    parentField: 'parentId', // 父节点字段名
    rowField: 'id', // 行数据字段名
  },
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        const { data } = await postMenusPage({
          body: {
            pageIndex: page.currentPage,
            pageSize: 99_999,
            ...formValues,
          },
        });
        return data;
      },
    },
  },
};

const [Grid, gridApi] = useVbenVxeGrid({ gridOptions });

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
  await postMenusDelete({
    body: {
      id: row.id,
    },
  });
  gridApi.reload();
};
const isExpand = ref(false);
/**
 * 展开/折叠
 */
const handleExpandAndCollapse = () => {
  if (isExpand.value) {
    gridApi.grid.setAllTreeExpand(false);
    isExpand.value = false;
  } else {
    gridApi.grid.setAllTreeExpand(true);
    isExpand.value = true;
  }
};
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
              auth: ['AbpIdentity.DynamicMenuManagement.Create'],
              onClick: handleAdd.bind(null),
            },
            {
              label: isExpand
                ? $t('common.collapseAll')
                : $t('common.expandAll'),
              type: 'primary',
              icon: isExpand
                ? 'ant-design:menu-fold-outlined'
                : 'ant-design:menu-unfold-outlined',
              onClick: handleExpandAndCollapse.bind(null),
            },
          ]"
        />
      </template>
      <template #enabled="{ row }">
        <Tag v-if="row.enabled" color="green"> {{ $t('common.yes') }} </Tag>
        <Tag v-if="!row.enabled" color="red"> {{ $t('common.no') }} </Tag>
      </template>

      <template #hideInMenu="{ row }">
        <Tag v-if="row.hideInMenu" color="green"> {{ $t('common.yes') }} </Tag>
        <Tag v-if="!row.hideInMenu" color="red"> {{ $t('common.no') }} </Tag>
      </template>
      <template #keepAlive="{ row }">
        <Tag v-if="row.keepAlive" color="green"> {{ $t('common.yes') }} </Tag>
        <Tag v-if="!row.keepAlive" color="red"> {{ $t('common.no') }} </Tag>
      </template>
      <template #menuType="{ row }">
        <Tag v-if="row.menuType === 20" color="green">
          {{ $t('abp.dynamicMenu.menu') }}
        </Tag>
        <Tag v-if="row.menuType === 10" color="red">
          {{ $t('abp.dynamicMenu.folder') }}
        </Tag>
      </template>
      <template #action="{ row }">
        <TableAction
          :actions="[
            {
              label: $t('common.edit'),
              type: 'link',
              size: 'small',
              auth: ['AbpIdentity.DynamicMenuManagement.Update'],
              onClick: handleEdit.bind(null, row),
            },
          ]"
          :drop-down-actions="[
            {
              label: $t('common.delete'),
              icon: 'ant-design:delete-outlined',
              type: 'primary',
              auth: ['AbpIdentity.DynamicMenuManagement.Delete'],
              popConfirm: {
                title: $t('common.askConfirmDelete'),
                confirm: handleDelete.bind(null, row),
              },
            },
          ]"
        />
      </template>
    </Grid>
    <AddVbenModal @reload="gridApi.reload" class="w-[900px]" />
    <EditVbenModal @reload="gridApi.reload" class="w-[900px]" />
  </Page>
</template>
<style scoped></style>
