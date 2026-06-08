using AuroraStruct3D.AI;
using AuroraStruct3D.AI.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// AI 模型转换阶段推送 SignalR Hub。
/// 客户端连接后会立即收到当前活动转换的快照。
/// </summary>
[Authorize]
[DisableAutoHubMap]
public class AiModelConversionHub : AbpHub<IAiModelConversionHub>
{
    private readonly IAiModelFileRepository _aiModelFileRepository;

    public AiModelConversionHub(IAiModelFileRepository aiModelFileRepository)
    {
        _aiModelFileRepository = aiModelFileRepository;
    }

    /// <inheritdoc/>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        IQueryable<AiModelFile> query = await _aiModelFileRepository.GetQueryableAsync();
        List<AiModelFile> activeFiles = await query
            .AsNoTracking()
            .Where(file =>
                file.IsOriginalFile
                && !file.IsConvertedFile
                && (
                    file.ConversionStatus == AiModelFileConversionStatus.Pending
                    || file.ConversionStatus == AiModelFileConversionStatus.Converting
                )
            )
            .OrderBy(file => file.AiModelId)
            .ThenBy(file => file.SortOrder)
            .ToListAsync();

        List<AiModelConversionStateDto> states = activeFiles
            .GroupBy(file => file.AiModelId)
            .Select(CreateStateDto)
            .OrderBy(state => state.LastUpdatedTime)
            .ToList();

        await Clients.Caller.ReceiveActiveConversionsSnapshotAsync(states);
    }

    private static AiModelConversionStateDto CreateStateDto(IEnumerable<AiModelFile> files)
    {
        List<AiModelFile> orderedFiles = files.OrderBy(file => file.SortOrder).ToList();
        AiModelFile latestFile = orderedFiles
            .OrderByDescending(file => file.LastModificationTime ?? file.CreationTime)
            .First();

        return new AiModelConversionStateDto
        {
            ModelId = orderedFiles[0].AiModelId,
            SourceFileIds = orderedFiles.Select(file => file.Id).ToList(),
            TargetType = orderedFiles
                .Select(file => file.ConversionTargetType)
                .FirstOrDefault(type => type.HasValue),
            Status = orderedFiles.Any(file =>
                file.ConversionStatus == AiModelFileConversionStatus.Converting
            )
                ? AiModelFileConversionStatus.Converting
                : AiModelFileConversionStatus.Pending,
            ConversionErrorMessage = orderedFiles
                .Select(file => file.ConversionErrorMessage)
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message)),
            LastUpdatedTime = latestFile.LastModificationTime ?? latestFile.CreationTime,
        };
    }
}
