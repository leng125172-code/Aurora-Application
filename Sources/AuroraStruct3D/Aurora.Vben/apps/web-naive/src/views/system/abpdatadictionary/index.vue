<script setup lang="ts">
import type { VbenFormProps } from '@vben/common-ui';

import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page, useVbenModal } from '@vben/common-ui';

import { NButton as Button, NSwitch as Switch } from 'naive-ui';

import { dialog, message } from '#/adapter/naive';
import { useVbenVxeGrid } from '#/adapter/vxe-table';
import {
  postDataDictionaryDelete,
  postDataDictionaryDeleteDataDictionaryType,
  postDataDictionaryPage,
  postDataDictionaryPageDetail,
  postDataDictionaryStatus,
} from '#/api-client/index';
import { TableAction } from '#/components/table-action';
import { $t } from '#/locales';

import DataDictionaryDetail from './DataDictionaryDetailModal.vue';
import DataDictionaryModal from './DataDictionaryModal.vue';

defineOptions({
  name: 'DataDictionary',
});

/**  ============左侧表格相关逻辑 start ============== */
const formOptions: VbenFormProps = {
  schema: [
    {
      component: 'Input',
      fieldName: 'filter',
      label: '',
      componentProps: {
        allowClear: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-2',
  showDefaultActions: true,
  submitOnEnter: true,
  showCollapseButton: false,
  commonConfig: {
    hideLabel: true,
  },
};

const gridOptions: VxeGridProps<any> = {
  columns: [
    { type: 'radio', width: '50' },
    {
      field: 'code',
      title: $t('abp.dataDictionary.code'),
      minWidth: '75',
    },
    {
      field: 'displayText',
      title: $t('abp.dataDictionary.name'),
      minWidth: '75',
    },
    {
      field: 'description',
      title: $t('abp.dataDictionary.description'),
      minWidth: '75',
    },
    {
      title: $t('common.action'),
      field: 'action',
      fixed: 'right',
      width: '180',
      slots: { default: 'action' },
    },
  ],
  height: '100%',
  keepSource: true,
  pagerConfig: {},
  radioConfig: {
    highlight: true,
  },
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        const { data } = await postDataDictionaryPage({
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

const gridEvents = {
  radioChange: handleDataDictionaryItemChange,
};
const [Grid, gridApi] = useVbenVxeGrid({
  gridOptions,
  formOptions,
  gridEvents,
});

const [DataDictionaryModalComponent, dataDictionaryModalApi] = useVbenModal({
  // 连接抽离的组件
  connectedComponent: DataDictionaryModal,
});

const openDataDictionaryModal = () => {
  dataDictionaryModalApi.setData({
    isEdit: false,
  });
  dataDictionaryModalApi.open();
};

const editDataDictionary = (row: Record<string, any>) => {
  dataDictionaryModalApi.setData({
    isEdit: true,
    row,
  });
  dataDictionaryModalApi.open();
};

const deleteDataDictionary = async (row: Record<string, any>) => {
  dialog.warning({
    positiveText: $t('common.confirm'),
    negativeText: $t('common.cancel'),
    closable: false,
    title: `${$t('common.confirmDelete')}${row.displayText} ?`,
    onPositiveClick: async () => {
      await postDataDictionaryDeleteDataDictionaryType({
        body: { id: row.id },
      });
      message.success($t('common.deleteSuccess'));
      gridApi.reload();
    },
  });
};

const current = ref<Record<string, any>>({});
async function handleDataDictionaryItemChange(item: Record<string, any>) {
  current.value = item;

  gridTableApi.reload({
    dataDictionaryId: 'current.value?.row?.id',
  });
}

/**  ============>左侧表格相关逻辑 end ============== */

/** ============>右侧表格相关逻辑 start ============== */
const rightFormOptions: VbenFormProps = {
  schema: [
    {
      component: 'Input',
      fieldName: 'filter',
      componentProps: {
        allowClear: true,
      },
    },
  ],
  wrapperClass: 'grid-cols-2',
  showDefaultActions: true,
  submitOnEnter: true,
  showCollapseButton: false,
  commonConfig: {
    hideLabel: true,
  },
};

const rightGridOptions: VxeGridProps<any> = {
  columns: [
    { field: 'code', title: $t('abp.dataDictionary.code'), minWidth: '75' },
    {
      field: 'displayText',
      title: $t('abp.dataDictionary.name'),
      minWidth: '150',
    },
    { field: 'order', title: $t('abp.dataDictionary.order'), minWidth: '150' },
    {
      field: 'isEnabled',
      title: $t('abp.dataDictionary.status'),
      minWidth: '150',
      slots: { default: 'isEnabled' },
    },
    {
      field: 'description',
      title: $t('abp.dataDictionary.description'),
      minWidth: '150',
    },
    {
      title: $t('common.action'),
      field: 'action',
      fixed: 'right',
      width: '150',
      slots: { default: 'action' },
    },
  ],
  toolbarConfig: {
    custom: true,
  },
  customConfig: {
    storage: true,
  },
  height: '100%',
  keepSource: true,
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        const { data } = await postDataDictionaryPageDetail({
          body: {
            pageIndex: page.currentPage,
            pageSize: page.pageSize,
            ...formValues,
            dataDictionaryId: current.value?.row?.id,
          },
        });
        return data;
      },
    },
  },
};

const [GridTable, gridTableApi] = useVbenVxeGrid({
  gridOptions: rightGridOptions,
  formOptions: rightFormOptions,
});

const handleItemStausChange = async (
  enabled: any,
  row: Record<string, any>,
) => {
  await postDataDictionaryStatus({
    body: {
      dataDictionaryId: current.value?.row?.id,
      dataDictionayDetailId: row.id,
      isEnabled: enabled,
    },
  });
};

const [DataDictionaryDetailComponent, dataDictionaryDetailModalApi] =
  useVbenModal({
    // 连接抽离的组件
    connectedComponent: DataDictionaryDetail,
  });

const openDataDictionaryDetailModal = () => {
  dataDictionaryDetailModalApi.setData({
    isEdit: false,
    type: current.value?.row?.displayText,
    id: current.value?.row?.id,
  });
  dataDictionaryDetailModalApi.open();
};

const editDetailRow = (row: Record<string, any>) => {
  dataDictionaryDetailModalApi.setData({
    isEdit: true,
    type: current.value?.row?.displayText,
    id: current.value?.row?.id,
    row,
  });
  dataDictionaryDetailModalApi.open();
};

const removeDetailRow = async (row: Record<string, any>) => {
  dialog.warning({
    positiveText: $t('common.confirm'),
    negativeText: $t('common.cancel'),
    closable: false,
    title: `${$t('common.confirmDelete')}${row.displayText} ?`,
    onPositiveClick: async () => {
      await postDataDictionaryDelete({
        body: {
          dataDictionaryId: current.value?.row?.id,
          dataDictionayDetailId: row.id,
        },
      });
      message.success($t('common.deleteSuccess'));
      gridTableApi.reload();
    },
  });
};

/** ============>右侧表格相关逻辑 end ============== */
</script>

<template>
  <Page :auto-content-height="true" class="h-full">
    <div class="flex h-full flex-1 flex-col gap-4 overflow-hidden lg:flex-row">
      <div class="flex h-full w-full flex-col lg:w-1/3">
        <Grid class="h-full flex-1">
          <template #toolbar-actions>
            <Button type="primary" @click="openDataDictionaryModal">
              {{ $t('common.add') }}
            </Button>
          </template>

          <template #action="{ row }">
            <TableAction
              :actions="[
                {
                  label: $t('common.edit'),
                  text: true,
                  size: 'small',
                  onClick: editDataDictionary.bind(null, row),
                },
                {
                  label: $t('common.delete'),
                  text: true,
                  size: 'small',
                  onClick: deleteDataDictionary.bind(null, row),
                },
              ]"
            />
          </template>
        </Grid>
      </div>
      <div class="flex h-full w-full flex-col lg:w-2/3">
        <GridTable class="h-full flex-1">
          <template #toolbar-actions>
            <Button
              :disabled="!current.row"
              type="primary"
              @click="openDataDictionaryDetailModal"
            >
              {{ $t('common.add') }}
            </Button>
          </template>

          <template #isEnabled="{ row }">
            <Switch
              v-model:value="row.isEnabled"
              @change="handleItemStausChange($event, row)"
            />
          </template>

          <template #action="{ row }">
            <TableAction
              :actions="[
                {
                  label: $t('common.edit'),
                  text: true,
                  size: 'small',
                  onClick: editDetailRow.bind(null, row),
                },
                {
                  label: $t('common.delete'),
                  text: true,
                  size: 'small',
                  onClick: removeDetailRow.bind(null, row),
                },
              ]"
            />
          </template>
        </GridTable>
      </div>
    </div>
    <DataDictionaryModalComponent @reload="gridApi.reload" />
    <DataDictionaryDetailComponent @reload="gridTableApi.reload" />
  </Page>
</template>

<style scoped></style>
