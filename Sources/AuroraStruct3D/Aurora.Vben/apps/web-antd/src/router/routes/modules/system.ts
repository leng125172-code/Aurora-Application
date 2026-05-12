import type { RouteRecordRaw } from 'vue-router';

import { BasicLayout } from '#/layouts';
import { $t } from '#/locales';

const routes: RouteRecordRaw[] = [
  {
    component: BasicLayout,
    meta: {
      icon: 'lucide:layout-dashboard',
      order: 1,
      title: $t('abp.menu.system'),
      authority: ['AbpIdentity'],
    },
    name: 'system',
    path: '/system',
    children: [
      {
        name: 'abpUser',
        path: 'user',
        component: () => import('#/views/system/abpuser/index.vue'),
        meta: {
          // affixTab: true,
          icon: 'ph:user',
          title: $t('abp.menu.user'),
          authority: ['AbpIdentity.Users'],
        },
      },
      {
        name: 'abpRole',
        path: 'role',
        component: () => import('#/views/system/abprole/index.vue'),
        meta: {
          icon: 'oui:app-users-roles',
          title: $t('abp.menu.role'),
          authority: ['AbpIdentity.Roles'],
        },
      },
      {
        name: 'OrganizationUnit',
        path: 'organizationUnit',
        component: () => import('#/views/system/abporganizationunit/index.vue'),
        meta: {
          title: $t('abp.menu.organizationUnit'),
          authority: ['AbpIdentity.OrganizationUnitManagement'],
          icon: 'ant-design:team-outlined',
        },
      },
      {
        name: 'abpSetting',
        path: 'setting',
        component: () => import('#/views/system/abpsetting/index.vue'),
        meta: {
          icon: 'uil:setting',
          title: $t('abp.menu.setting'),
          authority: ['AbpIdentity.Setting'],
        },
      },
      {
        name: 'abpfeature',
        path: 'Feature',
        component: () => import('#/views/system/abpfeature/index.vue'),
        meta: {
          icon: 'ant-design:tool-outlined',
          title: $t('abp.menu.feature'),
          authority: ['AbpIdentity.FeatureManagement'],
        },
      },
      {
        name: 'AbpAuditLog',
        path: 'auditlog',
        component: () => import('#/views/system/abplog/audit.vue'),
        meta: {
          title: $t('abp.menu.auditLog'),
          authority: ['AbpIdentity.AuditLog'],
          icon: 'ant-design:snippets-twotone',
        },
      },
      {
        name: 'AbpLoginLog',
        path: 'loginlog',
        component: () => import('#/views/system/abplog/login.vue'),
        meta: {
          title: $t('abp.menu.loginLog'),
          authority: ['AbpIdentity.IdentitySecurityLogs'],
          icon: 'ant-design:file-protect-outlined',
        },
      },
      {
        name: 'AbpLanguage',
        path: 'language',
        component: () => import('#/views/system/abplanguage/language.vue'),
        meta: {
          title: $t('abp.menu.language'),
          authority: ['AbpIdentity.Languages'],
          icon: 'ant-design:read-outlined',
        },
      },
      {
        name: 'AbpLanguageText',
        path: 'languagetext',
        component: () => import('#/views/system/abplanguage/languagetext.vue'),
        meta: {
          title: $t('abp.menu.languageText'),
          authority: ['AbpIdentity.LanguageTexts'],
          icon: 'ant-design:font-size-outlined',
        },
      },
      {
        name: 'DataDictionary',
        path: 'dataDictionary',
        component: () => import('#/views/system/abpdatadictionary/index.vue'),
        meta: {
          title: $t('abp.menu.dataDictionary'),
          authority: ['AbpIdentity.DataDictionaryManagement'],
          icon: 'ant-design:table-outlined',
        },
      },
      {
        name: 'AbpNotification',
        path: 'notification',
        component: () =>
          import('#/views/system/abpnotification/notification.vue'),
        meta: {
          title: $t('abp.menu.notification'),
          authority: ['AbpIdentity.NotificationSubscriptionManagement'],
          icon: 'ant-design:comment-outlined',
        },
      },
      {
        name: 'AbpMessage',
        path: 'message',
        component: () => import('#/views/system/abpnotification/message.vue'),
        meta: {
          title: $t('abp.menu.message'),
          authority: ['AbpIdentity.NotificationManagement'],
          icon: 'ant-design:customer-service-twotone',
        },
      },
      {
        path: 'menu',
        name: 'AbpMenu',
        component: () => import('#/views/system/abpmenu/index.vue'),
        meta: {
          icon: 'ant-design:bars-outlined',
          title: $t('abp.menu.menu'),
          authority: ['AbpIdentity.DynamicMenuManagement'],
        },
      },
      {
        path: 'import/excel',
        name: 'AbpImportExcel',
        component: () => import('#/views/system/abpImport/excel.vue'),
        meta: {
          icon: 'ant-design:to-top-outlined',
          title: $t('abp.menu.importExcel'),
          authority: ['AbpIdentity.ImportExportManagement'],
        },
      },
      {
        path: 'cache',
        name: 'AbpCache',
        component: () => import('#/views/system/abpCache/index.vue'),
        meta: {
          icon: 'ant-design:cloud-twotone',
          title: $t('abp.menu.cache'),
          authority: ['AbpIdentity.CacheManagement'],
        },
      },
      {
        path: 'onlineUser',
        name: 'AbpOnlineUser',
        component: () => import('#/views/system/abpOnlineUser/index.vue'),
        meta: {
          icon: 'ant-design:usergroup-add-outlined',
          title: $t('abp.menu.onlineUser'),
          authority: ['AbpIdentity.OnlineManagement'],
        },
      },
    ],
  },
];

export default routes;
