<script setup lang="ts">
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page, useVbenDrawer } from '@vben/common-ui';
import { IconDocDetail } from '@vben/icons';
import { usePreferences } from '@vben/preferences';

import { Button, Tag } from 'ant-design-vue';

import { useVbenVxeGrid } from '#/adapter/vxe-table';
import { postAuditLogsPage } from '#/api-client';

import { auditLogQuerySchema, auditLogTableSchema } from './schema';

defineOptions({
  name: 'AbpAuditLog',
});

const { isDark } = usePreferences();

// 定义HTTP方法对应的颜色
const getMethodColor = (method: string) => {
  switch (method) {
    case 'DELETE': {
      return 'red';
    }
    case 'GET': {
      return 'blue';
    }
    case 'POST': {
      return 'green';
    }
    case 'PUT': {
      return 'orange';
    }
    default: {
      return 'default';
    }
  }
};

// 定义HTTP状态码对应的颜色
const getStatusCodeColor = (statusCode: number) => {
  if (statusCode >= 200 && statusCode < 300) {
    return '#87d068';
  } else if (statusCode >= 300 && statusCode < 400) {
    return 'blue';
  } else if (statusCode >= 400 && statusCode < 500) {
    return 'orange';
  } else if (statusCode >= 500) {
    return '#f50';
  } else {
    return 'default';
  }
};

const formOptions: VbenFormProps = {
  schema: auditLogQuerySchema.value,
  wrapperClass: 'grid-cols-5',
};

const gridOptions: VxeGridProps<any> = {
  columns: auditLogTableSchema.value,
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
        if (formValues?.time?.length == 2) {
          formValues = {
            ...formValues,
            startTime: `${formValues.time[0]} 00:00:00`,
            endTime: `${formValues.time[1]} 23:59:59`,
          };
        }
        const { data } = await postAuditLogsPage({
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

const [Grid] = useVbenVxeGrid({ formOptions, gridOptions });

const jsonData = ref();
const [Drawer, drawerApi] = useVbenDrawer();
const viewDetail = (row: any) => {
  jsonData.value = row;
  drawerApi.open();
};
</script>

<template>
  <Page auto-content-height>
    <Grid>
      <template #url="{ row }">
        <span>
          <Tag v-if="row.httpMethod" :color="getMethodColor(row.httpMethod)">
            {{ row.httpMethod }}
          </Tag>
          {{ row.url }}
        </span>
      </template>
      <template #httpStatusCode="{ row }">
        <Tag
          v-if="row.httpStatusCode !== undefined && row.httpStatusCode !== null"
          :color="getStatusCodeColor(row.httpStatusCode)"
        >
          {{ row.httpStatusCode }}
        </Tag>
      </template>
      <template #action="{ row }">
        <div class="flex items-center">
          <IconDocDetail style="color: var(--vxe-ui-font-primary-color)" />
          <Button type="link" class="pl-1" @click="viewDetail(row)">
            详情
          </Button>
        </div>
      </template>
    </Grid>
    <Drawer class="w-[600px]" title="详情">
      <JsonViewer
        class="h-full"
        :value="jsonData"
        copyable
        sort
        line-numbers
        :theme="isDark ? 'dark' : 'light'"
      />
    </Drawer>
  </Page>
</template>
<style scoped></style>
