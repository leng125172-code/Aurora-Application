using AuroraStruct3D.SerialPorts.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 485 串口管理应用服务。
/// </summary>
public interface ISerialPortAppService : IApplicationService
{
    /// <summary>获取串口配置列表，并附带实际打开状态</summary>
    Task<PagedResultDto<SerialPortConfigDto>> GetListAsync(GetSerialPortListDto input);

    /// <summary>获取单个串口配置</summary>
    Task<SerialPortConfigDto> GetAsync(Guid id);

    /// <summary>扫描系统当前存在的全部串口并同步到数据库</summary>
    Task<SerialPortScanResultDto> ScanSystemPortsAsync();

    /// <summary>更新串口配置，不允许修改系统串口号</summary>
    Task<SerialPortConfigDto> UpdateAsync(Guid id, UpdateSerialPortConfigDto input);

    /// <summary>打开串口</summary>
    Task<SerialPortConfigDto> ConnectAsync(Guid id, ConnectSerialPortDto input);

    /// <summary>关闭串口</summary>
    Task<SerialPortConfigDto> DisconnectAsync(Guid id);

    /// <summary>发送原始串口数据并读取响应</summary>
    Task<SerialPortRawResponseDto> SendRawAsync(Guid id, SerialPortRawSendDto input);

    /// <summary>分页查询串口操作日志</summary>
    Task<PagedResultDto<SerialPortOperationLogDto>> GetLogsAsync(GetSerialPortLogListDto input);
}
