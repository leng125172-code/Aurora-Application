<script setup lang="ts">
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { Page } from '@vben/common-ui';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { postOnlineForceOut, postOnlinePage } from '#/api-client';
import { TableAction } from '#/components/table-action';

import { querySchema, tableSchema } from './schema';

defineOptions({
  name: 'AbpOnlineUser',
});

const formOptions: VbenFormProps = {
  schema: querySchema.value,
  wrapperClass: 'grid-cols-4',
};

const gridOptions: VxeGridProps<any> = {
  columns: tableSchema.value,
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
        if (formValues?.time?.length === 2) {
          formValues = {
            ...formValues,
            startCreationTime: formValues.time[0],
            endCreationTime: formValues.time[1],
          };
        }
        const { data } = await postOnlinePage({
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

const handleForceOut = async (row: any) => {
  await postOnlineForceOut({
    body: {
      id: row.userId,
    },
  });
  gridApi.reload();
};
</script>
<template>
  <Page auto-content-height>
    <Grid>
      <template #action="{ row }">
        <TableAction
          :actions="[
            {
              label: $t('abp.user.forceOut'),
              auth: ['AbpIdentity.OnlineManagement.ForceOut'],
              popConfirm: {
                title: $t('abp.user.confirmForceOut'),
                confirm: handleForceOut.bind(null, row),
              },
            },
          ]"
        />
      </template>
    </Grid>
  </Page>
</template>
<style scoped></style>
