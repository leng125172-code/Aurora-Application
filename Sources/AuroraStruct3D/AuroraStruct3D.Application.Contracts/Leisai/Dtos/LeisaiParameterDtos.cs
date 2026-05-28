namespace AuroraStruct3D.Leisai.Dtos;

/// <summary>
/// 雷赛参数的数据类型分类（用于前端 UI 输入控件与校验提示）。
/// </summary>
public enum LeisaiParamDataKind
{
    /// <summary>无符号 16 位（默认）。</summary>
    Uint16 = 0,

    /// <summary>有符号 16 位。</summary>
    Int16 = 1,

    /// <summary>32 位高低分割（手册中 H/L 成对，仍按各自的 16 位寄存器编辑）。</summary>
    Int32Pair = 2,
}

/// <summary>
/// 单个参数的元数据定义。同时充当目录条目与 GET /metadata 接口返回 DTO。
/// </summary>
public class LeisaiParameterMetadataDto
{
    /// <summary>参数编号，如 "Pr0.07"。</summary>
    public string Pr { get; set; } = string.Empty;

    /// <summary>分组（0..9）。</summary>
    public int Group { get; set; }

    /// <summary>低 16 位寄存器地址（FC03 / FC06 使用此地址）。</summary>
    public ushort AddressLow { get; set; }

    /// <summary>参数中文名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>详细说明 / 取值含义。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>单位（"--" 已转为空字符串）。</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>建议最小值（手册取值范围下界，可能为 null 表示未给出）。</summary>
    public int? RangeMin { get; set; }

    /// <summary>建议最大值。</summary>
    public int? RangeMax { get; set; }

    /// <summary>默认值（手册出厂值，可能为 null）。</summary>
    public int? DefaultValue { get; set; }

    /// <summary>数据类型分类。</summary>
    public LeisaiParamDataKind DataKind { get; set; }

    /// <summary>
    /// 当前寄存器值（仅当按分组查询时由后端实时读取并填充；全量查询时为 null）。
    /// </summary>
    public int? CurrentValue { get; set; }
}

/// <summary>
/// 单个参数读取结果（FC03 读回的值）。
/// </summary>
public class LeisaiParameterReadResultDto
{
    /// <summary>低 16 位地址。</summary>
    public ushort AddressLow { get; set; }

    /// <summary>从设备读回的当前值（FC03 低字）。</summary>
    public ushort CurrentValue { get; set; }

    /// <summary>是否成功。</summary>
    public bool IsSuccess { get; set; }

    /// <summary>错误信息（失败时填充）。</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 单个参数写入结果（FC06 写入的值）。
/// </summary>
public class LeisaiParameterValueDto
{
    /// <summary>低 16 位地址。</summary>
    public ushort AddressLow { get; set; }

    /// <summary>写入设备的值（FC06 写入的低字）。</summary>
    public ushort Value { get; set; }

    /// <summary>是否成功。</summary>
    public bool IsSuccess { get; set; }

    /// <summary>错误信息（失败时填充）。</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 批量读请求。
/// </summary>
public class LeisaiBatchReadInputDto
{
    /// <summary>要读取的低 16 位地址列表（最多 256 个）。</summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MinLength(1)]
    public ushort[] AddressLows { get; set; } = [];
}

/// <summary>
/// 批量写请求。
/// </summary>
public class LeisaiBatchWriteInputDto
{
    /// <summary>要写入的项目列表（最多 256 个）。</summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MinLength(1)]
    public LeisaiBatchWriteItemDto[] Items { get; set; } = [];
}

/// <summary>
/// 批量写明细。
/// </summary>
public class LeisaiBatchWriteItemDto
{
    /// <summary>低 16 位地址。</summary>
    public ushort AddressLow { get; set; }

    /// <summary>写入值。</summary>
    public ushort Value { get; set; }
}

/// <summary>
/// 批量读取结果（FC03）。
/// </summary>
public class LeisaiBatchReadResultDto
{
    /// <summary>每项读取结果（顺序与输入对齐）。</summary>
    public LeisaiParameterReadResultDto[] Items { get; set; } = [];

    /// <summary>整体耗时（毫秒）。</summary>
    public int ElapsedMs { get; set; }

    /// <summary>成功项目数。</summary>
    public int SuccessCount { get; set; }

    /// <summary>失败项目数。</summary>
    public int FailureCount { get; set; }
}

/// <summary>
/// 批量写入结果（FC06）。
/// </summary>
public class LeisaiBatchResultDto
{
    /// <summary>每项写入结果（顺序与输入对齐）。</summary>
    public LeisaiParameterValueDto[] Items { get; set; } = [];

    /// <summary>整体耗时（毫秒）。</summary>
    public int ElapsedMs { get; set; }

    /// <summary>成功项目数。</summary>
    public int SuccessCount { get; set; }

    /// <summary>失败项目数。</summary>
    public int FailureCount { get; set; }
}
