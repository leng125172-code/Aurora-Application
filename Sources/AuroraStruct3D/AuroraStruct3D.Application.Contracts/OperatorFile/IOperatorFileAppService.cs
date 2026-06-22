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
///   <item><c>read_image</c>（7544f3f3-040d-4571-b0f2-741c8f17ab41）— 上传图片文件</item>
///   <item><c>read_point_cloud</c>（a1b2c3d4-e5f6-7890-abcd-ef1234567890）— 上传点云文件</item>
/// </list>
/// 系统会根据算子注册表中的输入端口定义自动校验文件格式。
/// </para>
/// </summary>
public interface IOperatorFileAppService : IApplicationService
{
    /// <summary>
    /// 上传文件到指定算子。
    /// 系统根据算子 GUID 查找算子注册表，校验输入端口类型与文件格式匹配后，
    /// 将文件保存到 BLOB 存储并返回结果。
    /// POST /api/app/operator-file/upload
    /// </summary>
    /// <param name="operatorId">算子唯一标识（Guid）。</param>
    /// <param name="file">上传的文件流。</param>
    /// <returns>上传结果，包含保存的 Blob 名称和文件元数据。</returns>
    Task<UploadOperatorFileResultDto> UploadAsync(Guid operatorId, IRemoteStreamContent file);
}