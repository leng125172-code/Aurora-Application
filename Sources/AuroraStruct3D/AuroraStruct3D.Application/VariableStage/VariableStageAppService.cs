using System.Buffers;
using System.Text.Json;
using AuroraStruct3D.VariableStage.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp;
using Volo.Abp.Content;

namespace AuroraStruct3D.VariableStage;

/// <summary>
/// 变量暂存管理应用服务实现。
/// <para>
/// 使用 Redis（通过 <see cref="IDistributedCache"/>）暂存工作流运行时的输出变量值。
/// 存储结构：
/// <list type="bullet">
///   <item><c>var:data:{key}</c> — 二进制值内容</item>
///   <item><c>var:meta:{key}</c> — 元数据（类型、创建时间、大小）</item>
///   <item><c>var:index</c> — 所有变量名集合（Set 类型）</item>
/// </list>
/// </para>
/// </summary>
[Authorize]
public class VariableStageAppService : AuroraStruct3DAppService, IVariableStageAppService
{
    private readonly IDistributedCache _cache;

    private static readonly DistributedCacheEntryOptions DefaultExpiry = new()
    {
        SlidingExpiration = TimeSpan.FromHours(1),
    };

    /// <summary>Redis Key 前缀。</summary>
    private const string KeyPrefixData = "var:data:";

    private const string KeyPrefixMeta = "var:meta:";
    private const string KeyIndex = "var:index";

    public VariableStageAppService(IDistributedCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, string valueType, IRemoteStreamContent file)
    {
        Check.NotNullOrWhiteSpace(key, nameof(key));
        Check.NotNull(file, nameof(file));

        string dataKey = BuildDataKey(key);
        string metaKey = BuildMetaKey(key);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        long totalBytes = 0;

        try
        {
            using MemoryStream ms = new MemoryStream();
            await using Stream source = file.GetStream();

            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read == 0)
                    break;
                await ms.WriteAsync(buffer.AsMemory(0, read));
                totalBytes += read;
            }

            byte[] valueBytes = ms.ToArray();
            long size = totalBytes;

            // 写入数据
            await _cache.SetAsync(dataKey, valueBytes, DefaultExpiry);

            // 写入元数据
            var meta = new VariableMeta
            {
                ValueType = valueType,
                CreatedAt = DateTime.UtcNow,
                SizeBytes = size,
            };
            byte[] metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta);
            await _cache.SetAsync(metaKey, metaBytes, DefaultExpiry);

            // 添加到索引
            await AddToIndexAsync(key);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc/>
    public async Task<VariableValueDto> GetAsync(string key)
    {
        Check.NotNullOrWhiteSpace(key, nameof(key));

        string dataKey = BuildDataKey(key);
        string metaKey = BuildMetaKey(key);

        byte[]? dataBytes = await _cache.GetAsync(dataKey);
        if (dataBytes is null)
        {
            throw new UserFriendlyException($"变量 '{key}' 不存在或已过期。");
        }

        byte[]? metaBytes = await _cache.GetAsync(metaKey);
        VariableMeta meta = metaBytes is not null
            ? JsonSerializer.Deserialize<VariableMeta>(metaBytes) ?? new VariableMeta()
            : new VariableMeta();

        return new VariableValueDto
        {
            Key = key,
            ValueType = meta.ValueType,
            Value = Convert.ToBase64String(dataBytes),
            CreatedAt = meta.CreatedAt,
            SizeBytes = meta.SizeBytes,
        };
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string key)
    {
        Check.NotNullOrWhiteSpace(key, nameof(key));

        string dataKey = BuildDataKey(key);
        string metaKey = BuildMetaKey(key);

        await _cache.RemoveAsync(dataKey);
        await _cache.RemoveAsync(metaKey);
        await RemoveFromIndexAsync(key);
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetKeysAsync()
    {
        byte[]? indexBytes = await _cache.GetAsync(KeyIndex);
        if (indexBytes is null)
            return new List<string>();

        var index = JsonSerializer.Deserialize<VariableIndex>(indexBytes);
        return index?.Keys ?? new List<string>();
    }

    /// <inheritdoc/>
    public async Task DeleteAllAsync()
    {
        List<string> keys = await GetKeysAsync();

        foreach (string key in keys)
        {
            await _cache.RemoveAsync(BuildDataKey(key));
            await _cache.RemoveAsync(BuildMetaKey(key));
        }

        await _cache.RemoveAsync(KeyIndex);
    }

    /// <summary>
    /// 将变量名添加到全局索引。
    /// </summary>
    private async Task AddToIndexAsync(string key)
    {
        byte[]? indexBytes = await _cache.GetAsync(KeyIndex);
        VariableIndex index = indexBytes is not null
            ? JsonSerializer.Deserialize<VariableIndex>(indexBytes) ?? new VariableIndex()
            : new VariableIndex();

        if (!index.Keys.Contains(key))
        {
            index.Keys.Add(key);
        }

        byte[] updated = JsonSerializer.SerializeToUtf8Bytes(index);
        await _cache.SetAsync(KeyIndex, updated, DefaultExpiry);
    }

    /// <summary>
    /// 从全局索引中移除变量名。
    /// </summary>
    private async Task RemoveFromIndexAsync(string key)
    {
        byte[]? indexBytes = await _cache.GetAsync(KeyIndex);
        if (indexBytes is null)
            return;

        var index = JsonSerializer.Deserialize<VariableIndex>(indexBytes);
        if (index is null)
            return;

        index.Keys.Remove(key);
        byte[] updated = JsonSerializer.SerializeToUtf8Bytes(index);
        await _cache.SetAsync(KeyIndex, updated, DefaultExpiry);
    }

    private static string BuildDataKey(string variableName) => $"{KeyPrefixData}{variableName}";

    private static string BuildMetaKey(string variableName) => $"{KeyPrefixMeta}{variableName}";

    /// <summary>
    /// 变量元数据，记录类型、创建时间和大小。
    /// </summary>
    private sealed class VariableMeta
    {
        public string ValueType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// 全局变量索引，存储所有暂存变量名列表。
    /// </summary>
    private sealed class VariableIndex
    {
        public List<string> Keys { get; set; } = new();
    }
}
