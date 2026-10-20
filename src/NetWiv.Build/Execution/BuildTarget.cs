using System.Runtime.InteropServices;

namespace NetWiv.Build.Execution;

public sealed record BuildTarget(string RequestedTarget, string RuntimeIdentifier, string Platform, string Architecture)
{
    public static BuildTarget Resolve(string? target)
    {
        target ??= OperatingSystem.IsWindows() ? "x86_64-pc-windows-msvc" :
            OperatingSystem.IsLinux() ? "x86_64-unknown-linux-gnu" :
            OperatingSystem.IsMacOS() ? RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "aarch64-apple-darwin",
                System.Runtime.InteropServices.Architecture.X64 => "x86_64-apple-darwin",
                _ => throw new PlatformNotSupportedException("Unsupported macOS architecture.")
            } : throw new PlatformNotSupportedException("NetWiv supports Windows, Linux and macOS.");

        return target.ToLowerInvariant() switch
        {
            "x86_64-pc-windows-msvc" or "win-x64" => new(target, "win-x64", "windows", "x86_64"),
            "x86_64-unknown-linux-gnu" or "linux-x64" => new(target, "linux-x64", "linux", "x86_64"),
            "aarch64-apple-darwin" or "osx-arm64" => new(target, "osx-arm64", "macos", "aarch64"),
            // Source-only escape hatch; no official native package or CI coverage.
            "x86_64-apple-darwin" or "osx-x64" => new(target, "osx-x64", "macos", "x86_64"),
            "aarch64-pc-windows-msvc" or "win-arm64" => throw Unsupported(target),
            "aarch64-unknown-linux-gnu" or "linux-arm64" => throw Unsupported(target),
            _ => throw new ArgumentException($"Unknown target '{target}'. Supported package targets: x86_64-pc-windows-msvc, win-x64, x86_64-unknown-linux-gnu, linux-x64, aarch64-apple-darwin, osx-arm64.")
        };
    }

    public void EnsureHostOperatingSystem()
    {
        if ((Platform == "windows" && !OperatingSystem.IsWindows()) || (Platform == "linux" && !OperatingSystem.IsLinux()) || (Platform == "macos" && !OperatingSystem.IsMacOS()))
            throw new PlatformNotSupportedException($".NET NativeAOT does not support cross-OS publishing. Build {RuntimeIdentifier} on a {Platform} host.");
    }

    private static PlatformNotSupportedException Unsupported(string target) =>
        new($"Target '{target}' is not supported on Windows/Linux; these platforms currently require x64.");
}
