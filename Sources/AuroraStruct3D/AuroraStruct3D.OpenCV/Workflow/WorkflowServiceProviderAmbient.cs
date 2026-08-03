namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>Per-execution service provider bridge for infrastructure operators.</summary>
public static class WorkflowServiceProviderAmbient
{
    private static readonly AsyncLocal<IServiceProvider?> CurrentProvider = new();
    public static IServiceProvider Current => CurrentProvider.Value
        ?? throw new InvalidOperationException("当前工作流执行未提供服务作用域。");

    public static IDisposable Push(IServiceProvider provider)
    {
        IServiceProvider? previous = CurrentProvider.Value;
        CurrentProvider.Value = provider;
        return new Restore(() => CurrentProvider.Value = previous);
    }

    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}
