using System.IO.Ports;
using System.Text;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.RS485;
using AuroraStruct3D.SerialPorts.Dtos;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 485 串口管理应用服务实现。
/// </summary>
[Authorize]
public class SerialPortAppService : AuroraStruct3DAppService, ISerialPortAppService
{
    private readonly ISerialPortConfigRepository _serialPortConfigRepository;
    private readonly ISerialPortOperationLogRepository _operationLogRepository;
    private readonly IMotorControlService _motorControlService;
    private readonly IDeviceStateManager _deviceStateManager;

    public SerialPortAppService(
        ISerialPortConfigRepository serialPortConfigRepository,
        ISerialPortOperationLogRepository operationLogRepository,
        IMotorControlService motorControlService,
        IDeviceStateManager deviceStateManager
    )
    {
        _serialPortConfigRepository = serialPortConfigRepository;
        _operationLogRepository = operationLogRepository;
        _motorControlService = motorControlService;
        _deviceStateManager = deviceStateManager;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<SerialPortConfigDto>> GetListAsync(GetSerialPortListDto input)
    {
        List<SerialPortConfig> configs = await _serialPortConfigRepository.GetListAsync(
            input.IsEnabled
        );
        IEnumerable<SerialPortConfig> filtered = configs;
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            string filter = input.Filter.Trim();
            filtered = filtered.Where(config =>
                config.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || config.PortName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            );
        }

        List<SerialPortConfig> filteredList = filtered.ToList();
        List<SerialPortConfigDto> page = filteredList
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .Select(ToDto)
            .ToList();

        return new PagedResultDto<SerialPortConfigDto>(filteredList.Count, page);
    }

    /// <inheritdoc/>
    public async Task<SerialPortConfigDto> GetAsync(Guid id)
    {
        SerialPortConfig config = await _serialPortConfigRepository.GetAsync(id);
        return ToDto(config);
    }

    /// <inheritdoc/>
    public async Task<SerialPortScanResultDto> ScanSystemPortsAsync()
    {
        EnsureManualOrMaintenanceMode();
        string[] portNames = SerialPort
            .GetPortNames()
            .OrderBy(portName => portName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string portName in portNames)
        {
            SerialPortConfig? existing = await _serialPortConfigRepository.FindByPortNameAsync(
                portName
            );
            if (existing is not null)
            {
                continue;
            }

            SerialPortConfig config = new(
                GuidGenerator.Create(),
                $"串口 {portName}",
                portName,
                baudRate: 0
            );
            await _serialPortConfigRepository.InsertAsync(config, autoSave: true);
        }

        List<SerialPortConfig> configs = await _serialPortConfigRepository.GetListAsync();
        return new SerialPortScanResultDto
        {
            Count = portNames.Length,
            Items = configs.Select(ToDto).ToList(),
        };
    }

    /// <inheritdoc/>
    public async Task<SerialPortConfigDto> UpdateAsync(Guid id, UpdateSerialPortConfigDto input)
    {
        EnsureManualOrMaintenanceMode();
        SerialPortConfig config = await _serialPortConfigRepository.GetAsync(id);
        config.SetDisplayName(input.DisplayName);
        config.SetDescription(input.Description);
        config.SetEnabled(input.IsEnabled);
        config.SetParameters(
            input.BaudRate,
            input.DataBits,
            input.Parity,
            input.StopBits,
            input.Handshake
        );
        await _serialPortConfigRepository.UpdateAsync(config);
        return ToDto(config);
    }

    /// <inheritdoc/>
    public async Task<SerialPortConfigDto> ConnectAsync(Guid id, ConnectSerialPortDto input)
    {
        EnsureManualOrMaintenanceMode();
        SerialPortConfig config = await _serialPortConfigRepository.GetAsync(id);
        if (input.BaudRate.HasValue && input.BaudRate.Value != config.BaudRate)
        {
            config.SetParameters(
                input.BaudRate.Value,
                config.DataBits,
                config.Parity,
                config.StopBits,
                config.Handshake
            );
            await _serialPortConfigRepository.UpdateAsync(config);
        }

        if (config.BaudRate <= 0)
        {
            throw new UserFriendlyException("请先选择串口连接波特率");
        }

        await _motorControlService.OpenSerialPortAsync(config);
        return ToDto(config);
    }

    /// <inheritdoc/>
    public async Task<SerialPortConfigDto> DisconnectAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        SerialPortConfig config = await _serialPortConfigRepository.GetAsync(id);
        _motorControlService.CloseSerialPort(id);
        return ToDto(config);
    }

    /// <inheritdoc/>
    public async Task<SerialPortRawResponseDto> SendRawAsync(Guid id, SerialPortRawSendDto input)
    {
        EnsureManualOrMaintenanceMode();
        SerialPortConfig config = await _serialPortConfigRepository.GetAsync(id);
        byte[] request = input.IsHex
            ? ParseHex(input.Payload)
            : Encoding.UTF8.GetBytes(
                input.Payload + (input.AppendNewLine ? Environment.NewLine : string.Empty)
            );

        byte[] response = await _motorControlService.SendRawAsync(
            config,
            request,
            input.ExpectedResponseLength,
            input.TimeoutMs
        );

        return new SerialPortRawResponseDto
        {
            SentHex = Convert.ToHexString(request),
            ResponseHex = Convert.ToHexString(response),
            ResponseText = DecodeResponseText(response),
            ResponseLength = response.Length,
        };
    }

    /// <summary>
    /// 校验当前运行模式必须为手动或检修，否则抛出业务异常。
    /// </summary>
    private void EnsureManualOrMaintenanceMode()
    {
        DeviceRunMode mode = _deviceStateManager.RunMode;
        if (mode is not (DeviceRunMode.Manual or DeviceRunMode.Maintenance))
        {
            throw new UserFriendlyException(
                $"当前运行模式为【{mode switch {
                    DeviceRunMode.Online => "联机",
                    DeviceRunMode.Auto   => "自动",
                    _                    => mode.ToString()
                }}】，串口操作仅允许在手动模式或检修模式下执行"
            );
        }
    }

    /// <summary>
    /// 将串口实体转换为 DTO，并补充运行态打开状态。
    /// </summary>
    private SerialPortConfigDto ToDto(SerialPortConfig config)
    {
        return new SerialPortConfigDto
        {
            Id = config.Id,
            DisplayName = config.DisplayName,
            Description = config.Description,
            IsEnabled = config.IsEnabled,
            PortName = config.PortName,
            BaudRate = config.BaudRate,
            DataBits = config.DataBits,
            Parity = config.Parity,
            StopBits = config.StopBits,
            Handshake = config.Handshake,
            IsOpen = _motorControlService.IsSerialPortOpen(config.Id),
            SupportedBaudRates = SerialPortConsts.SupportedBaudRates,
        };
    }

    /// <summary>
    /// 解析十六进制文本，允许空格、逗号和 0x 前缀。
    /// </summary>
    private static byte[] ParseHex(string payload)
    {
        string normalized = payload
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace("\t", string.Empty)
            .Replace(",", string.Empty);

        if (normalized.Length == 0)
        {
            return [];
        }

        if (normalized.Length % 2 != 0)
        {
            throw new UserFriendlyException("十六进制内容长度必须为偶数");
        }

        byte[] bytes = new byte[normalized.Length / 2];
        for (int byteIndex = 0; byteIndex < bytes.Length; byteIndex++)
        {
            string hexByte = normalized.Substring(byteIndex * 2, 2);
            if (
                !byte.TryParse(
                    hexByte,
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out bytes[byteIndex]
                )
            )
            {
                throw new UserFriendlyException($"十六进制内容包含非法字节：{hexByte}");
            }
        }

        return bytes;
    }

    /// <summary>
    /// 尝试将响应字节解码为文本，失败时返回空字符串。
    /// </summary>
    private static string DecodeResponseText(byte[] response)
    {
        if (response.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(response);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<SerialPortOperationLogDto>> GetLogsAsync(
        GetSerialPortLogListDto input
    )
    {
        long totalCount = await _operationLogRepository.GetCountAsync(
            input.SerialPortConfigId,
            input.OperationType,
            input.IsFailedOnly,
            input.StartTime,
            input.EndTime
        );

        List<SerialPortOperationLog> items = await _operationLogRepository.GetPagedListAsync(
            input.SerialPortConfigId,
            input.SkipCount,
            input.MaxResultCount,
            input.OperationType,
            input.IsFailedOnly,
            input.StartTime,
            input.EndTime
        );

        return new PagedResultDto<SerialPortOperationLogDto>(
            totalCount,
            items
                .Select(log => new SerialPortOperationLogDto
                {
                    Id = log.Id,
                    SerialPortConfigId = log.SerialPortConfigId,
                    OperationType = log.OperationType,
                    OccurredAt = log.OccurredAt,
                    IsSuccess = log.IsSuccess,
                    ParameterSummary = log.ParameterSummary,
                    ErrorMessage = log.ErrorMessage,
                    RoundTripMs = log.RoundTripMs,
                })
                .ToList()
        );
    }
}
