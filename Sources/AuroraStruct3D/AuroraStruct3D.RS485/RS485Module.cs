using AuroraStruct3D.RS485.Ktech;
using AuroraStruct3D.RS485.Leisai;
using AuroraStruct3D.RS485.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.RS485;

/// <summary>
/// RS485 电机控制模块注册扩展
/// </summary>
/// <remarks>
/// 三轴电机硬件配置：
///   串口：/dev/ttyS6，波特率：115200，8N1
///   - 电机1（slave_id=1）：瓴控KTECH，KTECH CMD 0x9A 私有协议
///   - 电机2（slave_id=2）：瓴控KTECH，KTECH CMD 0x9A 私有协议
///   - 电机3（slave_id=3）：雷赛iCL-RS，Modbus RTU 0x03 @ 0x1003
/// </remarks>
public static class RS485Module
{
    /// <summary>三轴电机共用的 RS485 串口设备路径</summary>
    private const string DefaultPortName = "/dev/ttyS6";

    /// <summary>总线波特率</summary>
    private const int DefaultBaudRate = 115200;

    /// <summary>
    /// 注册 RS485 电机控制服务（单例，串口为共享资源）
    /// </summary>
    /// <param name="services">依赖注入容器</param>
    /// <param name="portName">RS485 串口路径，默认 /dev/ttyS6</param>
    /// <param name="baudRate">串口波特率，默认 115200</param>
    /// <returns>服务集合（支持链式调用）</returns>
    public static IServiceCollection AddRS485MotorServices(
        this IServiceCollection services,
        string portName = DefaultPortName,
        int baudRate = DefaultBaudRate
    )
    {
        // 注册共享 RS485 串口（单例，整个生命周期共用）
        services.AddSingleton<IRS485Port>(sp =>
        {
            ILogger<RS485Port> logger = sp.GetRequiredService<ILogger<RS485Port>>();
            RS485Port port = new(portName, baudRate, logger);
            port.Open();
            return port;
        });

        // 注册瓴控KTECH电机驱动（slave_id=1）
        services.AddSingleton<IMotorDriver>(sp =>
        {
            IRS485Port port = sp.GetRequiredService<IRS485Port>();
            ILogger<KtechMotorDriver> logger = sp.GetRequiredService<ILogger<KtechMotorDriver>>();
            return new KtechMotorDriver(slaveId: 1, port, logger);
        });

        // 注册瓴控KTECH电机驱动（slave_id=2）
        services.AddSingleton<IMotorDriver>(sp =>
        {
            IRS485Port port = sp.GetRequiredService<IRS485Port>();
            ILogger<KtechMotorDriver> logger = sp.GetRequiredService<ILogger<KtechMotorDriver>>();
            return new KtechMotorDriver(slaveId: 2, port, logger);
        });

        // 注册雷赛iCL-RS电机驱动（slave_id=3，Modbus RTU）
        services.AddSingleton<IMotorDriver>(sp =>
        {
            IRS485Port port = sp.GetRequiredService<IRS485Port>();
            ILogger<LeisaiMotorDriver> logger = sp.GetRequiredService<ILogger<LeisaiMotorDriver>>();
            return new LeisaiMotorDriver(slaveId: 3, port, logger);
        });

        // 注册统一电机控制服务
        services.AddSingleton<IMotorControlService, MotorControlService>();

        return services;
    }
}
