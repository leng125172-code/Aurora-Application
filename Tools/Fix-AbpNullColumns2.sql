-- 清理 04:10 启动写入的 NULL 数据
UPDATE "AbpPermissions" SET "ManagementPermissionName" = '' WHERE "ManagementPermissionName" IS NULL;
UPDATE "AbpPermissions" SET "Providers" = '' WHERE "Providers" IS NULL;
UPDATE "AbpPermissions" SET "StateCheckers" = '' WHERE "StateCheckers" IS NULL;
UPDATE "AbpPermissions" SET "ResourceName" = '' WHERE "ResourceName" IS NULL;
UPDATE "AbpPermissions" SET "GroupName" = '' WHERE "GroupName" IS NULL;
UPDATE "AbpPermissions" SET "ParentName" = '' WHERE "ParentName" IS NULL;
UPDATE "AbpPermissions" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;

UPDATE "AbpFeatures" SET "AllowedProviders" = '' WHERE "AllowedProviders" IS NULL;
UPDATE "AbpFeatures" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;

UPDATE "AbpSettingDefinitions" SET "Providers" = '' WHERE "Providers" IS NULL;
UPDATE "AbpSettingDefinitions" SET "ExtraProperties" = '{}' WHERE "ExtraProperties" IS NULL;

SELECT COUNT(*) as perm_null FROM "AbpPermissions" WHERE "ManagementPermissionName" IS NULL;
SELECT COUNT(*) as feat_null FROM "AbpFeatures" WHERE "AllowedProviders" IS NULL;
SELECT COUNT(*) as sett_null FROM "AbpSettingDefinitions" WHERE "Providers" IS NULL;
