using AuroraStruct3D.OperatorFile.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuroraStruct3D.OperatorFile;

/// <summary>
/// 算子文件上传应用服务接口。
/// 路由基础路径：/api/app/operator-file
/// <para>
/// 上传时通过 <c>operatorId</c>（Guid）区分目标算子，如：
/// <list type="bullet">
///   <item><c>read_image</c>（7544f3f3-040d-4571-b0f2-741c8f17ab41）— 上传图片文件，自动生成灰度预览图</item>
///   <item><c>read_point_cloud</c>（a1b2c3d4-e5f6-7890-abcd-ef1234567890）— 上传点云文件，自动生成三视图灰度预览</item>
/// </list>
/// 系统会根据算子注册表中的输入端口定义自动校验文件格式。
/// </para>
/// </summary>
public interface IOperatorFileAppService : IApplicationService
{
    /// <summary>
    /// 上传文件到指定算子，并绑定到指定项目。
    /// 系统根据算子 GUID 查找算子注册表，校验输入端口类型与文件格式匹配后，
    /// 将文件保存到 BLOB 存储，并自动生成预览图（图片→灰度图，点云→三视图灰度图）。
    /// POST /api/app/operator-file/upload
    /// </summary>
    /// <param name="projectId">项目唯一标识（Guid）。</param>
    /// <param name="operatorId">算子唯一标识（Guid）。</param>
    /// <param name="file">上传的文件流。</param>
    /// <returns>上传结果，包含保存的 Blob 名称、文件元数据和预览图下载 URL。</returns>
    Task<UploadOperatorFileResultDto> UploadAsync(
        Guid projectId,
        Guid operatorId,
        IRemoteStreamContent file
    );

    /// <summary>
    /// 下载预览图。
    /// GET /api/app/operator-file/preview
    /// </summary>
    /// <param name="blobName">预览图 Blob 名称（来自上传结果中的 PreviewImages[].BlobName）。</param>
    /// <returns>预览图文件流。</returns>
    Task<IRemoteStreamContent> GetPreviewAsync(string blobName);

    /// <summary>
    /// 下载原始算子文件。
    /// GET /api/app/operator-file/download
    /// </summary>
    /// <param name="blobName">文件 Blob 名称。</param>
    /// <returns>文件流。</returns>
    Task<IRemoteStreamContent> DownloadAsync(string blobName);

    /// <summary>
    /// 确认文件已被工作流使用。
    /// 前端保存工作流后调用此接口，标记文件为已使用，取消过期时间避免被自动清理。
    /// POST /api/app/operator-file/confirm
    /// </summary>
    /// <param name="input">确认信息，包含算子 ID 和 BlobName。</param>
    Task ConfirmAsync(ConfirmOperatorFileInput input);
}
