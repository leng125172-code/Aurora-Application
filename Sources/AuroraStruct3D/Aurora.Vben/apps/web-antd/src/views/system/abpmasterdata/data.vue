<script setup lang="ts">
import type { DataNode } from 'ant-design-vue/es/tree';

import { onMounted, ref } from 'vue';

import { useAccess } from '@vben/access';
import { Page, useVbenModal } from '@vben/common-ui';

import { message, Switch, Tree } from 'ant-design-vue';
import dayjs from 'dayjs';

import { useVbenForm } from '#/adapter/form';
import { useVbenVxeGrid } from '#/adapter/vxe-table';
import {
  postMasterDataCreateMasterData,
  postMasterDataGetMasterDataAttributes,
  postMasterDataGetMasterDataInfo,
  postMasterDataGetMasterDataTypes,
  postMasterDataSetMasterDataStatus,
  postMasterDataUpdateMasterData,
} from '#/api-client';
import { TableAction } from '#/components/table-action';
import { $t } from '#/locales';

// 定义树形数据
const gData = ref<Array<DataNode>>([]);

// 定义展开的节点
const expandedKeys = ref<(number | string)[]>([]);

// 控制是否自动展开父节点
const autoExpandParent = ref<boolean>(true);

// 当前选中的节点
const currentSelectedKey = ref<string>('');
const currentSelectedName = ref<string>('');
const currentSelectedCode = ref<string>('');
const showTable = ref<boolean>(false);
const dynamicFormFields = ref<any[]>([]); // 移动到这里，使其在整个组件中可访问

// 定义序号列
const dynamicTableColumn = [
  { type: 'seq', width: 60, title: $t('common.seq'), fixed: 'left' },
  {
    field: 'code',
    title: $t('abp.masterdata.code'),
    fixed: 'left',
  },
  {
    field: 'name',
    title: $t('abp.masterdata.name'),
    fixed: 'left',
  },
  {
    field: 'enabled',
    title: $t('common.isEnable'),
    slots: { default: 'enabled' },
  },
];

// 创建查询表单
const formOptions = {
  schema: [
    {
      component: 'Input',
      componentProps: {
        placeholder: '',
      },
      fieldName: 'masterDataCode',
      label: $t('abp.masterdata.code'),
    },
  ],
  wrapperClass: 'grid-cols-4 gap-4 mb-3',
};

// 创建数据表格
const gridOptions = {
  columns: dynamicTableColumn,
  pagerConfig: {
    enabled: true,
  },
  proxyConfig: {
    ajax: {
      query: async ({ page }, formValues) => {
        if (!currentSelectedKey.value) {
          return {
            items: [],
            totalCount: 0,
          };
        }
        const { data } = await postMasterDataGetMasterDataInfo({
          body: {
            masterDataTypeId: currentSelectedKey.value,
            pageIndex: page.currentPage,
            pageSize: page.pageSize,
            ...formValues,
          },
        });

        // 转换数据，将 attributes 数组转换为与 code 同级的属性
        const transformedItems = data.items.map((item) => {
          const transformedItem = {
            ...item,
          };

          // 将 attributes 数组中的每个属性提升到与 code 同级
          if (item.attributes && Array.isArray(item.attributes)) {
            item.attributes.forEach((attribute) => {
              transformedItem[attribute.code] = attribute.value;
            });
          }

          return transformedItem;
        });
        return {
          items: transformedItems,
          totalCount: data.totalCount,
        };
      },
    },
  },
  toolbarConfig: {
    // 显示搜索表单控制按钮
    search: true,
  },
};

const [Grid, gridApi] = useVbenVxeGrid({ formOptions, gridOptions });

// 创建新增数据的模态框
const [AddDataModal, addDataModalApi] = useVbenModal({
  onConfirm: () => {
    addDataFormApi.submitForm();
  },
  onOpenChange: (isOpen) => {
    if (!isOpen) {
      addDataFormApi.resetForm();
    }
  },
  title: $t('common.add'),
});

// 创建编辑数据的模态框
const [EditDataModal, editDataModalApi] = useVbenModal({
  onConfirm: () => {
    editDataFormApi.submitForm();
  },
  onOpenChange: (isOpen) => {
    if (!isOpen) {
      editDataFormApi.resetForm();
    }
  },
  title: $t('common.edit'),
});

// 创建查看数据详情的模态框
const [ViewDataModal, viewDataModalApi] = useVbenModal({
  showFooter: false,
  title: $t('common.view'),
});

// 存储当前正在编辑的数据
const currentEditingData = ref<any>({});

// 动态表单字段

// 创建新增数据的表单
const [AddDataForm, addDataFormApi] = useVbenForm({
  commonConfig: {
    componentProps: {
      class: 'w-full',
    },
  },
  handleSubmit: async (values) => {
    const { valid } = await addDataFormApi.validate();
    if (!valid) return;

    // 提取固定字段
    const { code, name, ...dynamicAttributes } = values;

    // 处理日期时间字段格式
    const formattedAttributes = { ...dynamicAttributes };
    dynamicFormFields.value.forEach((field) => {
      // 查找 DateTime 类型的字段并格式化
      // 由于我们在 onSelect 方法中设置了 field.attributeType，所以可以直接使用
      if (
        field.attributeType === 'DateTime' &&
        formattedAttributes[field.fieldName]
      ) {
        // 使用 dayjs 格式化为 yyyy-MM-dd hh:mm:ss
        formattedAttributes[field.fieldName] = dayjs(
          formattedAttributes[field.fieldName],
        ).format('YYYY-MM-DD HH:mm:ss');
      }
    });

    // 构造符合要求的请求参数格式
    const requestData = {
      masterDataTypeCode: currentSelectedCode.value,
      name,
      code,
      attributes: formattedAttributes,
    };

    await postMasterDataCreateMasterData({
      body: requestData,
    });
    message.success($t('common.addSuccess'));
    addDataModalApi.close();
    // 刷新表格数据
    await gridApi.reload();
  },
  schema: [
    {
      component: 'Input',
      componentProps: {},
      fieldName: 'code',
      label: $t('abp.masterdata.code'),
      rules: 'required',
    },
    {
      component: 'Input',
      componentProps: {},
      fieldName: 'name',
      label: $t('abp.masterdata.name'),
      rules: 'required',
    },
    // 动态字段将在这里添加
  ],
  showDefaultActions: false,
  wrapperClass: 'grid-cols-2 gap-4', // 设置每行显示2列
});

// 创建编辑数据的表单
const [EditDataForm, editDataFormApi] = useVbenForm({
  commonConfig: {
    componentProps: {
      class: 'w-full',
    },
  },
  handleSubmit: async (values) => {
    const { valid } = await editDataFormApi.validate();
    if (!valid) return;

    // 提取固定字段
    const { id, code, name, ...dynamicAttributes } = values;

    // 处理日期时间字段格式
    const formattedAttributes = { ...dynamicAttributes };
    dynamicFormFields.value.forEach((field) => {
      // 查找 DateTime 类型的字段并格式化
      if (
        field.attributeType === 'DateTime' &&
        formattedAttributes[field.fieldName]
      ) {
        // 使用 dayjs 格式化为 yyyy-MM-dd hh:mm:ss
        formattedAttributes[field.fieldName] = dayjs(
          formattedAttributes[field.fieldName],
        ).format('YYYY-MM-DD HH:mm:ss');
      }
    });

    // 构造符合要求的请求参数格式
    const requestData = {
      id,
      masterDataTypeCode: currentSelectedCode.value,
      name,
      code,
      attributes: formattedAttributes,
    };

    await postMasterDataUpdateMasterData({
      body: requestData,
    });
    message.success($t('common.editSuccess'));
    editDataModalApi.close();
    // 刷新表格数据
    await gridApi.reload();
  },
  schema: [
    {
      component: 'Input',
      componentProps: {
        disabled: true,
      },
      fieldName: 'id',
      label: 'ID',
      dependencies: {
        show: () => false,
        triggerFields: ['id'],
      },
    },
    {
      component: 'Input',
      componentProps: {},
      fieldName: 'code',
      label: $t('abp.masterdata.code'),
      rules: 'required',
    },
    {
      component: 'Input',
      componentProps: {},
      fieldName: 'name',
      label: $t('abp.masterdata.name'),
      rules: 'required',
    },
    // 动态字段将在这里添加
  ],
  showDefaultActions: false,
  wrapperClass: 'grid-cols-2 gap-4', // 设置每行显示2列
});

// 创建查看数据详情的表单
const [ViewDataForm, viewDataFormApi] = useVbenForm({
  commonConfig: {
    componentProps: {
      class: 'w-full',
      disabled: true, // 所有字段禁用
    },
  },
  handleSubmit: async () => {
    // 查看详情表单不需要提交
  },
  schema: [
    {
      component: 'Input',
      componentProps: {
        disabled: true,
      },
      fieldName: 'code',
      label: $t('abp.masterdata.code'),
    },
    {
      component: 'Input',
      componentProps: {
        disabled: true,
      },
      fieldName: 'name',
      label: $t('abp.masterdata.name'),
    },
    // 动态字段将在这里添加
  ],
  showDefaultActions: false,
  wrapperClass: 'grid-cols-2 gap-4', // 设置每行显示2列
});

// 获取树形数据
async function getTreeData() {
  const result = await postMasterDataGetMasterDataTypes();
  const treeData = result.data.map((item) => ({
    key: item.id,
    title: `${item.name}(${item.code})`,
    ...item,
  }));
  gData.value = treeData;
}

// 处理节点选择
const onSelect = async (keys: any[], info: any) => {
  if (keys.length === 0) {
    currentSelectedKey.value = '';
    return;
  }

  // 通过gdata中获取节点数据
  const nodeData = gData.value.find((item) => item.key === info.node.key);
  currentSelectedKey.value = nodeData.id;
  currentSelectedName.value = nodeData.name;
  currentSelectedCode.value = nodeData.code;

  // 清空动态列，只保留基础列
  dynamicTableColumn.splice(4); // 保留前4个基础列（包括enabled列）

  const result = await postMasterDataGetMasterDataAttributes({
    body: { masterDataTypeId: currentSelectedKey.value },
  });

  // 更新动态表格列
  result.data.forEach((attr) => {
    // 动态添加列
    dynamicTableColumn.push({
      field: attr.code,
      title: attr.name,
    });
  });

  const dynamicTableColumnCopy = [...dynamicTableColumn];
  dynamicTableColumnCopy.push({
    title: $t('common.action'),
    field: 'action',
    fixed: 'right',
    width: '200',
    slots: { default: 'action' },
  });
  // 获取 gridApi 实例后，可以调用 setGridOptions 方法更新列配置
  gridApi.setGridOptions({
    columns: dynamicTableColumnCopy,
  });

  // 更新动态表单字段
  const dynamicFields = result.data.map((attr) => {
    let component = 'Input'; // 默认组件
    let componentProps = { placeholder: '' }; // 默认组件属性

    switch (attr.attributeType) {
      case 'DateTime': {
        component = 'DatePicker';
        componentProps = {
          showTime: true, // 支持选择时分秒
        };
        break;
      }
      case 'Number': {
        component = 'InputNumber';
        break;
      }
      case 'String': {
        component = 'Input';
        break;
      }
      default: {
        component = 'Input';
      }
    }

    return {
      component,
      componentProps,
      fieldName: attr.code,
      label: attr.name,
      attributeType: attr.attributeType, // 添加 attributeType 以便后续使用
      // 根据需要添加规则
    };
  });

  dynamicFormFields.value = dynamicFields;

  // 更新表单schema
  const currentState = addDataFormApi.getState();
  const newSchema = [
    ...(currentState?.schema?.slice(0, 2) || []), // 保留code和name两个固定字段
    ...dynamicFields, // 添加动态字段
  ];

  addDataFormApi.setState({
    schema: newSchema,
  });

  // 更新编辑表单schema
  const editCurrentState = editDataFormApi.getState();
  const editNewSchema = [
    ...(editCurrentState?.schema?.slice(0, 3) || []), // 保留id, code和name三个固定字段
    ...dynamicFields, // 添加动态字段
  ];

  editDataFormApi.setState({
    schema: editNewSchema,
  });

  // 更新查看表单schema
  const viewCurrentState = viewDataFormApi.getState();
  const viewNewSchema = [
    ...(viewCurrentState?.schema?.slice(0, 3) || []), // 保留id, code和name三个固定字段
    ...dynamicFields.map((field) => ({
      ...field,
      componentProps: {
        ...field.componentProps,
        disabled: true, // 查看模式下所有字段都禁用
      },
    })), // 添加动态字段并设置为禁用
  ];

  viewDataFormApi.setState({
    schema: viewNewSchema,
  });

  showTable.value = true;
  // 刷新表格数据
  await gridApi.reload();
};

// 处理节点展开/折叠
const onExpand = (keys: (number | string)[]) => {
  expandedKeys.value = keys;
  autoExpandParent.value = false;
};

// 处理添加数据按钮点击
const handleAddData = () => {
  if (!currentSelectedKey.value) {
    message.warning($t('abp.masterdata.selectMasterDataPrompt'));
    return;
  }

  // 清空表单值，确保日期时间字段正确初始化
  addDataFormApi.setValues({});

  addDataModalApi.setState({
    title: `${$t('common.add')}: ${currentSelectedName.value}(${currentSelectedCode.value})`,
  });
  addDataModalApi.open();
};

// 处理编辑数据按钮点击
const handleEditData = (row: any) => {
  if (!currentSelectedKey.value) {
    message.warning($t('abp.masterdata.selectMasterDataPrompt'));
    return;
  }

  // 保存当前编辑的数据
  currentEditingData.value = { ...row };

  // 处理日期时间字段格式化，确保在编辑表单中正确显示
  const formattedRow = { ...row };
  dynamicFormFields.value.forEach((field) => {
    if (field.attributeType === 'DateTime' && formattedRow[field.fieldName]) {
      // 将字符串格式的时间转换为 dayjs 对象，以便在 DatePicker 中正确显示
      formattedRow[field.fieldName] = dayjs(formattedRow[field.fieldName]);
    }
  });

  // 设置表单值
  editDataFormApi.setValues(formattedRow);

  editDataModalApi.setState({
    title: `${$t('common.edit')}: ${currentSelectedName.value}(${currentSelectedCode.value})`,
  });
  editDataModalApi.open();
};

// 处理查看数据详情按钮点击
const handleViewData = (row: any) => {
  if (!currentSelectedKey.value) {
    message.warning($t('abp.masterdata.selectMasterDataPrompt'));
    return;
  }

  // 处理日期时间字段格式化，确保在查看表单中正确显示
  const formattedRow = { ...row };
  dynamicFormFields.value.forEach((field) => {
    if (field.attributeType === 'DateTime' && formattedRow[field.fieldName]) {
      // 将字符串格式的时间转换为 dayjs 对象，以便在 DatePicker 中正确显示
      formattedRow[field.fieldName] = dayjs(formattedRow[field.fieldName]);
    }
  });

  // 设置表单值
  viewDataFormApi.setValues(formattedRow);

  viewDataModalApi.setState({
    title: `${$t('common.view')}: ${currentSelectedName.value}(${currentSelectedCode.value})`,
  });
  viewDataModalApi.open();
};

// 处理启用状态变更
const onActiveChange = async (checked: boolean, row: any) => {
  const { hasAccessByCodes } = useAccess();
  if (hasAccessByCodes(['AbpMasterDataManagement.Data.Update1'])) {
    await postMasterDataSetMasterDataStatus({
      body: {
        id: row.id,
        enabled: checked,
      },
    });
    message.success($t('common.editSuccess'));
    // 更新当前行的状态
    row.enabled = checked;
  } else {
    message.warning($t('common.mesage403'));
    row.enabled = !checked;
  }
};

onMounted(async () => {
  await getTreeData();
});
</script>

<template>
  <Page auto-content-height class="h-full">
    <div class="grid h-full grid-cols-12 gap-4">
      <!-- 左侧区域 - 树形菜单 -->
      <div class="bg-card col-span-3 flex h-full flex-col xl:col-span-2">
        <div class="flex-1 overflow-auto p-3 pt-4">
          <Tree
            v-model:expanded-keys="expandedKeys"
            v-model:auto-expand-parent="autoExpandParent"
            :tree-data="gData"
            block-node
            @select="onSelect"
            @expand="onExpand"
          >
            <template #title="{ title }">
              <span>{{ title }}</span>
            </template>
          </Tree>
        </div>
      </div>

      <!-- 右侧区域 -->
      <div class="col-span-9 flex h-full flex-col xl:col-span-10">
        <div class="bg-card flex h-full flex-1 flex-col">
          <div class="h-full overflow-hidden">
            <div class="flex h-full flex-col">
              <!-- 表格区域 -->
              <div class="flex-1 overflow-auto p-3">
                <Grid v-if="showTable">
                  <template #toolbar-actions>
                    <TableAction
                      :actions="[
                        {
                          label: $t('common.add'),
                          type: 'primary',
                          icon: 'ant-design:plus-outlined',
                          onClick: handleAddData,
                          auth: ['AbpMasterDataManagement.Data.Create'],
                        },
                      ]"
                    />
                  </template>
                  <template #action="{ row }">
                    <TableAction
                      :actions="[
                        {
                          label: $t('common.edit'),
                          onClick: () => handleEditData(row),
                          auth: ['AbpMasterDataManagement.Data.Update'],
                          type: 'link',
                        },
                        {
                          label: $t('common.view'),
                          onClick: () => handleViewData(row),
                          type: 'link',
                        },
                      ]"
                    />
                  </template>
                  <template #enabled="{ row }">
                    <Switch
                      v-model:checked="row.enabled"
                      checked-children="是"
                      un-checked-children="否"
                      @change="onActiveChange($event, row)"
                    />
                  </template>
                </Grid>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 新增数据的模态框 -->
    <AddDataModal class="w-[800px]">
      <AddDataForm />
    </AddDataModal>

    <!-- 编辑数据的模态框 -->
    <EditDataModal class="w-[800px]">
      <EditDataForm />
    </EditDataModal>

    <!-- 查看数据详情的模态框 -->
    <ViewDataModal class="w-[800px]">
      <ViewDataForm />
    </ViewDataModal>
  </Page>
</template>
