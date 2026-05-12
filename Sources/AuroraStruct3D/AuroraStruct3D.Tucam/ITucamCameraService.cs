using AuroraStruct3D.Tucam.Interop;

namespace AuroraStruct3D.Tucam;

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
    /// 获取相机型号信息
    /// </summary>
    /// <param name="cameraIndex">相机索引</param>
    /// <returns>相机型号字符串</returns>
    Task<string> GetCameraModelAsync(int cameraIndex);

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
}
