namespace Lion.AbpPro.CodeManagement.Data.Templates;

public class StandardTemplateDataSeedConst
{
    public const string TemplateGroupName = "Lion.AbpPro标准模板(Vben2.8)";
    public const string TemplateVbenAntdGroupName = "Lion.AbpPro标准模板Vben5(Ant Design Vue)";
    public const string TemplateVbenEleGroupName = "Lion.AbpPro标准模板Vben5(Element Plus)";
    public const string TemplateVbenNaiveGroupName = "Lion.AbpPro标准模板Vben5(Naive UI)";

    /// <summary>
    /// AspNetCore
    /// </summary>
    public static class AspNetCore
    {
        public static string Name = "AspNetCore";

        public static class Src
        {
            public static string Name = "src";

            public static class HttpApi
            {
                public static string Name = "HttpApi";

                // 聚合根Controller模板
                public static string ControllerName = "{{aggregateCode}}Controller.txt";
                public static string ControllerPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/HttpApi/{{aggregateCode}}Controller.txt";
            }

            public static class Application
            {
                public static string Name = "Application";

                // 聚合根ApplicationService模板
                public static string ApplicationServiceName = "{{aggregateCode}}AppService.txt";
                public static string ApplicationServicePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Application/{{aggregateCode}}AppService.txt";
                // ApplicationService AutoMapper模板
                // public static string AutoMapperName = "{{projectName}}ApplicationAutoMapperProfile.txt";
                // public static string AutoMapperPath = "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Application/{{projectName}}ApplicationAutoMapperProfile.txt";
            }

            public static class ApplicationContracts
            {
                public static string Name = "Application.Contracts";

                // 聚合根IApplicationService模板
                public static string IApplicationServiceName = "I{{aggregateCode}}AppService.txt";
                public static string IApplicationServicePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/I{{aggregateCode}}AppService.txt";

                public static string CreateAggregateCodeInputName =
                    "Create{{aggregateCode}}Input.txt";
                public static string CreateAggregateCodeInputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Create{{aggregateCode}}Input.txt";

                public static string UpdateAggregateCodeInputName =
                    "Update{{aggregateCode}}Input.txt";
                public static string UpdateAggregateCodeInputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Update{{aggregateCode}}Input.txt";

                public static string DeleteAggregateCodeInputName =
                    "Delete{{aggregateCode}}Input.txt";
                public static string DeleteAggregateCodeInputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Delete{{aggregateCode}}Input.txt";

                public static string PageAggregateCodeInputName = "Page{{aggregateCode}}Input.txt";
                public static string PageAggregateCodeInputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Page{{aggregateCode}}Input.txt";

                public static string PageAggregateCodeOutputName =
                    "Page{{aggregateCode}}Output.txt";
                public static string PageAggregateCodeOutputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Page{{aggregateCode}}Output.txt";

                public static string CreateEntityCodeInputName = "Create{{entityCode}}Input.txt";
                public static string CreateEntityCodeInputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Create{{entityCode}}Input.txt";

                public static string UpdateEntityCodeInputName = "Update{{entityCode}}Input.txt";
                public static string UpdateEntityCodeInputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Update{{entityCode}}Input.txt";

                public static string DeleteEntityCodeInputName = "Delete{{entityCode}}Input.txt";
                public static string DeleteEntityCodeInputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Delete{{entityCode}}Input.txt";

                public static string PageEntityCodeInputName = "Page{{entityCode}}Input.txt";
                public static string PageEntityCodeInputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Page{{entityCode}}Input.txt";

                public static string PageEntityCodeOutputName = "Page{{entityCode}}Output.txt";
                public static string PageEntityCodeOutputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Page{{entityCode}}Output.txt";

                public static string ExportOutputName = "Export{{aggregateCode}}Output.txt";
                public static string ExportCodeOutputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/Export{{aggregateCode}}Output.txt";

                public static string PermissionDefinitionProviderOutputName =
                    "{{aggremateCode}}PermissionDefinitionProvider.txt";
                public static string PermissionDefinitionProviderCodeOutputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/{{aggremateCode}}PermissionDefinitionProvider.txt";

                public static string PermissionsOutputName = "{{aggremateCode}}Permissions.txt";
                public static string PermissionsCodeOutputPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/ApplicationContracts/{{aggremateCode}}Permissions.txt";
            }

            public static class Domain
            {
                public static string Name = "Domain";
                public static string AggregateCodeName = "{{aggregateCode}}.txt";
                public static string AggregateCodePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Domain/{{aggregateCode}}.txt";

                public static string EntityCodeName = "{{entityCode}}.txt";
                public static string EntityCodePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Domain/{{entityCode}}.txt";

                public static string AggregateCodeRepositoryName =
                    "I{{aggregateCode}}Repository.txt";
                public static string AggregateCodeRepositoryPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Domain/I{{aggregateCode}}Repository.txt";

                public static string AggregateCodeManagerName = "{{aggregateCode}}Manager.txt";
                public static string AggregateCodeManagerPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Domain/{{aggregateCode}}Manager.txt";

                // public static string AutoMapperName = "{{projectName}}DomainAutoMapperProfile.txt";
                // public static string AutoMapperPath = "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Domain/{{projectName}}DomainAutoMapperProfile.txt";
            }

            public static class DomainShared
            {
                public static string Name = "Domain.Shared";
                public static string AggregateCodeName = "{{aggregateCode}}Dto.txt";
                public static string AggregateCodePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/DomainShared/{{aggregateCode}}Dto.txt";

                public static string EntityCodeName = "{{entityCode}}Dto.txt";
                public static string EntityCodePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/DomainShared/{{entityCode}}Dto.txt";

                public static string EnumName = "{{enumCode}}.txt";
                public static string EnumPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/DomainShared/{{enumCode}}.txt";
            }

            public static class EntityFrameworkCore
            {
                public static string Name = "EntityFrameworkCore";
                public static string IDbContextName = "I{{projectName}}DbContext.txt";
                public static string IDbContextPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/EntityFrameworkCore/I{{projectName}}DbContext.txt";

                public static string DbContextName = "{{projectName}}DbContext.txt";
                public static string DbContextPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/EntityFrameworkCore/{{projectName}}DbContext.txt";

                public static string RepositoryName = "EfCore{{aggregateCode}}Repository.txt";
                public static string RepositoryPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/EntityFrameworkCore/EfCore{{aggregateCode}}Repository.txt";

                public static string DbContextModelCreatingName =
                    "{{projectName}}DbContextModelCreatingExtensions.txt";
                public static string DbContextModelCreatingPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/EntityFrameworkCore/{{projectName}}DbContextModelCreatingExtensions.txt";
            }
        }
    }

    public static class Vben5Antd
    {
        public static string Name = "Vben5";

        public static class Routes
        {
            public static string Name = "routes";
            public static string RouteName = "{{aggregateCode}}.ts";
            public static string RoutePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5/routes/{{aggregateCode}}.ts";
        }

        public static class Views
        {
            public static string Name = "views";
            public static string IndexName = "schema.ts";
            public static string IndexPath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5/views/schema.ts";
            public static string IndexVueName = "index.vue";
            public static string IndexVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5/views/index.vue";
            public static string CreateVueName = "AddModal.vue";
            public static string CreateVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5/views/AddModal.vue";
            public static string UpdateVueName = "EditModal.vue";
            public static string UpdateVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5/views/EditModal.vue";
        }
    }

    public static class Vben5Ele
    {
        public static string Name = "Vben5";

        public static class Routes
        {
            public static string Name = "routes";
            public static string RouteName = "{{aggregateCode}}.ts";
            public static string RoutePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-ele/routes/{{aggregateCode}}.ts";
        }

        public static class Views
        {
            public static string Name = "views";
            public static string IndexName = "schema.ts";
            public static string IndexPath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-ele/views/schema.ts";
            public static string IndexVueName = "index.vue";
            public static string IndexVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-ele/views/index.vue";
            public static string CreateVueName = "AddModal.vue";
            public static string CreateVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-ele/views/AddModal.vue";
            public static string UpdateVueName = "EditModal.vue";
            public static string UpdateVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-ele/views/EditModal.vue";
        }
    }

    public static class Vben5Naive
    {
        public static string Name = "Vben5";

        public static class Routes
        {
            public static string Name = "routes";
            public static string RouteName = "{{aggregateCode}}.ts";
            public static string RoutePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-naive/routes/{{aggregateCode}}.ts";
        }

        public static class Views
        {
            public static string Name = "views";
            public static string IndexName = "schema.ts";
            public static string IndexPath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-naive/views/schema.ts";
            public static string IndexVueName = "index.vue";
            public static string IndexVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-naive/views/index.vue";
            public static string CreateVueName = "AddModal.vue";
            public static string CreateVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-naive/views/AddModal.vue";
            public static string UpdateVueName = "EditModal.vue";
            public static string UpdateVuePath =
                "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vben5-naive/views/EditModal.vue";
        }
    }

    public static class Vue3
    {
        public static string Name = "Vue3";

        public static class Src
        {
            public static string Name = "src";

            public static class Routes
            {
                public static string Name = "routes";
                public static string RouteName = "{{aggregateCode}}.ts";
                public static string RoutePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vue3/routes/{{aggregateCode}}.ts";
            }

            public static class Views
            {
                public static string Name = "views";
                public static string IndexName = "Index.ts";
                public static string IndexPath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vue3/views/Index.ts";
                public static string IndexVueName = "Index.vue";
                public static string IndexVuePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vue3/views/Index.vue";
                public static string CreateVueName = "Create{{aggregateCode}}.vue";
                public static string CreateVuePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vue3/views/Create{{aggregateCode}}.vue";
                public static string UpdateVueName = "Update{{aggregateCode}}.vue";
                public static string UpdateVuePath =
                    "/Lion.AbpPro.CodeManagement/Data/Templates/Standard/Vue3/views/Update{{aggregateCode}}.vue";
            }
        }
    }
}
