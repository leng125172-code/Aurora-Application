using AuroraStruct3D.ProductModels;
using Hangfire;
using Microsoft.Extensions.Logging;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace AuroraStruct3D.ProductModels.Jobs;

/// <summary>
/// 产品三维数模格式转换 Hangfire Job。
/// 负责将上传的非 PLY/OBJ 文件异步转换为 PLY 格式，并更新数模的转换状态。
/// 失败时自动重试最多 3 次（间隔由 Hangfire 指数退避策略决定）。
/// 转换开始/完成时通过 <see cref="IProductModelConversionNotifier"/> 实时推送 SignalR 通知到前端。
/// </summary>
public class ProductModelConversionJob : ITransientDependency
{
    private readonly IRepository<ProductModel, Guid> _productModelRepository;
    private readonly IBlobContainer<ProductModelBlobContainer> _blobContainer;
    private readonly IConversionEngine _conversionEngine;
    private readonly IProductModelConversionNotifier _notifier;
    private readonly ILogger<ProductModelConversionJob> _logger;

    public ProductModelConversionJob(
        IRepository<ProductModel, Guid> productModelRepository,
        IBlobContainer<ProductModelBlobContainer> blobContainer,
        IConversionEngine conversionEngine,
        IProductModelConversionNotifier notifier,
        ILogger<ProductModelConversionJob> logger
    )
    {
        _productModelRepository = productModelRepository;
        _blobContainer = blobContainer;
        _conversionEngine = conversionEngine;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// 执行格式转换：读取原始文件 → 调用转换引擎 → 保存 PLY 文件 → 更新数模状态。
    /// </summary>
    /// <param name="productModelId">需要转换的数模 ID</param>
    [AutomaticRetry(Attempts = 3)]
    [UnitOfWork]
    public async Task ExecuteAsync(Guid productModelId)
    {
        _logger.LogInformation("[ProductModelConversionJob] 开始转换数模 {Id}...", productModelId);

        ProductModel? productModel = await _productModelRepository.FindAsync(productModelId);

        if (productModel is null)
        {
            _logger.LogWarning(
                "[ProductModelConversionJob] 数模 {Id} 不存在，跳过转换。",
                productModelId
            );
            return;
        }

        if (productModel.ConversionStatus == ProductModelConversionStatus.Success)
        {
            _logger.LogInformation(
                "[ProductModelConversionJob] 数模 {Id} 已转换成功，跳过重复执行。",
                productModelId
            );
            return;
        }

        // ── 标记为转换中，推送"开始转换"通知 ───────────────────────────────────
        productModel.SetConverting();
        await _productModelRepository.UpdateAsync(productModel);
        await _notifier.NotifyStartedAsync(productModelId);

        try
        {
            // ── 读取原始文件 ─────────────────────────────────────────────────
            await using Stream originalStream = await _blobContainer.GetAsync(
                productModel.OriginalBlobName
            );

            // ── 调用转换引擎 ─────────────────────────────────────────────────
            await using Stream plyStream = await _conversionEngine.ConvertToPlyAsync(
                originalStream,
                productModel.FileFormat
            );

            // ── 保存 PLY 文件到 BLOB 存储 ────────────────────────────────────
            string convertedBlobName = $"converted/{productModelId:N}.ply";
            await _blobContainer.SaveAsync(convertedBlobName, plyStream, overrideExisting: true);

            // ── 标记为成功 ───────────────────────────────────────────────────
            productModel.SetSuccess(convertedBlobName);
            await _productModelRepository.UpdateAsync(productModel);

            // ── 删除原始文件，节省 BLOB 存储空间（静默忽略失败）──────────────────
            try
            {
                await _blobContainer.DeleteAsync(productModel.OriginalBlobName);
                _logger.LogInformation(
                    "[ProductModelConversionJob] 已删除数模 {Id} 的原始文件 {BlobName}。",
                    productModelId,
                    productModel.OriginalBlobName
                );
            }
            catch (Exception delEx)
            {
                _logger.LogWarning(
                    delEx,
                    "[ProductModelConversionJob] 删除数模 {Id} 原始文件 {BlobName} 失败，忽略（不影响转换结果）。",
                    productModelId,
                    productModel.OriginalBlobName
                );
            }

            _logger.LogInformation(
                "[ProductModelConversionJob] 数模 {Id} 转换成功，PLY 已保存至 {BlobName}。",
                productModelId,
                convertedBlobName
            );

            // ── 推送"转换成功"通知 ────────────────────────────────────────────
            await _notifier.NotifyFinishedAsync(productModelId, success: true, errorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[ProductModelConversionJob] 数模 {Id} 转换失败：{Message}",
                productModelId,
                ex.Message
            );

            // ── 标记为失败，记录错误信息 ──────────────────────────────────────
            productModel.SetFailed(ex.Message);
            await _productModelRepository.UpdateAsync(productModel);

            // ── 推送"转换失败"通知（Hangfire 重试前每次都通知）────────────────
            await _notifier.NotifyFinishedAsync(
                productModelId,
                success: false,
                errorMessage: ex.Message
            );

            // 重新抛出，Hangfire 才会触发重试
            throw;
        }
    }
}
