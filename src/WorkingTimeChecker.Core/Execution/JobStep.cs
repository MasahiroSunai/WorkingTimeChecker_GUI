namespace WorkingTimeChecker.Core.Execution;

public enum JobStepType
{
    Python,
    PowerShell
}

public sealed record JobStep(
    string Name,
    JobStepType Type,
    string FileName
);