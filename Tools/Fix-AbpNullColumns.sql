-- 修复 ABP 10.x 新版本要求的 NOT NULL 列在旧数据库中存在 NULL 值的问题
-- 将所有 NULL 字符串值更新为空字符串，ExtraProperties 更新为空 JSON 对象

-- AbpSettingDefinitions 表
UPDATE "AbpSettingDefinitions" SET "Providers" = '' WHERE "Providers" IS NULL;
UPDATE "AbpSettingDefinitions" SET "Description" = '' WHERE "Description" IS NULL;
UPDATE "AbpSettingDefinitions" SET "DefaultValue" = '' WHERE "DefaultValue" IS NULL;
UPDATE "AbpSettingDefinitions" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;

-- AbpFeatures 表
UPDATE "AbpFeatures" SET "AllowedProviders" = '' WHERE "AllowedProviders" IS NULL;
UPDATE "AbpFeatures" SET "ParentName" = '' WHERE "ParentName" IS NULL;
UPDATE "AbpFeatures" SET "Description" = '' WHERE "Description" IS NULL;
UPDATE "AbpFeatures" SET "DefaultValue" = '' WHERE "DefaultValue" IS NULL;
UPDATE "AbpFeatures" SET "ValueType" = '' WHERE "ValueType" IS NULL;
UPDATE "AbpFeatures" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;

-- AbpFeatureGroups 表
UPDATE "AbpFeatureGroups" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;

-- AbpPermissions 表
UPDATE "AbpPermissions" SET "ManagementPermissionName" = '' WHERE "ManagementPermissionName" IS NULL;
UPDATE "AbpPermissions" SET "ResourceName" = '' WHERE "ResourceName" IS NULL;
UPDATE "AbpPermissions" SET "ParentName" = '' WHERE "ParentName" IS NULL;
UPDATE "AbpPermissions" SET "GroupName" = '' WHERE "GroupName" IS NULL;
UPDATE "AbpPermissions" SET "Providers" = '' WHERE "Providers" IS NULL;
UPDATE "AbpPermissions" SET "StateCheckers" = '' WHERE "StateCheckers" IS NULL;
UPDATE "AbpPermissions" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;

-- AbpPermissionGroups 表
UPDATE "AbpPermissionGroups" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;

-- 验证修复结果
SELECT 'AbpSettingDefinitions.Providers NULL 数:' AS info, COUNT(*) FROM "AbpSettingDefinitions" WHERE "Providers" IS NULL
UNION ALL
SELECT 'AbpFeatures.AllowedProviders NULL 数:', COUNT(*) FROM "AbpFeatures" WHERE "AllowedProviders" IS NULL
UNION ALL
SELECT 'AbpPermissions.ManagementPermissionName NULL 数:', COUNT(*) FROM "AbpPermissions" WHERE "ManagementPermissionName" IS NULL;
