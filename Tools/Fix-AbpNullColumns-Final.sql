-- 清理所有定义表中的 NULL 字符串值（每次启动静态 Saver 都可能写入新 NULL 记录）

-- AbpSettingDefinitions 表
UPDATE "AbpSettingDefinitions" SET "Providers" = '' WHERE "Providers" IS NULL;
UPDATE "AbpSettingDefinitions" SET "Description" = '' WHERE "Description" IS NULL;
UPDATE "AbpSettingDefinitions" SET "DefaultValue" = '' WHERE "DefaultValue" IS NULL;
UPDATE "AbpSettingDefinitions" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;
UPDATE "AbpSettingDefinitions" SET "DisplayName" = '' WHERE "DisplayName" IS NULL;

-- AbpFeatures 表
UPDATE "AbpFeatures" SET "AllowedProviders" = '' WHERE "AllowedProviders" IS NULL;
UPDATE "AbpFeatures" SET "ParentName" = '' WHERE "ParentName" IS NULL;
UPDATE "AbpFeatures" SET "Description" = '' WHERE "Description" IS NULL;
UPDATE "AbpFeatures" SET "DefaultValue" = '' WHERE "DefaultValue" IS NULL;
UPDATE "AbpFeatures" SET "ValueType" = '' WHERE "ValueType" IS NULL;
UPDATE "AbpFeatures" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;
UPDATE "AbpFeatures" SET "DisplayName" = '' WHERE "DisplayName" IS NULL;

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

SELECT 'Done. All NULL values fixed.' AS result;
