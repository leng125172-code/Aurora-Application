namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>Per-execution display theme for workflow image annotations.</summary>
public enum WorkflowDisplayTheme
{
    Light,
    Dark,
}

/// <summary>Flows the requesting client's display theme to workflow operators.</summary>
public static class WorkflowDisplayThemeAmbient
{
    private static readonly AsyncLocal<WorkflowDisplayTheme?> CurrentTheme = new();

    public static WorkflowDisplayTheme Current => CurrentTheme.Value ?? WorkflowDisplayTheme.Light;

    public static IDisposable Push(WorkflowDisplayTheme theme)
    {
        WorkflowDisplayTheme? previous = CurrentTheme.Value;
        CurrentTheme.Value = theme;
        return new Restore(() => CurrentTheme.Value = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
