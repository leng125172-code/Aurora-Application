namespace AuroraStruct3D.Sessions;

/// <summary>设备类型，用于区分不同硬件设备的独占会话</summary>
public enum DeviceType
{
    /// <summary>工业相机</summary>
    Camera,

    /// <summary>结构光投影仪</summary>
    Projector,

    /// <summary>伺服电机（RS-485）</summary>
    Motor,
}
