using Lion.AbpPro.CodeManagement.Files;
using Microsoft.Extensions.Logging;
using Volo.Abp.Timing;

namespace Lion.AbpPro.CodeManagement.Data.Templates;

public class TemplateDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly ITemplateRepository _templateRepository;
    private readonly IFileLoader _fileLoader;
    private readonly IClock _clock;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<TemplateDataSeedContributor> _logger;

    public TemplateDataSeedContributor(
        ITemplateRepository templateRepository,
        IFileLoader fileLoader,
        IClock clock,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant,
        ILogger<TemplateDataSeedContributor> logger
    )
    {
        _templateRepository = templateRepository;
        _fileLoader = fileLoader;
        _clock = clock;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        //await TemplateAsync();
        var createAntdResult = await CreateTemplateAsync(
            StandardTemplateDataSeedConst.TemplateVbenAntdGroupName
        );
        await CreateTemplateForAspNetCoreAsync(createAntdResult.template);
        await CreateTemplateForVben5AntdAsync(createAntdResult.template);
        if (createAntdResult.isInsert)
        {
            await _templateRepository.InsertAsync(createAntdResult.template);
        }
        else
        {
            await _templateRepository.UpdateAsync(createAntdResult.template);
        }

        var createEleResult = await CreateTemplateAsync(
            StandardTemplateDataSeedConst.TemplateVbenEleGroupName
        );
        await CreateTemplateForAspNetCoreAsync(createEleResult.template);
        await CreateTemplateForVben5EleAsync(createEleResult.template);
        if (createEleResult.isInsert)
        {
            await _templateRepository.InsertAsync(createEleResult.template);
        }
        else
        {
            await _templateRepository.UpdateAsync(createEleResult.template);
        }

        var createNaiveResult = await CreateTemplateAsync(
            StandardTemplateDataSeedConst.TemplateVbenNaiveGroupName
        );
        await CreateTemplateForAspNetCoreAsync(createNaiveResult.template);
        await CreateTemplateForVben5NaiveAsync(createNaiveResult.template);
        if (createNaiveResult.isInsert)
        {
            await _templateRepository.InsertAsync(createNaiveResult.template);
        }
        else
        {
            await _templateRepository.UpdateAsync(createNaiveResult.template);
        }
    }

    /// <summary>
    /// 创建模版
    /// </summary>
    private async Task<(Template template, bool isInsert)> CreateTemplateAsync(string name)
    {
        var template = await _templateRepository.FindByNameAsync(name);
        if (template == null)
        {
            template = new Template(
                _guidGenerator.Create(),
                name,
                "系统初始化模板",
                _currentTenant.Id
            );
            return (template, true);
        }

        return (template, false);
    }

    /// <summary>
    /// 创建aspnetcore模板
    /// </summary>
    public async Task CreateTemplateForAspNetCoreAsync(Template template)
    {
        #region 创建文件夹AspNetCore

        var aspNetCore = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Name
        );
        if (aspNetCore == null)
        {
            aspNetCore = AddFolder(template, StandardTemplateDataSeedConst.AspNetCore.Name);
        }

        #endregion

        #region 创建文件夹src

        var src = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.Name
        );
        if (src == null)
        {
            src = AddFolder(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.Name,
                "src文件夹",
                aspNetCore.Id
            );
        }

        #endregion

        #region 创建文件HttpApi

        var controllerFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.HttpApi.Name
        );
        if (controllerFolder == null)
        {
            controllerFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.HttpApi.Name,
                "文件Controller",
                src.Id
            );
        }

        #endregion

        #region 创建Controller

        var controller = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.HttpApi.ControllerName
        );
        if (controller == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.HttpApi.ControllerName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.HttpApi.ControllerPath
                ),
                controllerFolder.Id
            );
        }

        #endregion

        #region 创建文件夹Application

        var applicationFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.Application.Name
        );
        if (applicationFolder == null)
        {
            applicationFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.Application.Name,
                parentId: src.Id
            );
        }

        #endregion

        #region 创建ApplicationService

        var applicationService = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst.AspNetCore.Src.Application.ApplicationServiceName
        );
        if (applicationService == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.Application.ApplicationServiceName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.Application.ApplicationServicePath
                ),
                applicationFolder.Id
            );
        }

        #endregion

        #region 创建Application AutoMapper

        // var applicationAutoMapper = template.TemplateDetails.FirstOrDefault(e =>
        //     e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.Application.AutoMapperName);
        // if (applicationAutoMapper == null)
        // {
        //     AddFile(template,
        //         StandardTemplateDataSeedConst.AspNetCore.Src.Application.AutoMapperName,
        //         ControlType.Aggregate,
        //         await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.AspNetCore.Src.Application.AutoMapperPath),
        //         applicationFolder.Id);
        // }

        #endregion

        #region 创建文件夹ApplicationContract

        var applicationContractFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts.Name
        );
        if (applicationContractFolder == null)
        {
            applicationContractFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts.Name,
                parentId: src.Id
            );
        }

        #endregion

        #region 创建IApplicationService

        var applicationServiceInterface = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .IApplicationServiceName
        );
        if (applicationServiceInterface == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .IApplicationServiceName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .IApplicationServicePath
                ),
                applicationContractFolder.Id
            );
        }

        var createAggregateCodeInput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .CreateAggregateCodeInputName
        );
        if (createAggregateCodeInput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .CreateAggregateCodeInputName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .CreateAggregateCodeInputPath
                ),
                applicationContractFolder.Id
            );
        }

        var updateAggregateCodeInput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .UpdateAggregateCodeInputName
        );
        if (updateAggregateCodeInput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .UpdateAggregateCodeInputName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .UpdateAggregateCodeInputPath
                ),
                applicationContractFolder.Id
            );
        }

        var deleteAggregateCodeInput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .DeleteAggregateCodeInputName
        );
        if (deleteAggregateCodeInput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .DeleteAggregateCodeInputName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .DeleteAggregateCodeInputPath
                ),
                applicationContractFolder.Id
            );
        }

        var pageAggregateCodeInput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .PageAggregateCodeInputName
        );
        if (pageAggregateCodeInput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .PageAggregateCodeInputName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .PageAggregateCodeInputPath
                ),
                applicationContractFolder.Id
            );
        }

        var pageAggregateCodeOutput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .PageAggregateCodeOutputName
        );
        if (pageAggregateCodeOutput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .PageAggregateCodeOutputName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .PageAggregateCodeOutputPath
                ),
                applicationContractFolder.Id
            );
        }

        var createEntityCodeInput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .CreateEntityCodeInputName
        );
        if (createEntityCodeInput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .CreateEntityCodeInputName,
                ControlType.Entity,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .CreateEntityCodeInputPath
                ),
                applicationContractFolder.Id
            );
        }

        var updateEntityCodeInput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .UpdateEntityCodeInputName
        );
        if (updateEntityCodeInput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .UpdateEntityCodeInputName,
                ControlType.Entity,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .UpdateEntityCodeInputPath
                ),
                applicationContractFolder.Id
            );
        }

        var deleteEntityCodeInput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .DeleteEntityCodeInputName
        );
        if (deleteEntityCodeInput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .DeleteEntityCodeInputName,
                ControlType.Entity,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .DeleteEntityCodeInputPath
                ),
                applicationContractFolder.Id
            );
        }

        var pageEntityCodeInput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .PageEntityCodeInputName
        );
        if (pageEntityCodeInput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .PageEntityCodeInputName,
                ControlType.Entity,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .PageEntityCodeInputPath
                ),
                applicationContractFolder.Id
            );
        }

        var pageEntityCodeOutput = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .PageEntityCodeOutputName
        );
        if (pageEntityCodeOutput == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .PageEntityCodeOutputName,
                ControlType.Entity,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .ApplicationContracts
                        .PageEntityCodeOutputPath
                ),
                applicationContractFolder.Id
            );
        }

        // var exportCodeOutput = template.TemplateDetails.FirstOrDefault(e => e.Name ==
        //                                                                         StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts.ExportOutputName);
        // if (exportCodeOutput == null)
        // {
        //     AddFile(template,
        //         StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts.ExportOutputName,
        //         ControlType.Aggregate,
        //         await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts
        //             .ExportCodeOutputPath),
        //         applicationContractFolder.Id);
        // }
        // var permissionDefinitionProviderOutput = template.TemplateDetails.FirstOrDefault(e => e.Name ==
        //                                                                                       StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts.PermissionDefinitionProviderOutputName);
        // if (permissionDefinitionProviderOutput == null)
        // {
        //     AddFile(template,
        //         StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts.PermissionDefinitionProviderOutputName,
        //         ControlType.Entity,
        //         await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts
        //             .PageEntityCodeOutputPath),
        //         applicationContractFolder.Id);
        // }

        #endregion

        #region 创建文件Domain

        var domainFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.Domain.Name
        );
        if (domainFolder == null)
        {
            domainFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.Name,
                "创建文件Domain",
                src.Id
            );
        }

        #endregion

        #region 创建DomainService

        var aggregateCode = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeName
        );
        if (aggregateCode == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodePath
                ),
                domainFolder.Id
            );
        }

        var entityCode = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.Domain.EntityCodeName
        );
        if (entityCode == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.EntityCodeName,
                ControlType.Entity,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.Domain.EntityCodePath
                ),
                domainFolder.Id
            );
        }

        var aggregateCodeRepository = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeRepositoryName
        );
        if (aggregateCodeRepository == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeRepositoryName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeRepositoryPath
                ),
                domainFolder.Id
            );
        }

        var aggregateCodeManager = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeManagerName
        );
        if (aggregateCodeManager == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeManagerName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeManagerPath
                ),
                domainFolder.Id
            );
        }

        // var autoMapper = template.TemplateDetails.FirstOrDefault(e =>
        //     e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AutoMapperName);
        // if (autoMapper == null)
        // {
        //     AddFile(template,
        //         StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AutoMapperName,
        //         ControlType.Aggregate,
        //         await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AutoMapperPath),
        //         domainFolder.Id);
        // }

        #endregion

        #region 创建文件DomainShared

        var domainSharedFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.Name
        );
        if (domainSharedFolder == null)
        {
            domainSharedFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.Name,
                "创建文件DomainShared",
                src.Id
            );
        }

        #endregion

        #region 创建DomainShared

        var aggregateSharedCode = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.AggregateCodeName
        );
        if (aggregateSharedCode == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.AggregateCodeName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.AggregateCodePath
                ),
                domainSharedFolder.Id
            );
        }

        var entitySharedCode = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EntityCodeName
        );
        if (entitySharedCode == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EntityCodeName,
                ControlType.Entity,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EntityCodePath
                ),
                domainSharedFolder.Id
            );
        }

        var enumCode = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EnumName
        );
        if (enumCode == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EnumName,
                ControlType.Enum,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EnumPath
                ),
                domainSharedFolder.Id
            );
        }

        #endregion

        #region 创建文件EntityFrameworkCore

        var entityFrameworkCoreFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.Name
        );
        if (entityFrameworkCoreFolder == null)
        {
            entityFrameworkCoreFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.Name,
                "创建文件EntityFrameworkCore",
                src.Id
            );
        }

        #endregion

        #region 创建EntityFrameworkCore

        var iDbContextName = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.IDbContextName
        );
        if (iDbContextName == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.IDbContextName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.IDbContextPath
                ),
                entityFrameworkCoreFolder.Id
            );
        }

        var dbContextName = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.DbContextName
        );
        if (dbContextName == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.DbContextName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.DbContextPath
                ),
                entityFrameworkCoreFolder.Id
            );
        }

        var repository = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.RepositoryName
        );
        if (repository == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.RepositoryName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.RepositoryPath
                ),
                entityFrameworkCoreFolder.Id
            );
        }

        var dbContextModelCreating = template.TemplateDetails.FirstOrDefault(e =>
            e.Name
            == StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .EntityFrameworkCore
                .DbContextModelCreatingName
        );
        if (dbContextModelCreating == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .EntityFrameworkCore
                    .DbContextModelCreatingName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst
                        .AspNetCore
                        .Src
                        .EntityFrameworkCore
                        .DbContextModelCreatingPath
                ),
                entityFrameworkCoreFolder.Id
            );
        }

        #endregion
    }

    /// <summary>
    /// 创建vben5 antd vue模板
    /// </summary>
    private async Task CreateTemplateForVben5AntdAsync(Template template)
    {
        #region Vben5Antd文件夹

        var vben5AntdFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Antd.Name
        );
        if (vben5AntdFolder == null)
        {
            vben5AntdFolder = AddFolder(template, StandardTemplateDataSeedConst.Vben5Antd.Name);
        }

        #endregion

        #region Vben5 Views文件夹

        var viewsFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Antd.Views.Name
        );
        if (viewsFolder == null)
        {
            viewsFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.Vben5Antd.Views.Name,
                "Vben5 Views文件夹",
                vben5AntdFolder.Id
            );
        }

        #endregion

        #region Vben5 Views

        var indexTs = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Antd.Views.IndexName
        );
        if (indexTs == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Antd.Views.IndexName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Antd.Views.IndexPath
                ),
                viewsFolder.Id
            );
        }

        var indexVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Antd.Views.IndexVueName
        );
        if (indexVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Antd.Views.IndexVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Antd.Views.IndexVuePath
                ),
                viewsFolder.Id
            );
        }

        var createVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Antd.Views.CreateVueName
        );
        if (createVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Antd.Views.CreateVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Antd.Views.CreateVuePath
                ),
                viewsFolder.Id
            );
        }

        var updateVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Antd.Views.UpdateVueName
        );
        if (updateVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Antd.Views.UpdateVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Antd.Views.UpdateVuePath
                ),
                viewsFolder.Id
            );
        }

        #endregion

        #region Vben5 routers文件

        var routesFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Antd.Routes.Name
        );
        if (routesFolder == null)
        {
            routesFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.Vben5Antd.Routes.Name,
                parentId: vben5AntdFolder.Id
            );
        }

        var route = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Antd.Routes.RouteName
        );
        if (route == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Antd.Routes.RouteName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Antd.Routes.RoutePath
                ),
                routesFolder.Id
            );
        }

        #endregion
    }

    /// <summary>
    /// 创建vben5 ele vue模板
    /// </summary>
    private async Task CreateTemplateForVben5EleAsync(Template template)
    {
        #region Vben5文件夹

        var vben5EleFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Ele.Name
        );
        if (vben5EleFolder == null)
        {
            vben5EleFolder = AddFolder(template, StandardTemplateDataSeedConst.Vben5Ele.Name);
        }

        #endregion

        #region Vben5 Views文件夹

        var viewsFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Ele.Views.Name
        );
        if (viewsFolder == null)
        {
            viewsFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.Vben5Ele.Views.Name,
                "Vben5 Views文件夹",
                vben5EleFolder.Id
            );
        }

        #endregion

        #region Vben5 Views

        var indexTs = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Ele.Views.IndexName
        );
        if (indexTs == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Ele.Views.IndexName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.Vben5Ele.Views.IndexPath),
                viewsFolder.Id
            );
        }

        var indexVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Ele.Views.IndexVueName
        );
        if (indexVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Ele.Views.IndexVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Ele.Views.IndexVuePath
                ),
                viewsFolder.Id
            );
        }

        var createVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Ele.Views.CreateVueName
        );
        if (createVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Ele.Views.CreateVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Ele.Views.CreateVuePath
                ),
                viewsFolder.Id
            );
        }

        var updateVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Ele.Views.UpdateVueName
        );
        if (updateVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Ele.Views.UpdateVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Ele.Views.UpdateVuePath
                ),
                viewsFolder.Id
            );
        }

        #endregion

        #region Vben5 routers文件

        var routesFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Ele.Routes.Name
        );
        if (routesFolder == null)
        {
            routesFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.Vben5Ele.Routes.Name,
                parentId: vben5EleFolder.Id
            );
        }

        var route = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Ele.Routes.RouteName
        );
        if (route == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Ele.Routes.RouteName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Ele.Routes.RoutePath
                ),
                routesFolder.Id
            );
        }

        #endregion
    }

    /// <summary>
    /// 创建vben5 naive vue模板
    /// </summary>
    private async Task CreateTemplateForVben5NaiveAsync(Template template)
    {
        #region Vben5文件夹

        var vben5NaiveFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Naive.Name
        );
        if (vben5NaiveFolder == null)
        {
            vben5NaiveFolder = AddFolder(template, StandardTemplateDataSeedConst.Vben5Naive.Name);
        }

        #endregion

        #region Vben5 Views文件夹

        var viewsFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Naive.Views.Name
        );
        if (viewsFolder == null)
        {
            viewsFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.Vben5Naive.Views.Name,
                "Vben5 Views文件夹",
                vben5NaiveFolder.Id
            );
        }

        #endregion

        #region Vben5 Views

        var indexTs = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Naive.Views.IndexName
        );
        if (indexTs == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Naive.Views.IndexName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Naive.Views.IndexPath
                ),
                viewsFolder.Id
            );
        }

        var indexVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Naive.Views.IndexVueName
        );
        if (indexVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Naive.Views.IndexVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Naive.Views.IndexVuePath
                ),
                viewsFolder.Id
            );
        }

        var createVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Naive.Views.CreateVueName
        );
        if (createVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Naive.Views.CreateVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Naive.Views.CreateVuePath
                ),
                viewsFolder.Id
            );
        }

        var updateVue = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Naive.Views.UpdateVueName
        );
        if (updateVue == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Naive.Views.UpdateVueName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Naive.Views.UpdateVuePath
                ),
                viewsFolder.Id
            );
        }

        #endregion

        #region Vben5 routers文件

        var routesFolder = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Naive.Routes.Name
        );
        if (routesFolder == null)
        {
            routesFolder = AddFolder(
                template,
                StandardTemplateDataSeedConst.Vben5Naive.Routes.Name,
                parentId: vben5NaiveFolder.Id
            );
        }

        var route = template.TemplateDetails.FirstOrDefault(e =>
            e.Name == StandardTemplateDataSeedConst.Vben5Naive.Routes.RouteName
        );
        if (route == null)
        {
            AddFile(
                template,
                StandardTemplateDataSeedConst.Vben5Naive.Routes.RouteName,
                ControlType.Aggregate,
                await _fileLoader.LoadAsync(
                    StandardTemplateDataSeedConst.Vben5Naive.Routes.RoutePath
                ),
                routesFolder.Id
            );
        }

        #endregion
    }

    private async Task TemplateAsync()
    {
        var templateGroup = await _templateRepository.FindByNameAsync(
            StandardTemplateDataSeedConst.TemplateGroupName
        );

        if (templateGroup != null)
            return;

        #region 模板组

        if (templateGroup == null)
        {
            templateGroup = new Template(
                _guidGenerator.Create(),
                StandardTemplateDataSeedConst.TemplateGroupName,
                "系统初始化模板",
                _currentTenant.Id
            );
        }

        #endregion

        #region AspNetCore

        TemplateDetail aspNetCore = null;
        if (
            templateGroup.TemplateDetails.FirstOrDefault(e =>
                e.Name == StandardTemplateDataSeedConst.AspNetCore.Name
            ) == null
        )
        {
            aspNetCore = AddFolder(templateGroup, StandardTemplateDataSeedConst.AspNetCore.Name);
        }

        #endregion

        #region src文件夹

        var src = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Name,
            parentId: aspNetCore?.Id
        );

        #endregion

        #region Controller

        var controller = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.HttpApi.Name,
            parentId: src.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.HttpApi.ControllerName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.HttpApi.ControllerPath
            ),
            controller.Id
        );

        #endregion

        #region Application

        TemplateDetail application;
        application = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Application.Name,
            parentId: src.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Application.ApplicationServiceName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.Application.ApplicationServicePath
            ),
            application.Id
        );
        // AddFile(templateGroup,
        //     StandardTemplateDataSeedConst.AspNetCore.Src.Application.AutoMapperName,
        //     ControlType.Global,
        //     await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.AspNetCore.Src.Application.AutoMapperPath),
        //     application.Id);

        #endregion

        #region ApplicationContracts

        TemplateDetail applicationContracts;
        applicationContracts = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.ApplicationContracts.Name,
            parentId: src.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .IApplicationServiceName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .IApplicationServicePath
            ),
            applicationContracts.Id
        );

        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .CreateAggregateCodeInputName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .CreateAggregateCodeInputPath
            ),
            applicationContracts.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .UpdateAggregateCodeInputName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .UpdateAggregateCodeInputPath
            ),
            applicationContracts.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .DeleteAggregateCodeInputName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .DeleteAggregateCodeInputPath
            ),
            applicationContracts.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .PageAggregateCodeInputName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .PageAggregateCodeInputPath
            ),
            applicationContracts.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .PageAggregateCodeOutputName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .PageAggregateCodeOutputPath
            ),
            applicationContracts.Id
        );

        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .CreateEntityCodeInputName,
            ControlType.Entity,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .CreateEntityCodeInputPath
            ),
            applicationContracts.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .UpdateEntityCodeInputName,
            ControlType.Entity,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .UpdateEntityCodeInputPath
            ),
            applicationContracts.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .DeleteEntityCodeInputName,
            ControlType.Entity,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .DeleteEntityCodeInputPath
            ),
            applicationContracts.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .PageEntityCodeInputName,
            ControlType.Entity,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .PageEntityCodeInputPath
            ),
            applicationContracts.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .ApplicationContracts
                .PageEntityCodeOutputName,
            ControlType.Entity,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .ApplicationContracts
                    .PageEntityCodeOutputPath
            ),
            applicationContracts.Id
        );

        #endregion

        #region Domain

        TemplateDetail domain;
        domain = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Domain.Name,
            parentId: src.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodePath
            ),
            domain.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Domain.EntityCodeName,
            ControlType.Entity,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.EntityCodePath
            ),
            domain.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeRepositoryName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeRepositoryPath
            ),
            domain.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeManagerName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AggregateCodeManagerPath
            ),
            domain.Id
        );
        // AddFile(templateGroup,
        //     StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AutoMapperName,
        //     ControlType.Global,
        //     await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.AspNetCore.Src.Domain.AutoMapperPath),
        //     domain.Id);

        # endregion

        #region DomainShared

        TemplateDetail domainShared;
        domainShared = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.Name,
            parentId: src.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.AggregateCodeName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.AggregateCodePath
            ),
            domainShared.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EntityCodeName,
            ControlType.Entity,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EntityCodePath
            ),
            domainShared.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EnumName,
            ControlType.Enum,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.DomainShared.EnumPath
            ),
            domainShared.Id
        );

        # endregion

        #region EntityFrameworkCore

        TemplateDetail entityFrameworkCore;
        entityFrameworkCore = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.Name,
            parentId: src.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.IDbContextName,
            ControlType.Global,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.IDbContextPath
            ),
            entityFrameworkCore.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.DbContextName,
            ControlType.Global,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.DbContextPath
            ),
            entityFrameworkCore.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.RepositoryName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst.AspNetCore.Src.EntityFrameworkCore.RepositoryPath
            ),
            entityFrameworkCore.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst
                .AspNetCore
                .Src
                .EntityFrameworkCore
                .DbContextModelCreatingName,
            ControlType.Global,
            await _fileLoader.LoadAsync(
                StandardTemplateDataSeedConst
                    .AspNetCore
                    .Src
                    .EntityFrameworkCore
                    .DbContextModelCreatingPath
            ),
            entityFrameworkCore.Id
        );

        # endregion

        #region Vue3

        TemplateDetail vue3 = null;
        if (
            templateGroup.TemplateDetails.FirstOrDefault(e =>
                e.Name == StandardTemplateDataSeedConst.Vue3.Name
            ) == null
        )
        {
            vue3 = AddFolder(templateGroup, StandardTemplateDataSeedConst.Vue3.Name);
        }

        #endregion

        #region src

        TemplateDetail vueSrc;
        vueSrc = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.AspNetCore.Src.Name,
            parentId: vue3?.Id
        );

        #endregion

        #region routes

        TemplateDetail route;
        route = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.Vue3.Src.Routes.Name,
            parentId: vueSrc.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.Vue3.Src.Routes.RouteName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.Vue3.Src.Routes.RoutePath),
            route.Id
        );

        #endregion

        #region views

        TemplateDetail view;
        view = AddFolder(
            templateGroup,
            StandardTemplateDataSeedConst.Vue3.Src.Views.Name,
            parentId: vueSrc.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.Vue3.Src.Views.IndexName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.Vue3.Src.Views.IndexPath),
            view.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.Vue3.Src.Views.IndexVueName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.Vue3.Src.Views.IndexVuePath),
            view.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.Vue3.Src.Views.CreateVueName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.Vue3.Src.Views.CreateVuePath),
            view.Id
        );
        AddFile(
            templateGroup,
            StandardTemplateDataSeedConst.Vue3.Src.Views.UpdateVueName,
            ControlType.Aggregate,
            await _fileLoader.LoadAsync(StandardTemplateDataSeedConst.Vue3.Src.Views.UpdateVuePath),
            view.Id
        );

        #endregion

        await _templateRepository.InsertAsync(templateGroup);
    }

    private TemplateDetail AddFolder(
        Template template,
        string name,
        string description = "Default",
        Guid? parentId = null
    )
    {
        var detail = template.AddTemplateDetail(
            _guidGenerator.Create(),
            TemplateType.Folder,
            null,
            name,
            description,
            string.Empty,
            parentId
        );
        return detail;
    }

    private TemplateDetail AddFile(
        Template template,
        string name,
        ControlType controlType,
        string content,
        Guid parentId,
        string description = "Default"
    )
    {
        var detail = template.AddTemplateDetail(
            _guidGenerator.Create(),
            TemplateType.File,
            controlType,
            name,
            description,
            content,
            parentId
        );
        return detail;
    }
}
