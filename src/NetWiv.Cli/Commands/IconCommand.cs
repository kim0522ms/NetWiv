using NetWiv.Build.Execution;

namespace NetWiv.Cli;

internal static class IconCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help")) { await output.WriteLineAsync("Usage: netwiv icon [INPUT] [-o OUTPUT] [-p PNG_SIZES]"); return 0; }
        var destination = Path.GetFullPath(arguments.TakeOption("--output", "-o") ?? "icons", workingDirectory);
        var sizesText = arguments.TakeOption("--png", "-p");
        var iosColor = arguments.TakeOption("--ios-color");
        var input = Path.GetFullPath(arguments.TakePositional() ?? "app-icon.png", workingDirectory);
        arguments.EnsureEmpty();
        if (iosColor is not null) throw new NotSupportedException("--ios-color is outside the Windows/Linux desktop milestone.");
        if (!File.Exists(input)) throw new FileNotFoundException($"Icon source not found: {input}");
        var tool = FindImageMagick() ?? throw new NotSupportedException("Icon generation requires ImageMagick (`magick` or `convert`) on PATH. No files were created.");
        Directory.CreateDirectory(destination);
        var sizes = sizesText is null ? new[] { 32, 128, 256, 512 } : sizesText.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        foreach (var size in sizes)
        {
            if (size is < 1 or > 4096) throw new ArgumentException($"Invalid PNG icon size {size}.");
            await Require(tool, [input, "-resize", $"{size}x{size}", Path.Combine(destination, $"{size}x{size}.png")], workingDirectory, cancellationToken);
        }
        await Require(tool, [input, "-define", "icon:auto-resize=256,128,64,48,32,16", Path.Combine(destination, "icon.ico")], workingDirectory, cancellationToken);
        await output.WriteLineAsync($"Generated desktop icons in {destination}");
        return 0;
    }
    private static string? FindImageMagick() => Find("magick.exe") ?? Find("magick") ?? Find("convert");
    private static string? Find(string name) => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(path => Path.Combine(path.Trim('"'), name)).FirstOrDefault(File.Exists);
    private static async Task Require(string tool, IReadOnlyList<string> args, string cwd, CancellationToken token) { var result = await ProcessRunner.RunAsync(new ProcessSpec(tool, args, cwd, CaptureOutput: true), token); if (result.ExitCode != 0) throw new InvalidOperationException($"ImageMagick failed: {result.StandardError.Trim()}"); }
}
