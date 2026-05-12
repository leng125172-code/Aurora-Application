<script lang="ts" setup>
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page, useVbenModal } from '@vben/common-ui';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import {
  postCachesKeys,
  postCachesRemove,
  postCachesValue,
} from '#/api-client/index';
import { TableAction } from '#/components/table-action';
import { $t } from '#/locales';

// 新增modal
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
  wrapperClass: 'grid-cols-4',
};

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
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        const { data } = await postCachesKeys({
          body: {
            ...formValues,
          },
        });
        return data;
      },
    },
  },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

const handleDelete = async (row: any) => {
  await postCachesRemove({
    body: {
      key: row.key,
    },
  });
  gridApi.reload();
};
const cacheValue = ref();
const handleLook = async (row: any) => {
  cacheValue.value = '';
  const response = await postCachesValue({
    body: {
      key: row.key,
    },
  });
  cacheValue.value = response.data;
  modalApi.open();
};

const [Modal, modalApi] = useVbenModal({});
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #action="{ row }">
        <TableAction
          :actions="[
            {
              label: $t('abp.cache.look'),
              auth: ['AbpIdentity.CacheManagement.LookValue'],
              onClick: handleLook.bind(null, row),
            },
            {
              label: $t('common.delete'),
              auth: ['AbpIdentity.CacheManagement.Delete'],
              popConfirm: {
                title: $t('common.askConfirmDelete'),
                confirm: handleDelete.bind(null, row),
              },
            },
          ]"
        />
      </template>
    </Grid>
    <Modal>
      <JsonViewer
        class="h-full"
        :value="cacheValue"
        copyable
        sort
        line-numbers
        :expand-depth="5"
        theme="light"
      />
    </Modal>
  </Page>
</template>
<style scoped></style>
