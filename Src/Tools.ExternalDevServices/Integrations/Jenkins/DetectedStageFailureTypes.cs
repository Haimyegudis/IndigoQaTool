namespace Tools.ExternalDevServices.Integrations.Jenkins;

[Flags]
public enum DetectedStageFailureTypes
{
    None = 0,
    CanceledDueToTimeout = 1,
    DotNetTestsRunFailure = 2,
    DotNetBuildFailure = 4,
    TimeoutDueToLongRunningTest = 8
}