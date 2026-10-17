using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NetWiv;
using NetWiv.Platform.Desktop;

internal static partial class Program
{
    private const string Identifier = "dev.netwiv.sandbox-smoke";
    private static int _failures;

    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS only.");
        if (args.Length != 2 || args[0] is not ("--sandbox" or "--control") || args[1] is not ("write" or "read"))
            throw new ArgumentException("Usage: --sandbox|--control write|read");
        bool sandbox = args[0] == "--sandbox";
        string phase = args[1];
        string outside = Environment.GetEnvironmentVariable("NETWIV_SANDBOX_OUTSIDE_DIRECTORY")
            ?? throw new ArgumentException("Set a test-owned outside directory.");
        var guest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = new Builder().UseRuntime(new DesktopRuntime())
            .Metadata(new AppMetadata("Sandbox smoke", "0.1.0", Identifier))
            .UseEmbeddedAssets()
            .Window("main", "NetWiv sandbox smoke", "index.html", 480, 320)
            .RegisterCommand("echo", (_, args, _) => ValueTask.FromResult(args.GetProperty("value").Clone()))
            .RegisterCommand("phase", (_, _, _) => ValueTask.FromResult(JsonValue.From(phase)))
            .RegisterCommand("complete", (_, args, _) =>
            {
                guest.TrySetResult(args.GetProperty("success").GetBoolean());
                return ValueTask.FromResult(JsonValue.Null);
            })
            .Setup(app => _ = VerifyAsync(app.Handle));
        using var app = builder.Build();
        using var watchdog = new Timer(_ => { Console.WriteLine("FAIL watchdog"); Environment.Exit(124); },
            null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        app.Run();
        return _failures == 0 ? 0 : 1;

        async Task VerifyAsync(AppHandle handle)
        {
            try
            {
                Check("NativeAOT execution", !RuntimeFeature.IsDynamicCodeSupported);
                var home = Marshal.PtrToStringUTF8(Utf8String(HomeDirectory(), Selector("UTF8String")))!;
                Check("Foundation container selection", home.Contains("/Library/Containers/" + Identifier + "/Data", StringComparison.Ordinal) == sandbox);
                string outsideFile = Path.Combine(outside, "netwiv-sandbox-" + Guid.NewGuid().ToString("N"));
                bool denied = false;
                try { File.WriteAllText(outsideFile, "synthetic fixture"); File.Delete(outsideFile); }
                catch (UnauthorizedAccessException) { denied = true; }
                Check("outside write policy", denied == sandbox);

                foreach (var (label, resolve) in new (string, Func<AppHandle, CancellationToken, Task<string>>)[]
                {
                    ("app data", PathApi.AppDataDirectoryAsync),
                    ("app config", PathApi.AppConfigDirectoryAsync),
                    ("app cache", PathApi.AppCacheDirectoryAsync),
                    ("app log", PathApi.AppLogDirectoryAsync)
                })
                {
                    var path = await resolve(handle, CancellationToken.None);
                    bool contained = Path.GetFullPath(path).StartsWith(home.TrimEnd('/') + "/", StringComparison.Ordinal);
                    Check(label + " container path", contained);
                    if (!contained) continue;
                    var file = Path.Combine(path, "sandbox-smoke-" + label.Replace(' ', '-') + ".txt");
                    if (phase == "write")
                    {
                        Directory.CreateDirectory(path);
                        File.WriteAllText(file, "path-api-persistence");
                    }
                    Check(label + " file persistence", File.ReadAllText(file) == "path-api-persistence");
                    if (phase == "read") File.Delete(file);
                }

                string data = Path.Combine(home, "Library", "Application Support", Identifier);
                string marker = Path.Combine(data, "persistence-fixture.txt");
                if (phase == "write")
                {
                    Directory.CreateDirectory(data);
                    File.WriteAllText(marker, "sandbox-persistence");
                    Check("container file write", File.ReadAllText(marker) == "sandbox-persistence");
                }
                else
                {
                    Check("container file persistence", File.ReadAllText(marker) == "sandbox-persistence");
                    File.Delete(marker);
                }
                Check("WKWebView IPC and local storage", await guest.Task.WaitAsync(TimeSpan.FromSeconds(40)));
            }
            catch (Exception error)
            {
                Interlocked.Increment(ref _failures);
                // Error types suffice here; exception messages may contain personal paths.
                Console.WriteLine("FAIL runtime " + error.GetType().Name);
            }
            finally { handle.Exit(_failures == 0 ? 0 : 1); }
        }
    }

    private static void Check(string name, bool success)
    {
        Console.WriteLine((success ? "PASS " : "FAIL ") + name);
        if (!success) Interlocked.Increment(ref _failures);
    }

    [LibraryImport("/System/Library/Frameworks/Foundation.framework/Foundation", EntryPoint = "NSHomeDirectory")]
    private static partial nint HomeDirectory();
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint Utf8String(nint receiver, nint selector);
}
