namespace NetWiv.Build.Execution;

public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? Environment = null,
    bool CaptureOutput = false);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
