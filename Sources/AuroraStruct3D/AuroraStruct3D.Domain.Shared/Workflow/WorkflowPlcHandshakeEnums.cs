namespace AuroraStruct3D.Workflow;

public enum WorkflowPlcHandshakePhase
{
    Idle = 0,
    Accepted = 1,
    Running = 2,
    ResultPending = 3,
    Fault = 4,
}

public enum WorkflowInspectionDecision
{
    None = 0,
    Ok = 1,
    Ng = 2,
    Error = 3,
    Canceled = 4,
}

public enum WorkflowPlcHandshakeErrorCode
{
    None = 0,
    WorkflowFailed = 1,
    ResultVariableMissing = 2,
    ResultVariableTypeMismatch = 3,
    Canceled = 4,
    PlcCommunicationFailed = 5,
}
