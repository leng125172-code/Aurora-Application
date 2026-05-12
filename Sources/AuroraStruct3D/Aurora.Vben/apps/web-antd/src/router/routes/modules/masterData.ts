import type { RouteRecordRaw } from 'vue-router';

import { BasicLayout } from '#/layouts';
import { $t } from '#/locales';

const routes: RouteRecordRaw[] = [
  {
    component: BasicLayout,
    meta: {
      icon: 'ic:baseline-view-in-ar',
      keepAlive: true,
      order: 7,
      title: $t('abp.menu.masterdata'),
      authority: ['AbpMasterDataManagement'],
    },
    name: 'MasterData',
    path: '/masterData',
    children: [
      {
        path: 'model',
        name: 'model',
        component: () => import('#/views/system/abpmasterdata/datamodel.vue'),
        meta: {
          icon: 'ant-design:setting-outlined',
          title: $t('abp.menu.datamodel'),
          authority: ['AbpMasterDataManagement.DataModel'],
        },
      },
      {
        path: 'data',
        name: 'data',
        component: () => import('#/views/system/abpmasterdata/data.vue'),
        meta: {
          icon: 'ant-design:apartment-outlined',
          title: $t('abp.menu.data'),
          authority: ['AbpMasterDataManagement.Data'],
        },
      },
    ],
  },
];

export default routes;
