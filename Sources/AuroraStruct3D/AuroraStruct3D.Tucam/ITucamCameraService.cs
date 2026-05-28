using AuroraStruct3D.Tucam.GenICam;
using AuroraStruct3D.Tucam.Interop;

namespace AuroraStruct3D.Tucam;

/// <summary>
/// 单个 GenICam 节点的读取结果（动态值快照）
/// </summary>
public class GenICamNodeValue
{
    /// <summary>节点名称</summary>
    public string NodeName { get; init; } = string.Empty;

    /// <summary>是否读取成功</summary>
    public bool Success { get; init; }

    /// <summary>节点当前值（统一字符串表示）</summary>
    public string? Value { get; init; }

    /// <summary>当前访问模式（动态可能因 Selector 切换而变化）</summary>
    public TuAccessMode Access { get; init; }

    /// <summary>是否被锁定</summary>
    public bool IsLocked { get; init; }

    /// <summary>错误描述（仅失败时填充）</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 相机帧数据，封装从TUCam SDK获取的原始帧信息
/// </summary>
public class CameraFrameData
{
    /// <summary>图像宽度（像素）</summary>
    public int Width { get; init; }

    /// <summary>图像高度（像素）</summary>
    public int Height { get; init; }

    /// <summary>像素位深度</summary>
    public int BitDepth { get; init; }

    /// <summary>通道数</summary>
    public int Channels { get; init; }

    /// <summary>帧索引</summary>
    public uint FrameIndex { get; init; }

    /// <summary>图像原始数据</summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// 帧图像质量评分（对焦清晰度 + 曝光质量），随实时预览帧一同计算并推送至前端。
/// </summary>
public record struct FrameQualityScore
{
    /// <summary>对焦清晰度评分（0-100，越高越清晰）</summary>
    public float FocusScore { get; init; }

    /// <summary>曝光质量评分（0-100，越高曝光越适中）</summary>
    public float ApertureScore { get; init; }

    /// <summary>光圈（曝光）调节建议方向</summary>
    public ApertureHint ApertureHint { get; init; }
}

/// <summary>
/// 光圈（曝光）调节建议方向
/// </summary>
public enum ApertureHint
{
    /// <summary>曝光良好，无需调节</summary>
    Good = 0,

    /// <summary>曝光过度，建议缩小光圈</summary>
    Decrease = 1,

    /// <summary>曝光不足，建议增大光圈</summary>
    Increase = 2,
}

/// <summary>
/// TUCam相机操作服务接口
/// </summary>
public interface ITucamCameraService
{
    /// <summary>
    /// 初始化SDK，扫描并返回检测到的相机数量
    /// </summary>
    /// <returns>检测到的相机数量</returns>
    Task<int> InitializeAsync();

    /// <summary>
    /// 反初始化SDK，释放所有资源
    /// </summary>
    Task UninitializeAsync();

    /// <summary>
    /// 打开指定索引的相机
    /// </summary>
    /// <param name="cameraIndex">相机索引，从0开始</param>
    Task OpenCameraAsync(int cameraIndex);

    /// <summary>
    /// 关闭指定索引的相机
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    Task CloseCameraAsync(int cameraIndex);

    /// <summary>
    /// 获取相机型号信息（需相机已打开）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <returns>相机型号字符串</returns>
    Task<string> GetCameraModelAsync(int cameraIndex);

    /// <summary>
    /// 按设备索引读取相机型号（无需打开相机，适用于扫描阶段）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <returns>相机型号字符串</returns>
    Task<string> GetModelByIndexAsync(int cameraIndex);

    /// <summary>
    /// 获取属性值（浮点型，如曝光时间、增益等）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="propId">属性ID</param>
    /// <param name="channel">通道索引，默认为0</param>
    /// <returns>属性当前值</returns>
    Task<double> GetPropertyValueAsync(int cameraIndex, TUCamIdProp propId, int channel = 0);

    /// <summary>
    /// 设置属性值（浮点型，如曝光时间、增益等）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="propId">属性ID</param>
    /// <param name="value">要设置的值</param>
    /// <param name="channel">通道索引，默认为0</param>
    Task SetPropertyValueAsync(int cameraIndex, TUCamIdProp propId, double value, int channel = 0);

    /// <summary>
    /// 获取能力值（整数型，如分辨率、位深等）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="capaId">能力ID</param>
    /// <returns>能力当前值</returns>
    Task<int> GetCapabilityValueAsync(int cameraIndex, TUCamIdCapa capaId);

    /// <summary>
    /// 设置能力值（整数型，如分辨率、位深等）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="capaId">能力ID</param>
    /// <param name="value">要设置的值</param>
    Task SetCapabilityValueAsync(int cameraIndex, TUCamIdCapa capaId, int value);

    /// <summary>
    /// 开始采集（连续模式）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    Task StartCaptureAsync(int cameraIndex);

    /// <summary>
    /// 停止采集
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    Task StopCaptureAsync(int cameraIndex);

    /// <summary>
    /// 抓取一帧图像（同步等待）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="timeoutMs">超时毫秒数，默认3000</param>
    /// <returns>帧数据</returns>
    Task<CameraFrameData> GrabFrameAsync(int cameraIndex, int timeoutMs = 3000);

    /// <summary>
    /// 检查指定相机是否已打开
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    bool IsCameraOpen(int cameraIndex);

    /// <summary>
    /// 检查指定相机是否正在连续采集
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <returns>正在采集且采集缓冲有效时返回 true</returns>
    bool IsCapturing(int cameraIndex);

    /// <summary>
    /// 注入相机设备索引 → 数据库 ID 的映射，用于写入操作日志
    /// </summary>
    /// <param name="deviceIds">key = SDK cameraIndex，value = CameraDevice.Id</param>
    void SetCameraDeviceIdMapping(IReadOnlyDictionary<int, Guid> deviceIds);

    // ─── ROI 区域控制 ────────────────────────────────────────────────────────

    /// <summary>获取当前硬件 ROI 区域</summary>
    Task<TUCamRoiAttr> GetRoiAsync(int cameraIndex);

    /// <summary>设置硬件 ROI 区域</summary>
    Task SetRoiAsync(int cameraIndex, TUCamRoiAttr roi);

    // ─── 触发模式 ─────────────────────────────────────────────────────────────

    /// <summary>获取触发参数</summary>
    Task<TUCamTriggerAttr> GetTriggerAsync(int cameraIndex);

    /// <summary>设置触发参数</summary>
    Task SetTriggerAsync(int cameraIndex, TUCamTriggerAttr trigger);

    /// <summary>发送软件触发信号</summary>
    Task DoSoftwareTriggerAsync(int cameraIndex);

    // ─── 触发输出 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 获取触发输出参数
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="port">输出端口（0/1/2）</param>
    Task<TUCamTrgOutAttr> GetTriggerOutAsync(int cameraIndex, int port);

    /// <summary>设置触发输出参数</summary>
    Task SetTriggerOutAsync(int cameraIndex, TUCamTrgOutAttr trgOut);

    // ─── 计算 ROI（AE/WB 测光区域）─────────────────────────────────────────

    /// <summary>获取自动曝光或白平衡的计算区域</summary>
    Task<TUCamCalcRoiAttr> GetCalcRoiAsync(int cameraIndex, TUCamIdCalcRoi calcId);

    /// <summary>设置自动曝光或白平衡的计算区域</summary>
    Task SetCalcRoiAsync(int cameraIndex, TUCamCalcRoiAttr calcRoi);

    // ─── 用户配置文件 ─────────────────────────────────────────────────────────

    /// <summary>加载用户配置文件（需在停止采集后调用）</summary>
    Task LoadProfilesAsync(int cameraIndex, string profileName);

    /// <summary>保存用户配置文件</summary>
    Task SaveProfilesAsync(int cameraIndex, string profileName);

    // ─── 设备信息查询（数值型）────────────────────────────────────────────────

    /// <summary>
    /// 获取整型设备信息（如 CurrentWidth / CurrentHeight / Bus 等）
    /// </summary>
    Task<int> GetDeviceNumericInfoAsync(int cameraIndex, TUCamIdInfo infoId);

    // ─── 属性/能力元数据 ──────────────────────────────────────────────────────

    /// <summary>
    /// 获取属性的元信息（包括取值范围 dbValMin/dbValMax 等）
    /// </summary>
    Task<TUCamPropAttr> GetPropertyAttrAsync(int cameraIndex, TUCamIdProp propId);

    /// <summary>
    /// 获取能力的元信息（包括可选值数量 nValMax 等）
    /// </summary>
    Task<TUCamCapaAttr> GetCapabilityAttrAsync(int cameraIndex, TUCamIdCapa capaId);

    // ─── 原始帧抓取（用于单帧快照/RTP推流）──────────────────────────────────

    /// <summary>
    /// 抓取一帧并编码为 JPEG 字节数组（连续采集模式下调用）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="timeoutMs">超时毫秒数，默认3000</param>
    /// <param name="maxWidth">JPEG 输出最大宽度，0 表示保持原始宽度</param>
    /// <param name="jpegQuality">JPEG 编码质量，范围 1-100</param>
    /// <param name="imageRotationAngle">图像顺时针旋转角度（度，支持 0/90/180/270）</param>
    /// <returns>JPEG 字节数组及对应帧的图像质量评分</returns>
    Task<(byte[] JpegBytes, FrameQualityScore Quality)> GrabFrameRawAsync(
        int cameraIndex,
        int timeoutMs = 3000,
        int maxWidth = 0,
        int jpegQuality = 85,
        int imageRotationAngle = 0
    );

    /// <summary>
    /// 仅抓取并丢弃一帧（不进行 JPEG 编码或质量评分），用于快速清空 SDK 环形缓冲区。
    /// 当预览循环不需要推送新帧时调用，防止环形缓冲区溢出（Ring buffer full）导致
    /// USB 总线被单台相机打满、其他相机无法获得带宽。
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="timeoutMs">等待帧超时时间（毫秒），超时返回 false</param>
    /// <returns>true 表示成功消费了一帧；false 表示超时无帧或采集未启动</returns>
    Task<bool> DrainFrameAsync(int cameraIndex, int timeoutMs = 1000);

    // ─── GenICam 原生节点访问 ─────────────────────────────────────────────────

    /// <summary>
    /// 通过 GenICam 节点名称读取整型值
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="nodeName">GenICam 节点名称</param>
    /// <returns>节点当前值，读取失败时返回 0</returns>
    Task<long> GetGenICamIntAsync(int cameraIndex, string nodeName);

    /// <summary>
    /// 通过 GenICam 节点名称写入整型值
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="nodeName">GenICam 节点名称</param>
    /// <param name="value">要写入的值</param>
    Task SetGenICamIntAsync(int cameraIndex, string nodeName, long value);

    /// <summary>
    /// 通过 GenICam 节点名称读取浮点值
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="nodeName">GenICam 节点名称</param>
    /// <returns>节点当前值，读取失败时返回 0.0</returns>
    Task<double> GetGenICamFloatAsync(int cameraIndex, string nodeName);

    /// <summary>
    /// 通过 GenICam 节点名称写入浮点值
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="nodeName">GenICam 节点名称</param>
    /// <param name="value">要写入的值</param>
    Task SetGenICamFloatAsync(int cameraIndex, string nodeName, double value);

    /// <summary>
    /// 执行 GenICam Command 节点（如 ExposureAutoOncePulse）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="nodeName">GenICam 命令节点名称</param>
    Task ExecuteGenICamCommandAsync(int cameraIndex, string nodeName);

    /// <summary>
    /// 通过 GenICam 节点名称读取字符串值（String 类型节点，如 DeviceSerialNumber）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="nodeName">GenICam 节点名称</param>
    /// <returns>读取到的字符串；节点不可访问或为空时返回 null</returns>
    Task<string?> GetGenICamStringAsync(int cameraIndex, string nodeName);

    /// <summary>
    /// 通过 GenICam 节点名称写入字符串值（String 类型节点，如 DeviceUserID）
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="nodeName">GenICam 节点名称</param>
    /// <param name="value">要写入的字符串值</param>
    Task SetGenICamStringAsync(int cameraIndex, string nodeName, string value);

    // ─── GenICam 动态 NodeMap 与依赖图 ─────────────────────────────────────────

    /// <summary>
    /// 获取已缓存的 GenICam NodeMap 快照；
    /// 若尚未首次枚举（例如相机未打开或预跑未完成），返回 null。
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    GenICamNodeMap? GetCachedNodeMap(int cameraIndex);

    /// <summary>
    /// 获取已缓存的选择器依赖图；尚未探测则返回 null。
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    GenICamDependencyGraph? GetCachedDependencyGraph(int cameraIndex);

    /// <summary>
    /// 重新枚举 GenICam NodeMap 并探测选择器依赖（覆盖原有缓存）。
    /// 调用方需自行确保此时相机未在采集，否则可能影响 SDK 状态。
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    Task<GenICamNodeMap> RefreshGenICamNodeMapAsync(int cameraIndex);

    /// <summary>
    /// 按节点名批量读取节点当前值与动态访问模式（不刷新整张 NodeMap，只查值）。
    /// 根据缓存的节点类型自动选择 Int / Float / String / Enumeration 读取方式。
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="nodeNames">节点名列表</param>
    Task<IReadOnlyList<GenICamNodeValue>> ReadGenICamNodesAsync(
        int cameraIndex,
        IReadOnlyList<string> nodeNames
    );

    /// <summary>
    /// 将一批节点的回读结果（Value / Access / IsLocked）同步写回服务端 NodeMap 缓存。
    /// 在 SetGenICamParam 成功写入并回读后调用，确保缓存与硬件实际值一致，
    /// 避免页面刷新时从缓存读到过期的初始枚举值。
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <param name="values">回读结果列表（仅 Success=true 的条目会被写回）</param>
    void UpdateCachedNodeValues(int cameraIndex, IReadOnlyList<GenICamNodeValue> values);
}
