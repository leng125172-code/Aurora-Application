<script setup lang="ts">
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { Page, useVbenModal } from '@vben/common-ui';

import { ElTag as Tag } from 'element-plus';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { postImportRecordsPage, putFilesDownload } from '#/api-client';
import { TableAction } from '#/components/table-action';

// 新增modal
import AddImportExcelModal from './AddImportExcelModal.vue';
import { importExcelQuerySchema, importExcelTableSchema } from './schema';

defineOptions({
  name: 'Excel',
});

const [AddVbenImportExcelModal, addModalApi] = useVbenModal({
  // 连接抽离的组件
  connectedComponent: AddImportExcelModal,
});

const formOptions: VbenFormProps = {
  schema: importExcelQuerySchema.value,
};

const gridOptions: VxeGridProps<any> = {
  columns: importExcelTableSchema.value,
  toolbarConfig: {
    custom: true,
  },
  customConfig: {
    storage: true,
  },
  height: 'auto',
  keepSource: true,
  pagerConfig: {},
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        const { data } = await postImportRecordsPage({
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

const handleAdd = () => {
  addModalApi.open();
};

const downloadFile = async (blobId: any, blobName: any) => {
  const { data } = await putFilesDownload({
    body: { id: blobId },
    responseType: 'blob',
  });
  const url = window.URL.createObjectURL(new Blob([data as Blob]));
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', blobName);
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
              label: $t('common.add'),
              type: 'primary',
              icon: 'ant-design:plus-outlined',
              onClick: handleAdd.bind(null),
            },
          ]"
        />
      </template>
      <template #status="{ row }">
        <Tag type="info" v-if="row.status === 20">
          {{ row.importStatusDescription }}
        </Tag>
        <Tag type="warning" v-else-if="row.status === 10">
          {{ row.importStatusDescription }}
        </Tag>
        <Tag type="danger" v-else> {{ row.importStatusDescription }} </Tag>
      </template>

      <template #blobName="{ row }">
        <a
          href="javascript:void(0)"
          @click="downloadFile(row.blobId, row.blobName)"
        >
          <div style="color: blue">
            {{ row.blobName }}
          </div></a>
      </template>
      <template #blobErrorName="{ row }">
        <a
          href="javascript:void(0)"
          @click="downloadFile(row.blobErrorId, row.blobErrorName)"
        >
          <div style="color: blue">
            {{ row.blobErrorName }}
          </div></a>
      </template>
    </Grid>
    <AddVbenImportExcelModal @reload="gridApi.reload" />
  </Page>
</template>
<style scoped></style>
