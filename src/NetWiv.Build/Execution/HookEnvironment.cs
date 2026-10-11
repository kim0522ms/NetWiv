namespace NetWiv.Build.Execution;

public static class HookEnvironment
{
    public static IReadOnlyDictionary<string, string?> Create(BuildTarget target, bool debug, Configuration.ConfigDocument? config = null) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TAURI_ENV_TARGET_TRIPLE"] = target.RequestedTarget,
            ["TAURI_ENV_ARCH"] = target.Architecture,
            ["TAURI_ENV_PLATFORM"] = target.Platform == "macos" ? "darwin" : target.Platform,
            ["TAURI_ENV_FAMILY"] = target.Platform == "windows" ? "windows" : "unix",
            ["TAURI_ENV_PLATFORM_VERSION"] = Environment.OSVersion.VersionString,
            ["TAURI_ENV_DEBUG"] = debug ? "true" : "false",
            ["TAURI_CONFIG"] = config?.CliPatch?.ToJsonString(),
            ["NETWIV_ENV_RUNTIME_IDENTIFIER"] = target.RuntimeIdentifier,
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["NETWIV_DEV_URL"] = null
        };
}
