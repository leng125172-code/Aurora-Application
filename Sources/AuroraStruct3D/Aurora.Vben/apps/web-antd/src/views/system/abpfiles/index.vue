<script lang="ts" setup>
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { Page, useVbenModal } from '@vben/common-ui';

import dayjs from 'dayjs';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import {
  postFilesDelete,
  postFilesPage,
  putFilesDownload,
} from '#/api-client/index';
import { TableAction } from '#/components/table-action';
import { $t } from '#/locales';

// 新增modal
import AddModal from './AddModal.vue';
// 编辑modal
import { querySchema, tableSchema } from './schema';

const formOptions: VbenFormProps = {
  // 默认展开
  collapsed: false,
  schema: querySchema.value,
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
            endCreationTime: dayjs(formValues.time[1])
              .hour(23)
              .minute(59)
              .second(59)
              .format('YYYY-MM-DD HH:mm:ss'),
          };
        }
        const { data } = await postFilesPage({
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

const handleAdd = () => {
  addModalApi.open();
};

const handleDelete = async (row: any) => {
  await postFilesDelete({
    body: {
      id: row.id,
    },
  });
  gridApi.reload();
};

const handleDown = async (row: any) => {
  const { data } = await putFilesDownload({
    body: { id: row.id },
    responseType: 'blob',
  });
  const url = window.URL.createObjectURL(new Blob([data as Blob]));
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', row.fileName);
  document.body.append(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
};
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #toolbar-actions>
        <TableAction
          :actions="[
            {
              label: $t('common.upload'),
              type: 'primary',
              icon: 'ant-design:upload-outlined',
              onClick: handleAdd.bind(null),
              auth: ['FileManagement.File.Upload'],
            },
          ]"
        />
      </template>
      <template #action="{ row }">
        <TableAction
          :actions="[
            {
              label: $t('common.delete'),
              icon: 'ant-design:delete-outlined',
              type: 'link',
              size: 'small',
              auth: ['FileManagement.File.Delete'],
              popConfirm: {
                title: $t('common.askConfirmDelete'),
                confirm: handleDelete.bind(null, row),
              },
            },
            {
              label: $t('common.download'),
              icon: 'ant-design:download-outlined',
              type: 'link',
              size: 'small',
              auth: ['FileManagement.File.Download'],
              onClick: handleDown.bind(null, row),
            },
          ]"
        />
      </template>
    </Grid>
    <AddVbenModal @reload="gridApi.reload" />
  </Page>
</template>
<style scoped></style>
