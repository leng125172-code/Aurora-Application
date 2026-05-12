<script setup lang="ts">
import type { DataNode } from 'ant-design-vue/es/tree';

import { onMounted, ref } from 'vue';

import { useAccess } from '@vben/access';
import { Page } from '@vben/common-ui';
// 添加 useVbenModal 和 useVbenForm 的导入
import { useVbenModal } from '@vben/common-ui';

import {
  Button,
  Card,
  Dropdown,
  Menu,
  message,
  Modal,
  Tree,
} from 'ant-design-vue';

import { useVbenForm } from '#/adapter/form';
import {
  postMasterDataCreateMasterDataAttribute,
  postMasterDataCreateMasterDataType,
  postMasterDataDeleteMasterDataAttribute,
  postMasterDataDeleteMasterDataType,
  postMasterDataGetMasterDataAttributes,
  postMasterDataGetMasterDataTypes,
  postMasterDataUpdateMasterDataAttribute,
  postMasterDataUpdateMasterDataType,
} from '#/api-client';
import { $t } from '#/locales';

const { hasAccessByCodes } = useAccess();

// 定义树形数据
const gData = ref<Array<DataNode>>([]);

// 定义展开的节点
const expandedKeys = ref<(number | string)[]>([]);

// 控制是否自动展开父节点
const autoExpandParent = ref<boolean>(true);

// 当前选中的节点
const currentSelectedKey = ref<string>('');
// 当前选中的节点
const currentSelectedName = ref<string>('');
const currentSelectedCode = ref<string>('');
// 当前右键选中的节点数据
const currentContextMenuNode = ref<DataNode | null>(null);

// 右侧显示的数据
const masterDataAttributes = ref<any[]>([]);

// 创建新增表单的模态框
const [AddModal, addModalApi] = useVbenModal({
  onConfirm: () => {
    addFormApi.submitForm();
  },
  onOpenChange: (isOpen) => {
    if (!isOpen) {
      addFormApi.resetForm();
    }
  },
  title: $t('common.add'),
});

// 创建新增表单
const [AddForm, addFormApi] = useVbenForm({
  commonConfig: {
    componentProps: {
      class: 'w-full',
    },
  },
  handleSubmit: async (values) => {
    const { valid } = await addFormApi.validate();
    if (!valid) return;
    await postMasterDataCreateMasterDataType({ body: values });
    message.success($t('common.addSuccess'));
    addModalApi.close();
    // 重新加载树数据
    await getTreeData();
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
  ],
  showDefaultActions: false,
});

// 创建新增属性表单的模态框
const [AddAttributeModal, addAttributeModalApi] = useVbenModal({
  onConfirm: () => {
    addAttributeFormApi.submitForm();
  },
  onOpenChange: (isOpen) => {
    if (!isOpen) {
      addAttributeFormApi.resetForm();
    }
  },
  title: $t('abp.masterdata.addAttribute'),
});

// 创建新增属性表单
const [AddAttributeForm, addAttributeFormApi] = useVbenForm({
  commonConfig: {
    componentProps: {
      class: 'w-full',
    },
  },
  handleSubmit: async (values) => {
    const { valid } = await addAttributeFormApi.validate();
    if (!valid) return;

    // 添加当前选中的主数据类型ID
    const formData = {
      ...values,
      masterDataTypeId: currentSelectedKey.value,
    };

    await postMasterDataCreateMasterDataAttribute({ body: formData });
    message.success($t('common.addSuccess'));
    addAttributeModalApi.close();

    // 重新加载属性数据
    if (currentSelectedKey.value) {
      const result = await postMasterDataGetMasterDataAttributes({
        body: { masterDataTypeId: currentSelectedKey.value },
      });
      masterDataAttributes.value = result?.data || [];
    }
  },
  schema: [
    {
      component: 'Input',
      componentProps: {
        placeholder: $t('abp.masterdata.codePlaceholder'),
      },
      fieldName: 'code',
      label: $t('abp.masterdata.code'),
      rules: 'required',
    },
    {
      component: 'Input',
      componentProps: {
        placeholder: $t('abp.masterdata.namePlaceholder'),
      },
      fieldName: 'name',
      label: $t('abp.masterdata.name'),
      rules: 'required',
    },
    {
      component: 'Select',
      componentProps: {
        allowClear: true,
        filterOption: true,
        options: [
          { label: $t('abp.masterdata.text'), value: 'String' },
          { label: $t('abp.masterdata.number'), value: 'Number' },
          { label: $t('abp.masterdata.date'), value: 'DateTime' },
        ],
        placeholder: $t('abp.masterdata.attributeTypePlaceholder'),
        showSearch: true,
      },
      fieldName: 'attributeType',
      label: $t('abp.masterdata.attributeType'),
      rules: 'required',
    },
  ],
  showDefaultActions: false,
});

// 创建编辑属性表单的模态框
const [EditAttributeModal, editAttributeModalApi] = useVbenModal({
  onConfirm: () => {
    editAttributeFormApi.submitForm();
  },
  onOpenChange: (isOpen) => {
    if (!isOpen) {
      editAttributeFormApi.resetForm();
    }
  },
  title: $t('common.edit'),
});

// 创建编辑属性表单
const [EditAttributeForm, editAttributeFormApi] = useVbenForm({
  commonConfig: {
    componentProps: {
      class: 'w-full',
    },
  },
  handleSubmit: async (values) => {
    try {
      const { valid } = await editAttributeFormApi.validate();
      if (!valid) return;

      // 更新属性数据
      await postMasterDataUpdateMasterDataAttribute({ body: values });
      message.success($t('common.updateSuccess'));
      editAttributeModalApi.close();

      // 重新加载属性数据
      if (currentSelectedKey.value) {
        const result = await postMasterDataGetMasterDataAttributes({
          body: { masterDataTypeId: currentSelectedKey.value },
        });
        masterDataAttributes.value = result?.data || [];
      }
    } catch {
      message.error($t('common.updateFail'));
    }
  },
  schema: [
    {
      component: 'Input',
      componentProps: {},
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
      fieldName: 'masterDataTypeId',
      label: 'masterDataTypeId',
      dependencies: {
        show: () => false,
        triggerFields: ['masterDataTypeId'],
      },
    },
    {
      component: 'Input',
      componentProps: {
        placeholder: $t('abp.masterdata.codePlaceholder'),
      },
      fieldName: 'code',
      label: $t('abp.masterdata.code'),
      rules: 'required',
    },
    {
      component: 'Input',
      componentProps: {
        placeholder: $t('abp.masterdata.namePlaceholder'),
      },
      fieldName: 'name',
      label: $t('abp.masterdata.name'),
      rules: 'required',
    },
    {
      component: 'Select',
      componentProps: {
        allowClear: true,
        filterOption: true,
        options: [
          { label: $t('abp.masterdata.text'), value: 'String' },
          { label: $t('abp.masterdata.number'), value: 'Number' },
          { label: $t('abp.masterdata.date'), value: 'DateTime' },
        ],
        placeholder: $t('abp.masterdata.attributeTypePlaceholder'),
        showSearch: true,
      },
      fieldName: 'attributeType',
      label: $t('abp.masterdata.attributeType'),
      rules: 'required',
    },
  ],
  showDefaultActions: false,
});

// 创建编辑表单的模态框
const [EditModal, editModalApi] = useVbenModal({
  onConfirm: () => {
    editFormApi.submitForm();
  },
  onOpenChange: (isOpen) => {
    if (!isOpen) {
      editFormApi.resetForm();
    }
  },
  title: $t('common.edit'),
});

// 创建编辑表单
const [EditForm, editFormApi] = useVbenForm({
  commonConfig: {
    componentProps: {
      class: 'w-full',
    },
  },
  handleSubmit: async (values) => {
    const { valid } = await editFormApi.validate();
    if (!valid) return;
    // 更新数据
    await postMasterDataUpdateMasterDataType({
      body: values,
    });
    message.success($t('common.updateSuccess'));
    editModalApi.close();
    // 重新加载树数据
    await getTreeData();
  },
  schema: [
    {
      component: 'Input',
      componentProps: {},
      fieldName: 'id',
      label: 'id',
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
  ],
  showDefaultActions: false,
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
  // 通过gdata中获取节点数据
  const nodeData = gData.value.find((item) => item.key === info.node.key);
  currentSelectedKey.value = nodeData.id;
  currentSelectedName.value = nodeData.name;
  currentSelectedCode.value = nodeData.code;

  // 打印被点击节点的数据
  if (info.selectedNodes && info.selectedNodes.length > 0) {
    const result = await postMasterDataGetMasterDataAttributes({
      body: { masterDataTypeId: nodeData.id },
    });
    masterDataAttributes.value = result?.data || [];
  } else {
    // 如果没有选中节点，则清空表格数据
    masterDataAttributes.value = [];
  }
};

// 处理节点展开/折叠
const onExpand = (keys: (number | string)[]) => {
  expandedKeys.value = keys;
  autoExpandParent.value = false;
};

// 处理右键菜单点击事件
const onContextMenuClick = (
  treeKey: string,
  nodeData: DataNode,
  menuKey: any,
) => {
  currentSelectedKey.value = treeKey;
  currentContextMenuNode.value = nodeData;
  // 关闭设置菜单
  settingDropdownVisible.value[treeKey] = false;
  onContextMenuSelect(menuKey);
};

// 处理右键菜单点击
const onContextMenuSelect = (key: string) => {
  switch (key) {
    case 'add': {
      addAttributeModalApi.setState({
        title: `${$t('abp.masterdata.addAttribute')}: ${currentSelectedName.value}(${currentSelectedCode.value})`,
      });
      addAttributeModalApi.open();
      break;
    }
    case 'delete': {
      if (currentContextMenuNode.value) {
        Modal.confirm({
          content: $t('abp.masterdata.deleteWarning'),
          title: `${$t('common.confirmDelete')} ${currentContextMenuNode.value.title}?`,
          async onOk() {
            // 调用删除主数据类型的接口
            await postMasterDataDeleteMasterDataType({
              body: { id: currentContextMenuNode.value.key },
            });
            message.success($t('common.deleteSuccess'));
            // 重新加载树数据
            await getTreeData();
            // 清空右侧属性数据
            masterDataAttributes.value = [];
          },
        });
      } else {
        Modal.confirm({
          content: $t('abp.masterdata.deleteWarning'),
          title: `${$t('common.confirmDelete')}?`,
          async onOk() {
            message.success($t('common.deleteSuccess'));
          },
        });
      }
      break;
    }
    case 'edit': {
      if (currentContextMenuNode.value) {
        editFormApi.setValues({
          id: currentSelectedKey.value,
          code: currentSelectedCode.value,
          name: currentSelectedName.value,
        });
        editModalApi.open();
      } else {
        message.info($t('common.edit'));
      }
      break;
    }
  }
};

// 处理新增按钮点击
const handleAddClick = () => {
  addModalApi.open();
};

// 处理编辑属性
const handleEditAttribute = (attribute: any) => {
  // 设置编辑模态框的标题
  editAttributeModalApi.setState({
    title: `${$t('common.edit')}: ${currentSelectedName.value}(${currentSelectedCode.value})`,
  });
  // 设置编辑表单的值
  editAttributeFormApi.setValues({
    ...attribute,
    masterDataTypeId: currentSelectedKey.value,
  });
  // 打开编辑属性模态框
  editAttributeModalApi.open();
};

// 处理删除属性
const handleDeleteAttribute = (attribute: any) => {
  Modal.confirm({
    content: $t('abp.masterdata.deleteWarning'),
    title: `${$t('common.confirmDelete')}${attribute.name}?`,
    async onOk() {
      // 调用删除主数据属性的接口
      await postMasterDataDeleteMasterDataAttribute({
        body: { id: attribute.id },
      });
      message.success($t('common.deleteSuccess'));
      // 重新加载当前选中节点的属性数据
      if (currentSelectedKey.value) {
        const result = await postMasterDataGetMasterDataAttributes({
          body: { masterDataTypeId: currentSelectedKey.value },
        });
        masterDataAttributes.value = result?.data || [];
      }
    },
  });
};

// 添加用于控制每个节点设置下拉菜单显示状态的响应式对象
const settingDropdownVisible = ref<Record<string, boolean>>({});

// 添加新的处理函数，用于处理设置图标点击事件
// 处理设置图标点击事件
const onSettingClick = (treeKey: string, nodeData: DataNode) => {
  // 从节点数据中提取名称和代码
  const node = gData.value.find((item) => item.key === treeKey);
  if (node) {
    currentSelectedKey.value = node.id; // 使用节点的id而不是key
    currentSelectedName.value = node.name;
    currentSelectedCode.value = node.code;
  }
  currentContextMenuNode.value = nodeData;
  // 显示下拉菜单
  settingDropdownVisible.value[treeKey] = true;
};

// 添加计算属性用于映射显示的类型名称
const getAttributeTypeDisplay = (attributeType: string) => {
  switch (attributeType) {
    case 'DateTime': {
      return `${$t('abp.masterdata.date')}(DateTime)`;
    }
    case 'Number': {
      return `${$t('abp.masterdata.number')}(Number)`;
    }
    case 'String': {
      return `${$t('abp.masterdata.text')}(String)`;
    }
    default: {
      return attributeType;
    }
  }
};

onMounted(() => {
  getTreeData();
});
</script>

<template>
  <Page :auto-content-height="true" class="h-full">
    <div class="grid h-full grid-cols-12 gap-4">
      <!-- 左侧区域 - 树形菜单 -->
      <div class="bg-card col-span-3 flex h-full flex-col xl:col-span-2">
        <div class="flex-1 overflow-auto p-3 pt-0">
          <div class="-ml-3 -mr-3 mb-2 pl-3 pr-3">
            <Button
              type="primary"
              @click="handleAddClick"
              class="mt-3"
              v-access:code="'AbpMasterDataManagement.DataModel.Create'"
            >
              {{ $t('common.add') }}
            </Button>
          </div>
          <!-- 在新增按钮和树形菜单之间添加分割线 -->
          <div
            class="-ml-3 -mr-3 mb-3 border-b border-gray-200 pl-3 pr-3"
          ></div>
          <Tree
            v-model:expanded-keys="expandedKeys"
            v-model:auto-expand-parent="autoExpandParent"
            :tree-data="gData"
            block-node
            @select="onSelect"
            @expand="onExpand"
          >
            <template #title="{ title, key: treeKey, data: nodeData }">
              <div class="flex w-full items-center justify-between">
                <span>{{ title }}</span>
                <Dropdown
                  :trigger="['click']"
                  placement="bottomRight"
                  v-model:open="settingDropdownVisible[treeKey]"
                  @open-change="
                    (visible) => (settingDropdownVisible[treeKey] = visible)
                  "
                >
                  <Button
                    type="text"
                    size="small"
                    @click.stop="() => onSettingClick(treeKey, nodeData)"
                    class="text-blue-500 hover:text-blue-700"
                  >
                    <span class="icon-[ant-design--setting-outlined]"></span>
                  </Button>
                  <template #overlay>
                    <Menu
                      @click="
                        ({ key: menuKey }) =>
                          onContextMenuClick(treeKey, nodeData, menuKey)
                      "
                    >
                      <Menu.Item
                        key="add"
                        v-if="
                          hasAccessByCodes([
                            'AbpMasterDataManagement.DataModel.Create',
                          ])
                        "
                      >
                        {{ $t('abp.masterdata.addAttribute') }}
                      </Menu.Item>
                      <Menu.Item
                        key="edit"
                        v-if="
                          hasAccessByCodes([
                            'AbpMasterDataManagement.DataModel.Update',
                          ])
                        "
                      >
                        {{ $t('common.edit') }}
                      </Menu.Item>
                      <Menu.Item
                        key="delete"
                        v-if="
                          hasAccessByCodes([
                            'AbpMasterDataManagement.DataModel.Delete',
                          ])
                        "
                      >
                        {{ $t('common.delete') }}
                      </Menu.Item>
                    </Menu>
                  </template>
                </Dropdown>
              </div>
            </template>
          </Tree>
        </div>
      </div>

      <!-- 右侧区域 -->
      <div class="col-span-9 flex h-full flex-col xl:col-span-10">
        <div class="bg-card flex h-full flex-1 flex-col">
          <div class="h-full overflow-hidden">
            <div class="flex h-full flex-col">
              <div class="flex-1 overflow-auto p-3">
                <!-- 右侧内容区域 -->
                <div class="mb-4">
                  <h3 class="-ml-3 -mr-3 mb-3 pl-3 pr-3 text-lg font-semibold">
                    {{ $t('abp.masterdata.attributes') }}
                  </h3>
                  <!-- 在标题和卡片之间添加分割线 -->
                  <div
                    class="-ml-3 -mr-3 mb-3 border-b border-gray-200 pl-3 pr-3"
                  ></div>
                  <div
                    class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-2"
                  >
                    <Card
                      v-for="attribute in masterDataAttributes"
                      :key="attribute.name"
                      class="shadow-sm transition-shadow hover:shadow-md"
                      :body-style="{ padding: '6px' }"
                    >
                      <div class="space-y-0.5 text-xs">
                        <div class="flex">
                          <span class="w-8 text-[10px] text-gray-500"
                            >{{ $t('abp.masterdata.name') }}:</span
                          >
                          <span
                            class="flex-1 truncate"
                            :title="attribute.name"
                            >{{ attribute.name }}</span
                          >
                        </div>
                        <div class="flex">
                          <span class="w-8 text-[10px] text-gray-500"
                            >{{ $t('abp.masterdata.code') }}:</span
                          >
                          <span
                            class="flex-1 truncate"
                            :title="attribute.code"
                            >{{ attribute.code }}</span
                          >
                        </div>
                        <div class="flex">
                          <span class="w-8 text-[10px] text-gray-500"
                            >{{ $t('abp.masterdata.attributeType') }}:</span
                          >
                          <span
                            class="flex-1 truncate"
                            :title="
                              getAttributeTypeDisplay(attribute.attributeType)
                            "
                            >{{
                              getAttributeTypeDisplay(attribute.attributeType)
                            }}</span
                          >
                        </div>
                        <div class="mt-1 flex justify-end space-x-1">
                          <Button
                            type="link"
                            size="small"
                            @click="handleEditAttribute(attribute)"
                            v-access:code="
                              'AbpMasterDataManagement.DataModel.Delete'
                            "
                          >
                            <span
                              class="icon-[ant-design--edit-outlined]"
                            ></span>
                          </Button>
                          <Button
                            type="link"
                            size="small"
                            @click="handleDeleteAttribute(attribute)"
                            v-access:code="
                              'AbpMasterDataManagement.DataModel.Update'
                            "
                          >
                            <span
                              class="icon-[ant-design--delete-outlined]"
                            ></span>
                          </Button>
                        </div>
                      </div>
                    </Card>
                  </div>
                  <div
                    v-if="masterDataAttributes.length === 0"
                    class="py-8 text-center text-gray-500"
                  >
                    {{ $t('common.noData') }}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
    <!-- 新增数据模型的模态框 -->
    <AddModal class="w-[600px]">
      <AddForm />
    </AddModal>

    <!-- 新增属性的模态框 -->
    <AddAttributeModal class="w-[600px]">
      <AddAttributeForm />
    </AddAttributeModal>

    <!-- 编辑数据模型的模态框 -->
    <EditModal class="w-[600px]">
      <EditForm />
    </EditModal>

    <!-- 编辑属性的模态框 -->
    <EditAttributeModal class="w-[600px]">
      <EditAttributeForm />
    </EditAttributeModal>
  </Page>
</template>

<style scoped></style>
