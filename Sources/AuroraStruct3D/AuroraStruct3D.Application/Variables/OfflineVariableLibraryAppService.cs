using System.Text.RegularExpressions;
using AuroraStruct3D.Variables.Dtos;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Variables;

/// <summary>
/// 离线变量库应用服务实现。
/// </summary>
public class OfflineVariableLibraryAppService
    : AuroraStruct3DAppService,
        IOfflineVariableLibraryAppService
{
    private static readonly Regex SequenceRegex = new("\\d+", RegexOptions.Compiled);

    private readonly IRepository<VariableDefinition, Guid> _variableDefinitionRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public OfflineVariableLibraryAppService(
        IRepository<VariableDefinition, Guid> variableDefinitionRepository,
        IUnitOfWorkManager unitOfWorkManager
    )
    {
        _variableDefinitionRepository = variableDefinitionRepository;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <inheritdoc/>
    public async Task<VariableCompileResultDto> CompileAsync(VariableCompileRequestDto input)
    {
        ValidateCompileInput(input);

        List<VariableDiagnosticDto> diagnostics = new();
        long snapshotVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Dictionary<string, VariableDeclarationDto> declarationMap = BuildDeclarationMap(
            input.Declarations,
            diagnostics
        );

        HashSet<string> importAliasSet = BuildImportAliasSet(input.Imports, diagnostics);
        DetectDeclarationImportAliasConflict(declarationMap, importAliasSet, diagnostics);

        Dictionary<(Guid OwnerWorkflowId, string VariableName), VariableDefinition> externalMap =
            await BuildExternalDefinitionMapAsync(input, diagnostics);

        ValidateReferenceSanity(input.Reads, diagnostics, isWrite: false);
        ValidateReferenceSanity(input.Writes, diagnostics, isWrite: true);
        ValidateReads(input, declarationMap, externalMap, diagnostics);
        ValidateWrites(input, declarationMap, externalMap, diagnostics);
        ValidateRequiredInitializationCoverage(input, declarationMap, diagnostics);
        ValidatePotentialUninitializedReads(
            input,
            declarationMap,
            diagnostics,
            input.DefUseAnalysisMode
        );
        diagnostics.Add(
            OfflineVariableDiagnosticPipeline.CreateAnalysisModeDiagnostic(input.DefUseAnalysisMode)
        );
        diagnostics = OfflineVariableDiagnosticPipeline.DeduplicateDiagnostics(diagnostics);
        diagnostics = OfflineVariableDiagnosticPipeline.ApplySuppression(
            diagnostics,
            input.SuppressedDiagnosticCodes
        );

        bool canPublish = diagnostics.TrueForAll(x =>
            x.Severity != VariableDiagnosticSeverity.Error
        );
        if (canPublish)
        {
            await UpsertOwnerDefinitionsAsync(input, declarationMap, snapshotVersion);
        }

        List<VariableDefinitionDto> visibleVariables = await GetVisibleVariablesInternalAsync(
            input.ProjectId,
            input.WorkflowId
        );

        return new VariableCompileResultDto
        {
            SnapshotVersion = snapshotVersion,
            CanPublish = canPublish,
            VisibleVariables = visibleVariables,
            Diagnostics = diagnostics,
        };
    }

    /// <inheritdoc/>
    public async Task<List<VariableDefinitionDto>> GetVisibleVariablesAsync(
        Guid projectId,
        Guid workflowId
    )
    {
        return await GetVisibleVariablesInternalAsync(projectId, workflowId);
    }

    private static void ValidateCompileInput(VariableCompileRequestDto input)
    {
        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: ProjectId 不能为空。"
            );
        }

        if (input.WorkflowId == Guid.Empty)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: WorkflowId 不能为空。"
            );
        }

        // MOD: 强制路径敏感 Def-Use 输入，禁止线性回退模式。
        if (string.IsNullOrWhiteSpace(input.EntryNodeId))
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: EntryNodeId 不能为空。"
            );
        }

        if (input.ControlFlowEdges.Count == 0)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: ControlFlowEdges 不能为空。"
            );
        }
    }

    private static Dictionary<string, VariableDeclarationDto> BuildDeclarationMap(
        List<VariableDeclarationDto> declarations,
        List<VariableDiagnosticDto> diagnostics
    )
    {
        Dictionary<string, VariableDeclarationDto> map = new(StringComparer.Ordinal);

        foreach (VariableDeclarationDto declaration in declarations)
        {
            if (string.IsNullOrWhiteSpace(declaration.Name))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableBadRequest,
                        "变量名不能为空。",
                        null,
                        declaration.Name,
                        null
                    )
                );
                continue;
            }

            if (string.IsNullOrWhiteSpace(declaration.TypeName))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableTypeMismatch,
                        $"变量 '{declaration.Name}' 缺少类型定义。",
                        null,
                        declaration.Name,
                        null
                    )
                );
                continue;
            }

            if (!map.TryAdd(declaration.Name, declaration))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableDuplicateDefinition,
                        $"变量 '{declaration.Name}' 在同一工作流中重复定义。",
                        null,
                        declaration.Name,
                        null
                    )
                );
            }
        }

        return map;
    }

    private static HashSet<string> BuildImportAliasSet(
        List<VariableImportDto> imports,
        List<VariableDiagnosticDto> diagnostics
    )
    {
        HashSet<string> aliases = new(StringComparer.Ordinal);

        foreach (VariableImportDto import in imports)
        {
            string alias = string.IsNullOrWhiteSpace(import.Alias)
                ? import.VariableName
                : import.Alias;
            if (string.IsNullOrWhiteSpace(alias))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableBadRequest,
                        "导入变量缺少变量名。",
                        import.OwnerWorkflowId,
                        import.VariableName,
                        null
                    )
                );
                continue;
            }

            if (!aliases.Add(alias))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableDuplicateDefinition,
                        $"导入别名 '{alias}' 重复。",
                        import.OwnerWorkflowId,
                        import.VariableName,
                        null
                    )
                );
            }
        }

        return aliases;
    }

    private static void DetectDeclarationImportAliasConflict(
        Dictionary<string, VariableDeclarationDto> declarationMap,
        HashSet<string> importAliasSet,
        List<VariableDiagnosticDto> diagnostics
    )
    {
        foreach ((string name, _) in declarationMap)
        {
            if (!importAliasSet.Contains(name))
            {
                continue;
            }

            diagnostics.Add(
                CreateDiagnostic(
                    VariableDiagnosticSeverity.Error,
                    AuroraStruct3DDomainErrorCodes.VariableDuplicateDefinition,
                    $"本地变量 '{name}' 与导入变量别名冲突。",
                    null,
                    name,
                    null
                )
            );
        }
    }

    private async Task<
        Dictionary<(Guid OwnerWorkflowId, string VariableName), VariableDefinition>
    > BuildExternalDefinitionMapAsync(
        VariableCompileRequestDto input,
        List<VariableDiagnosticDto> diagnostics
    )
    {
        Dictionary<(Guid OwnerWorkflowId, string VariableName), VariableDefinition> map = new();

        if (input.Imports.Count == 0)
        {
            return map;
        }

        IQueryable<VariableDefinition> queryable =
            await _variableDefinitionRepository.GetQueryableAsync();
        List<VariableDefinition> candidates = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.ProjectId == input.ProjectId)
        );

        foreach (VariableImportDto import in input.Imports)
        {
            VariableDefinition? definition = candidates.FirstOrDefault(x =>
                x.OwnerWorkflowId == import.OwnerWorkflowId && x.Name == import.VariableName
            );

            if (definition is null)
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableUndefinedReference,
                        $"导入变量 '{import.VariableName}' 不存在。",
                        import.OwnerWorkflowId,
                        import.VariableName,
                        null
                    )
                );
                continue;
            }

            if (definition.Visibility != VariableVisibility.PublicRead)
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableVisibilityDenied,
                        $"变量 '{import.VariableName}' 不是 PublicRead，禁止跨工作流导入。",
                        import.OwnerWorkflowId,
                        import.VariableName,
                        null
                    )
                );
                continue;
            }

            map[(import.OwnerWorkflowId, import.VariableName)] = definition;
        }

        return map;
    }

    private static void ValidateReads(
        VariableCompileRequestDto input,
        Dictionary<string, VariableDeclarationDto> declarationMap,
        Dictionary<(Guid OwnerWorkflowId, string VariableName), VariableDefinition> externalMap,
        List<VariableDiagnosticDto> diagnostics
    )
    {
        foreach (VariableReferenceDto read in input.Reads)
        {
            bool isLocalReference =
                read.OwnerWorkflowId == input.WorkflowId || read.OwnerWorkflowId == Guid.Empty;

            if (isLocalReference)
            {
                if (
                    !declarationMap.TryGetValue(
                        read.VariableName,
                        out VariableDeclarationDto? declaration
                    )
                )
                {
                    diagnostics.Add(
                        CreateDiagnostic(
                            VariableDiagnosticSeverity.Error,
                            AuroraStruct3DDomainErrorCodes.VariableUndefinedReference,
                            $"读取引用变量 '{read.VariableName}' 未声明。",
                            read.OwnerWorkflowId,
                            read.VariableName,
                            read.Location
                        )
                    );
                }
                else
                {
                    ValidateExpectedType(read, declaration.TypeName, diagnostics, isWrite: false);
                }

                continue;
            }

            if (
                !externalMap.TryGetValue(
                    (read.OwnerWorkflowId, read.VariableName),
                    out VariableDefinition? definition
                )
            )
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableUndefinedReference,
                        $"读取引用变量 '{read.VariableName}' 未导入或不存在。",
                        read.OwnerWorkflowId,
                        read.VariableName,
                        read.Location
                    )
                );
            }
            else
            {
                ValidateExpectedType(read, definition.TypeName, diagnostics, isWrite: false);
            }
        }
    }

    private static void ValidateReferenceSanity(
        List<VariableReferenceDto> references,
        List<VariableDiagnosticDto> diagnostics,
        bool isWrite
    )
    {
        foreach (VariableReferenceDto reference in references)
        {
            if (reference.OwnerWorkflowId == Guid.Empty)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(reference.VariableName))
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableBadRequest,
                        $"{(isWrite ? "写入" : "读取")}引用缺少 VariableName。",
                        reference.OwnerWorkflowId,
                        null,
                        reference.Location
                    )
                );
            }

            if (reference.Sequence.HasValue && reference.Sequence.Value < 0)
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableBadRequest,
                        $"{(isWrite ? "写入" : "读取")}引用 Sequence 不能为负数。",
                        reference.OwnerWorkflowId,
                        reference.VariableName,
                        reference.Location
                    )
                );
            }
        }
    }

    private static void ValidateWrites(
        VariableCompileRequestDto input,
        Dictionary<string, VariableDeclarationDto> declarationMap,
        Dictionary<(Guid OwnerWorkflowId, string VariableName), VariableDefinition> externalMap,
        List<VariableDiagnosticDto> diagnostics
    )
    {
        foreach (VariableReferenceDto write in input.Writes)
        {
            bool isLocalWrite =
                write.OwnerWorkflowId == input.WorkflowId || write.OwnerWorkflowId == Guid.Empty;

            if (!isLocalWrite)
            {
                if (
                    externalMap.TryGetValue(
                        (write.OwnerWorkflowId, write.VariableName),
                        out VariableDefinition? definition
                    )
                )
                {
                    diagnostics.Add(
                        CreateDiagnostic(
                            VariableDiagnosticSeverity.Error,
                            AuroraStruct3DDomainErrorCodes.VariableCrossWorkflowWriteForbidden,
                            $"禁止写入外部工作流变量 '{write.VariableName}'。",
                            write.OwnerWorkflowId,
                            write.VariableName,
                            write.Location
                        )
                    );

                    ValidateExpectedType(write, definition.TypeName, diagnostics, isWrite: true);
                }
                else
                {
                    diagnostics.Add(
                        CreateDiagnostic(
                            VariableDiagnosticSeverity.Error,
                            AuroraStruct3DDomainErrorCodes.VariableUndefinedReference,
                            $"写入引用变量 '{write.VariableName}' 未声明。",
                            write.OwnerWorkflowId,
                            write.VariableName,
                            write.Location
                        )
                    );
                }

                continue;
            }

            if (
                !declarationMap.TryGetValue(
                    write.VariableName,
                    out VariableDeclarationDto? declaration
                )
            )
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableUndefinedReference,
                        $"写入引用变量 '{write.VariableName}' 未声明。",
                        write.OwnerWorkflowId,
                        write.VariableName,
                        write.Location
                    )
                );
                continue;
            }

            ValidateExpectedType(write, declaration.TypeName, diagnostics, isWrite: true);

            if (declaration.Mutability == VariableMutability.Readonly)
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        VariableDiagnosticSeverity.Error,
                        AuroraStruct3DDomainErrorCodes.VariableReadonlyWriteForbidden,
                        $"变量 '{write.VariableName}' 为 Readonly，禁止写入。",
                        write.OwnerWorkflowId,
                        write.VariableName,
                        write.Location
                    )
                );
            }
        }
    }

    private static void ValidateExpectedType(
        VariableReferenceDto reference,
        string actualTypeName,
        List<VariableDiagnosticDto> diagnostics,
        bool isWrite
    )
    {
        if (string.IsNullOrWhiteSpace(reference.ExpectedTypeName))
        {
            return;
        }

        if (string.Equals(reference.ExpectedTypeName, actualTypeName, StringComparison.Ordinal))
        {
            return;
        }

        diagnostics.Add(
            CreateDiagnostic(
                VariableDiagnosticSeverity.Error,
                AuroraStruct3DDomainErrorCodes.VariableTypeMismatch,
                $"{(isWrite ? "写入" : "读取")}引用变量 '{reference.VariableName}' 类型不匹配，期望 {reference.ExpectedTypeName}，实际 {actualTypeName}。",
                reference.OwnerWorkflowId,
                reference.VariableName,
                reference.Location
            )
        );
    }

    private static void ValidateRequiredInitializationCoverage(
        VariableCompileRequestDto input,
        Dictionary<string, VariableDeclarationDto> declarationMap,
        List<VariableDiagnosticDto> diagnostics
    )
    {
        HashSet<string> writtenVariables = input
            .Writes.Where(x => x.OwnerWorkflowId == input.WorkflowId)
            .Select(x => x.VariableName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string name, VariableDeclarationDto declaration) in declarationMap)
        {
            if (!declaration.IsRequiredInit)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(declaration.DefaultValueJson))
            {
                continue;
            }

            if (writtenVariables.Contains(name))
            {
                continue;
            }

            diagnostics.Add(
                CreateDiagnostic(
                    VariableDiagnosticSeverity.Error,
                    AuroraStruct3DDomainErrorCodes.VariableUninitializedRead,
                    $"必填变量 '{name}' 未设置默认值，且未检测到初始化写入引用。",
                    input.WorkflowId,
                    name,
                    null
                )
            );
        }
    }

    private static void ValidatePotentialUninitializedReads(
        VariableCompileRequestDto input,
        Dictionary<string, VariableDeclarationDto> declarationMap,
        List<VariableDiagnosticDto> diagnostics,
        VariableDefUseAnalysisMode mode
    )
    {
        VariableCompileRequestDto analyzeInput = input;
        analyzeInput.DefUseAnalysisMode = mode;
        diagnostics.AddRange(
            OfflineVariableDefUseAnalyzer.AnalyzePotentialUninitializedReads(
                analyzeInput,
                declarationMap
            )
        );
    }

    private static long? TryExtractSequence(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        MatchCollection matches = SequenceRegex.Matches(location);
        if (matches.Count == 0)
        {
            return null;
        }

        if (!long.TryParse(matches[0].Value, out long sequence))
        {
            return null;
        }

        return sequence;
    }

    private static long? TryResolveSequence(VariableReferenceDto reference)
    {
        if (reference.Sequence.HasValue)
        {
            return reference.Sequence.Value;
        }

        return TryExtractSequence(reference.Location);
    }

    private async Task UpsertOwnerDefinitionsAsync(
        VariableCompileRequestDto input,
        Dictionary<string, VariableDeclarationDto> declarationMap,
        long snapshotVersion
    )
    {
        using var uow = _unitOfWorkManager.Begin();

        IQueryable<VariableDefinition> queryable =
            await _variableDefinitionRepository.GetQueryableAsync();
        List<VariableDefinition> existing = await AsyncExecuter.ToListAsync(
            queryable.Where(x =>
                x.ProjectId == input.ProjectId && x.OwnerWorkflowId == input.WorkflowId
            )
        );

        HashSet<string> incomingNames = declarationMap.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (VariableDefinition item in existing.Where(x => !incomingNames.Contains(x.Name)))
        {
            await _variableDefinitionRepository.DeleteAsync(item, autoSave: false);
        }

        foreach ((string name, VariableDeclarationDto declaration) in declarationMap)
        {
            VariableDefinition? found = existing.FirstOrDefault(x => x.Name == name);
            if (found is null)
            {
                // 使用 ABP 内置的 HardDeleteAsync 硬删除可能存在的软删除同名记录
                await _variableDefinitionRepository.HardDeleteAsync(
                    x =>
                        x.ProjectId == input.ProjectId
                        && x.OwnerWorkflowId == input.WorkflowId
                        && x.Name == name,
                    autoSave: false
                );

                VariableDefinition created = VariableDefinition.Create(
                    GuidGenerator.Create(),
                    input.ProjectId,
                    input.WorkflowId,
                    declaration.Name,
                    declaration.TypeName,
                    declaration.Visibility,
                    declaration.Mutability,
                    declaration.IsRequiredInit,
                    declaration.DefaultValueJson,
                    snapshotVersion
                );
                await _variableDefinitionRepository.InsertAsync(created, autoSave: false);
                continue;
            }

            found.Update(
                declaration.TypeName,
                declaration.Visibility,
                declaration.Mutability,
                declaration.IsRequiredInit,
                declaration.DefaultValueJson,
                snapshotVersion
            );
            await _variableDefinitionRepository.UpdateAsync(found, autoSave: false);
        }

        await uow.CompleteAsync();
    }

    private async Task<List<VariableDefinitionDto>> GetVisibleVariablesInternalAsync(
        Guid projectId,
        Guid workflowId
    )
    {
        IQueryable<VariableDefinition> queryable =
            await _variableDefinitionRepository.GetQueryableAsync();
        List<VariableDefinition> visible = await AsyncExecuter.ToListAsync(
            queryable.Where(x =>
                x.ProjectId == projectId
                && (
                    x.OwnerWorkflowId == workflowId || x.Visibility == VariableVisibility.PublicRead
                )
            )
        );

        return visible
            .OrderBy(x => x.OwnerWorkflowId)
            .ThenBy(x => x.Name)
            .Select(MapToDto)
            .ToList();
    }

    private static VariableDefinitionDto MapToDto(VariableDefinition entity)
    {
        return new VariableDefinitionDto
        {
            VariableId = entity.Id,
            OwnerWorkflowId = entity.OwnerWorkflowId,
            Name = entity.Name,
            TypeName = entity.TypeName,
            Visibility = entity.Visibility,
            Mutability = entity.Mutability,
            IsRequiredInit = entity.IsRequiredInit,
            DefaultValueJson = entity.DefaultValueJson,
        };
    }

    private static VariableDiagnosticDto CreateDiagnostic(
        VariableDiagnosticSeverity severity,
        string code,
        string message,
        Guid? ownerWorkflowId,
        string? variableName,
        string? location
    )
    {
        return new VariableDiagnosticDto
        {
            Severity = severity,
            Code = code,
            Message = message,
            OwnerWorkflowId = ownerWorkflowId,
            VariableName = variableName,
            Location = location,
        };
    }
}
