using AuroraStruct3D.AI;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// AI 模型模块数据库配置扩展。
/// </summary>
public static class AiModelDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置 AI 模型相关数据库表结构。
    /// </summary>
    public static void ConfigureAiModel(this ModelBuilder builder)
    {
        builder.Entity<AiModel>(b =>
        {
            b.ToTable($"{TablePrefix}AiModels");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(AiModelConsts.MaxNameLength);

            b.Property(x => x.Description).HasMaxLength(AiModelConsts.MaxDescriptionLength);

            b.Property(x => x.Version).HasMaxLength(AiModelConsts.MaxVersionLength);

            b.Property(x => x.LocationKey).HasMaxLength(AiModelConsts.MaxLocationKeyLength);

            b.Property(x => x.GenerationCondition)
                .HasMaxLength(AiModelConsts.MaxGenerationConditionLength);

            b.Property(x => x.ConversionPreference).HasConversion<int>().IsRequired();

            b.Property(x => x.ResolvedConversionType).HasConversion<int>().IsRequired();

            b.Property(x => x.FileCount).IsRequired();

            b.HasIndex(x => x.CreationTime);
            b.HasIndex(x => x.CreatorId);
            b.HasIndex(x => x.LocationKey);
            b.HasIndex(x => x.ConversionPreference);
            b.HasIndex(x => x.ResolvedConversionType);
        });

        builder.Entity<AiModelFile>(b =>
        {
            b.ToTable($"{TablePrefix}AiModelFiles");
            b.ConfigureByConvention();

            b.Property(x => x.AiModelId).IsRequired();

            b.Property(x => x.IsOriginalFile).IsRequired().HasDefaultValue(true);

            b.Property(x => x.IsConvertedFile).IsRequired().HasDefaultValue(false);

            b.Property(x => x.SourceFileId);

            b.Property(x => x.OriginalFileName)
                .IsRequired()
                .HasMaxLength(AiModelConsts.MaxOriginalFileNameLength);

            b.Property(x => x.DisplayName).IsRequired().HasMaxLength(AiModelConsts.MaxNameLength);

            b.Property(x => x.FileFormat)
                .IsRequired()
                .HasMaxLength(AiModelConsts.MaxFileFormatLength);

            b.Property(x => x.FileSizeBytes).IsRequired();

            b.Property(x => x.Md5).IsRequired().HasMaxLength(AiModelConsts.MaxMd5Length);

            b.Property(x => x.BlobName).IsRequired().HasMaxLength(AiModelConsts.MaxBlobNameLength);

            b.Property(x => x.FileRole).HasConversion<int>().IsRequired();

            b.Property(x => x.Md5Verified).IsRequired();

            b.Property(x => x.SortOrder).IsRequired();

            b.Property(x => x.ConversionTargetType).HasConversion<int?>();

            b.Property(x => x.ConversionStatus)
                .HasConversion<int>()
                .IsRequired()
                .HasDefaultValue(AiModelFileConversionStatus.None);

            b.Property(x => x.ConversionErrorMessage)
                .HasMaxLength(AiModelConsts.MaxErrorMessageLength);

            b.Property(x => x.ConversionTime);

            b.HasIndex(x => x.AiModelId);
            b.HasIndex(x => x.Md5);
            b.HasIndex(x => x.FileRole);
            b.HasIndex(x => x.FileFormat);
            b.HasIndex(x => new { x.AiModelId, x.SortOrder });
            b.HasIndex(x => x.IsOriginalFile);
            b.HasIndex(x => x.IsConvertedFile);
            b.HasIndex(x => x.SourceFileId);
            b.HasIndex(x => x.ConversionTargetType);
            b.HasIndex(x => x.ConversionStatus);

            b.HasOne<AiModel>()
                .WithMany()
                .HasForeignKey(x => x.AiModelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AiModelIdentifier>(b =>
        {
            b.ToTable($"{TablePrefix}AiModelIdentifiers");
            b.ConfigureByConvention();

            b.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(AiModelConsts.MaxIdentifierNameLength);

            b.Property(x => x.NormalizedName)
                .IsRequired()
                .HasMaxLength(AiModelConsts.MaxIdentifierNormalizedNameLength);

            b.HasIndex(x => x.NormalizedName).IsUnique();
        });

        builder.Entity<AiModelIdentifierLink>(b =>
        {
            b.ToTable($"{TablePrefix}AiModelIdentifierLinks");
            b.ConfigureByConvention();

            b.Property(x => x.AiModelId).IsRequired();
            b.Property(x => x.IdentifierId).IsRequired();

            b.HasIndex(x => x.AiModelId);
            b.HasIndex(x => x.IdentifierId);
            b.HasIndex(x => new { x.AiModelId, x.IdentifierId }).IsUnique();

            b.HasOne<AiModel>()
                .WithMany()
                .HasForeignKey(x => x.AiModelId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<AiModelIdentifier>()
                .WithMany()
                .HasForeignKey(x => x.IdentifierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AiModelOperationLog>(b =>
        {
            b.ToTable($"{TablePrefix}AiModelOperationLogs");
            b.ConfigureByConvention();

            b.Property(x => x.ModelName)
                .IsRequired()
                .HasMaxLength(AiModelConsts.MaxOperationLogModelNameLength);

            b.Property(x => x.OriginalFileName)
                .HasMaxLength(AiModelConsts.MaxOperationLogFileNameLength);

            b.Property(x => x.OperationType).HasConversion<int>().IsRequired();

            b.Property(x => x.ParameterSummary)
                .HasMaxLength(AiModelConsts.MaxOperationLogParameterLength);

            b.Property(x => x.ErrorMessage)
                .HasMaxLength(AiModelConsts.MaxOperationLogErrorMessageLength);

            b.Property(x => x.DurationMs).IsRequired();

            b.HasIndex(x => x.AiModelId);
            b.HasIndex(x => x.OperationType);
            b.HasIndex(x => x.OccurredAt);
            b.HasIndex(x => x.IsSuccess);
        });
    }
}
