<script setup lang="ts">
import type { VbenFormProps } from '#/adapter/form';
import type { VxeGridProps } from '#/adapter/vxe-table';

import { ref } from 'vue';

import { Page, useVbenModal, z } from '@vben/common-ui';

import {
  NCheckbox as Checkbox,
  NCheckboxGroup as CheckboxGroup,
  NTabPane as TabPane,
  NTabs as Tabs,
  NTag as Tag,
} from 'naive-ui';

import { useVbenForm } from '#/adapter/form';
import { message as Message } from '#/adapter/naive';
import { useVbenVxeGrid } from '#/adapter/vxe-table';
import {
  postRolesAll,
  postUsersCreate,
  postUsersDelete,
  postUsersExport,
  postUsersLock,
  postUsersPage,
  postUsersResetPassword,
  postUsersResetTwoFactor,
  postUsersRole,
  postUsersUpdate,
} from '#/api-client';
import { TableAction } from '#/components/table-action';
import { $t } from '#/locales';

import {
  addUserFormSchema,
  editUserFormSchemaEdit,
  querySchema,
  tableSchema,
} from './schema';

defineOptions({
  name: 'AbpUser',
});

const formOptions: VbenFormProps = {
  schema: querySchema.value,
};

const gridOptions: VxeGridProps<any> = {
  checkboxConfig: {
    highlight: true,
    labelField: 'name',
  },
  columns: tableSchema.value,
  height: 'auto',
  keepSource: true,
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
        const { data } = await postUsersPage({
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

const editRow: Record<string, any> = ref({});
const [UserModal, userModalApi] = useVbenModal({
  draggable: true,
  onConfirm: submit,
  onBeforeClose: () => {
    editRow.value = {};
  },
});

const [AddForm, addFormApi] = useVbenForm({
  // 默认展开
  collapsed: false,
  // 所有表单项共用，可单独在表单内覆盖
  commonConfig: {
    labelWidth: 110,
    componentProps: {
      class: 'w-4/5',
    },
  },
  layout: 'horizontal',
  schema: addUserFormSchema.value,
  showCollapseButton: false,
  showDefaultActions: false,
  wrapperClass: 'grid-cols-2',
});

const [EditForm, editFormApi] = useVbenForm({
  // 默认展开
  collapsed: false,
  // 所有表单项共用，可单独在表单内覆盖
  commonConfig: {
    labelWidth: 110,
    componentProps: {
      class: 'w-4/5',
    },
  },
  // 提交函数
  layout: 'horizontal',
  schema: editUserFormSchemaEdit.value,
  showCollapseButton: false,
  showDefaultActions: false,
  wrapperClass: 'grid-cols-2',
});

// 新增和编辑提交的逻辑
async function submit() {
  const isEdit = !!editRow.value.id;
  const formApi = isEdit ? editFormApi : addFormApi;
  const api = isEdit ? postUsersUpdate : postUsersCreate;
  const { valid } = await formApi.validate();
  if (!valid) return;

  const formValues = await formApi.getValues();
  const fetchParams: any = isEdit
    ? {
        userInfo: {
          ...formValues,
          roleNames: checkedRoles.value,
          concurrencyStamp: editRow.value.concurrencyStamp,
        },
        userId: editRow.value.id,
      }
    : { ...formValues, roleNames: checkedRoles.value };

  try {
    userModalApi.setState({ loading: true, confirmLoading: true });
    const resp = await api({ body: fetchParams });
    if (resp.status === 200 || resp.status === 204) {
      Message.success(
        editRow.value.id ? $t('common.editSuccess') : $t('common.addSuccess'),
      );
      userModalApi.close();
      gridApi.reload();
    }
  } finally {
    userModalApi.setState({ loading: false, confirmLoading: false });
  }
}

async function onEdit(record: any) {
  editRow.value = record;
  userModalApi.open();
  editFormApi.setValues({ ...record });
  getAllRoles();
  const { data: { items = [] } = {} } = await postUsersRole({
    body: { id: record.id },
  });
  checkedRoles.value = items?.map((item: any) => item.name) as any;
}

async function onDel(row: any) {
  await postUsersDelete({ body: { id: row.id } });
  gridApi.reload();
  Message.success($t('common.deleteSuccess'));
}

async function resetTwoFactor(row: any) {
  await postUsersResetTwoFactor({ body: { userId: row.id } });
  gridApi.reload();
  Message.success($t('abp.user.resetTwoFactor') + $t('common.success'));
}
const onLock = async (row: Record<string, any>) => {
  await postUsersLock({ body: { userId: row.id, locked: !row.isActive } });
  gridApi.reload();
  Message.success($t('common.editSuccess'));
};

const rolesList = ref([] as any);
const checkedRoles = ref([]);
const openAddModal = async () => {
  editRow.value = {};
  checkedRoles.value = [];
  userModalApi.open();
  getAllRoles();
};

async function getAllRoles() {
  const { data: { items = [] } = {} } = await postRolesAll();
  rolesList.value = items?.map((item) => ({
    label: item.name,
    value: item.name,
  }));
}

const exportData = async () => {
  gridApi.setLoading(true);
  try {
    const formValues = await gridApi.formApi.getValues();
    const {
      pager: { currentPage, pageSize },
    } = await gridApi.grid.getProxyInfo();
    const pagination = { pageIndex: currentPage, pageSize };
    const { data } = await postUsersExport({
      body: { ...formValues, ...pagination },
      responseType: 'blob',
    });
    const url = window.URL.createObjectURL(new Blob([data as Blob]));
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', '用户列表导出.xlsx');
    document.body.append(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  } finally {
    gridApi.setLoading(false);
  }
};

const [ResetPasswordModal, resetPasswordModalApi] = useVbenModal({
  draggable: true,
  showCancelButton: false,
  showConfirmButton: false,
  onConfirm: () => {},
  onBeforeClose: () => {
    editRow.value = {};
    return true;
  },
});
const [ResetPasswordForm, resetPasswordApi] = useVbenForm({
  // 默认展开
  collapsed: false,
  // 所有表单项共用，可单独在表单内覆盖
  commonConfig: {
    // 所有表单项
    componentProps: {
      class: 'w-4/5',
    },
  },
  // 提交函数
  handleSubmit: async () => {
    // 表单校验
    const { valid } = await resetPasswordApi.validate();
    if (!valid) return;
    const formValues = await resetPasswordApi.getValues();
    await postUsersResetPassword({ body: formValues });
    Message.success($t('abp.user.resetPassword') + $t('common.success'));
    await resetPasswordApi.resetForm();
    resetPasswordModalApi.close();
  },
  layout: 'horizontal',
  schema: [
    {
      component: 'Input',
      componentProps: {},
      fieldName: 'userId',
      label: 'userId',
      dependencies: {
        show: () => false,
        triggerFields: ['userId'],
      },
    },
    {
      component: 'VbenInputPassword',
      componentProps: {
        placeholder: $t('common.pleaseInput') + $t('abp.user.newPassword'),
      },
      fieldName: 'password',
      label: $t('abp.user.newPassword'),
      rules: z.string().min(1, {
        message: $t('common.pleaseInput') + $t('abp.user.newPassword'),
      }),
    },
  ],
  resetButtonOptions: {
    show: false,
  },
  submitButtonOptions: {
    content: '确认',
  },
  wrapperClass: 'grid-cols-1',
});
function changePassword(row: any) {
  resetPasswordApi.setValues({
    userId: row.id,
  });
  resetPasswordModalApi.open();
}
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
              onClick: openAddModal.bind(null),
              auth: ['AbpIdentity.Users.Create'],
            },
            {
              label: $t('common.export'),
              type: 'primary',
              icon: 'ant-design:download-outlined',
              onClick: exportData.bind(null),
              auth: ['AbpIdentity.Users.Export'],
            },
          ]"
        />
      </template>

      <template #isActive="{ row }">
        <Tag
          :color="row.isActive ? '#87d068' : '#f50'"
          :type="row.isActive ? 'success' : 'error'"
        >
          {{ row.isActive ? $t('common.enabled') : $t('common.disabled') }}
        </Tag>
      </template>
      <template #twoFactorEnabled="{ row }">
        <Tag
          :color="row.twoFactorEnabled ? '#87d068' : '#f50'"
          :type="row.twoFactorEnabled ? 'success' : 'error'"
        >
          {{
            row.twoFactorEnabled ? $t('common.enabled') : $t('common.disabled')
          }}
        </Tag>
      </template>
      <template #action="{ row }">
        <TableAction
          :actions="[
            {
              label: $t('common.edit'),
              type: 'primary',
              text: true,
              size: 'small',
              auth: ['AbpIdentity.Users.Update'],
              onClick: onEdit.bind(null, row),
            },
          ]"
          :drop-down-actions="[
            {
              label: row.isActive
                ? $t('common.disabled')
                : $t('common.enabled'),
              icon: 'ant-design:lock-outlined',
              auth: ['AbpIdentity.Users.Enable'],
              onClick: onLock.bind(null, row),
            },
            {
              label: $t('common.delete'),
              icon: 'ant-design:delete-outlined',
              auth: ['AbpIdentity.Users.Delete'],
              popConfirm: {
                title: $t('common.askConfirmDelete'),
                confirm: onDel.bind(null, row),
              },
            },
            {
              label: $t('abp.user.resetTwoFactor'),
              icon: 'ant-design:usergroup-add-outlined',
              auth: ['AbpIdentity.Users.ResetTwoFactor'],
              onClick: resetTwoFactor.bind(null, row),
            },
            {
              label: $t('abp.user.resetPassword'),
              icon: 'ant-design:lock-outlined',
              auth: ['AbpIdentity.Users.ResetPassword'],
              onClick: changePassword.bind(null, row),
            },
          ]"
        />
      </template>
    </Grid>

    <UserModal
      :title="editRow.id ? $t('common.edit') : $t('common.add')"
      class="w-[800px]"
    >
      <Tabs>
        <TabPane
          key="1"
          :name="$t('abp.user.user')"
          :tab="$t('abp.user.user')"
          display-directive="show"
        >
          <component :is="editRow.id ? EditForm : AddForm" />
        </TabPane>
        <TabPane
          key="2"
          :name="$t('abp.role.role')"
          :tab="$t('abp.role.role')"
          display-directive="show"
        >
          <div class="role-checkbox-group">
            <CheckboxGroup v-model:value="checkedRoles" name="checkboxgroup">
              <Checkbox
                v-for="item in rolesList"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </CheckboxGroup>
          </div>
        </TabPane>
      </Tabs>
    </UserModal>
    <ResetPasswordModal>
      <ResetPasswordForm />
    </ResetPasswordModal>
  </Page>
</template>

<style scoped>
.role-checkbox-group :deep(.n-checkbox-group) {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.role-checkbox-group :deep(.n-checkbox) {
  flex: 0 0 calc(20% - 8px); /* 20% = 1/5 width, minus gap adjustment */
  min-width: 0;
}
</style>
