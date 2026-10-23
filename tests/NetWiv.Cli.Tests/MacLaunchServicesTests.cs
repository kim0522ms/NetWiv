using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NetWiv.Build.Execution;
using NetWiv.Cli;

internal static class MacLaunchServicesTests
{
    private const string Product = "LaunchServices Fixture";
    private const string ReceiverProject = "tests/NetWiv.LaunchServices.Smoke/NetWiv.LaunchServices.Smoke.csproj";
    private const string LaunchServicesRegister = "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister";
    private const string HandlerScript = "import CoreServices; import Foundation; import CoreFoundation; let scheme = CommandLine.arguments.last!; if let value = LSCopyDefaultHandlerForURLScheme(scheme as CFString)?.takeRetainedValue() { print(value as String) }";
    private const string RunningApplicationScript = "import AppKit; import Foundation; for app in NSRunningApplication.runningApplications(withBundleIdentifier: CommandLine.arguments.last!) { print(app.processIdentifier) }";

    internal static async Task Run()
    {
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            Console.WriteLine("SKIP LaunchServices packaging fixture: macOS arm64 only");
            return;
        }

        var repository = FindRepositoryRoot();
        var receiver = Path.Combine(repository, ReceiverProject.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(receiver)) throw new FileNotFoundException("The LaunchServices receiver project is missing.", receiver);

        await RunMode(repository, receiver, aot: false);
        await RunMode(repository, receiver, aot: true);
    }

    private static async Task RunMode(string repository, string receiver, bool aot)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var identifier = "dev.netwiv.urltest." + nonce;
        var scheme = "netwiv-urltest-" + nonce;
        // Use a discoverable non-temporary app location for LaunchServices.
        var applications = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"));
        RequireNoLinks(applications);
        var root = Path.Combine(applications, "netwiv-urltest-" + (aot ? "aot-" : "managed-") + nonce);
        if (Path.Exists(root)) throw new InvalidOperationException("The fixture output directory already exists.");
        Directory.CreateDirectory(root);
        var app = Path.Combine(root, "target", BuildTarget.Resolve(null).RequestedTarget, "release", "bundle", "macos", "LaunchServices-Fixture.app");
        var pipe = new LaunchServicesPipe(identifier);
        int? receiverPid = null;
        Exception? failure = null;
        var registered = false;

        try
        {
            var existingHandler = await DefaultHandler(scheme, root);
            if (existingHandler is not null)
                throw new InvalidOperationException("The generated URL scheme already has a LaunchServices handler.");

            WriteConfig(root, receiver, identifier, scheme);
            var build = new List<string> { "build", "--no-bundle" };
            if (aot) build.Add("--aot");
            await RunCli(build, root);
            // Applications can discover the bundle before explicit registration, even if bundling fails.
            registered = true;
            await RunCli(["bundle", "-b", "app", "--no-sign"], root);

            VerifyBundle(app, identifier, scheme);
            await RequireSuccess("/usr/bin/codesign", ["--verify", "--deep", "--strict", app], root);
            await RequireSuccess(LaunchServicesRegister, ["-f", app], root);
            var handler = await DefaultHandler(scheme, root);
            if (!string.Equals(handler, identifier, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("LaunchServices did not resolve the fixture scheme to its app bundle.");

            pipe.Start();
            await Open(scheme, "startup", root);
            await pipe.Connect();

            var ready = await pipe.ReadEvent("ready");
            var instance = RequiredString(ready, "instance");
            var pid = RequiredInt32(ready, "pid");
            Check(pid > 0, "receiver reported an invalid process id");
            receiverPid = pid;

            var startup = await pipe.ReadEvent("startup");
            CheckInstance(startup, instance);
            CheckUrls(startup, [Url(scheme, "startup")]);
            CheckInt32(startup, "nativeWindowCount", 1);

            await Open(scheme, "warm-one", root);
            await Open(scheme, "warm-two", root);
            var warm = await pipe.ReadEvent("warm");
            CheckInstance(warm, instance);
            CheckUrls(warm, [Url(scheme, "warm-one"), Url(scheme, "warm-two")]);
            CheckInt32(warm, "nativeWindowCount", 1);

            await pipe.SendCommand("close");
            var residentReady = await pipe.ReadEvent("resident-ready");
            CheckInstance(residentReady, instance);
            CheckInt32(residentReady, "windowCount", 0);

            await Open(scheme, "resident-one", root);
            await Open(scheme, "resident-two", root);
            var resident = await pipe.ReadEvent("resident");
            CheckInstance(resident, instance);
            CheckUrls(resident, [Url(scheme, "resident-one"), Url(scheme, "resident-two")]);
            CheckInt32(resident, "nativeWindowCount", 0);
            CheckInt32(resident, "recreatedWindowCount", 1);

            await pipe.SendCommand("quit");
            var quit = await pipe.ReadEvent("quit");
            CheckInstance(quit, instance);
            var complete = await pipe.ReadEvent("complete");
            CheckInstance(complete, instance);
            await WaitForExit(receiverPid.Value, app, TimeSpan.FromSeconds(15));
        }
        catch (Exception error)
        {
            failure = error;
        }

        var cleanupFailure = await Cleanup(pipe, receiverPid, app, root, scheme, identifier, registered, requestQuit: failure is not null);
        if (cleanupFailure is not null)
        {
            var message = "LaunchServices fixture cleanup incomplete: " + cleanupFailure.Message;
            if (failure is not null) throw new AggregateException(message, failure, cleanupFailure);
            throw new InvalidOperationException(message, cleanupFailure);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine($"PASS LaunchServices {(aot ? "NativeAOT" : "managed")}: cold/warm/resident URLs, shutdown completion and owned cleanup");
    }

    private static void WriteConfig(string root, string receiver, string identifier, string scheme)
    {
        var relativeProject = Path.GetRelativePath(root, receiver).Replace(Path.DirectorySeparatorChar, '/');
        var config = new JsonObject
        {
            ["identifier"] = identifier,
            ["productName"] = Product,
            ["version"] = "1.0.0",
            ["build"] = new JsonObject { ["dotnetProject"] = relativeProject },
            ["plugins"] = new JsonObject
            {
                ["deep-link"] = new JsonObject { ["desktop"] = new JsonObject { ["schemes"] = new JsonArray(scheme) } }
            },
            ["app"] = new JsonObject { ["windows"] = new JsonArray() }
        };
        File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), config.ToJsonString());
    }

    private static async Task RunCli(IReadOnlyList<string> args, string root)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var error = new StringWriter();
        var result = await CliApp.RunAsync(args.ToArray(), TextWriter.Null, error, root, timeout.Token);
        if (result != 0) throw new InvalidOperationException($"netwiv {string.Join(" ", args)} failed with exit code {result}: {error}");
    }

    private static void VerifyBundle(string app, string identifier, string scheme)
    {
        var plistPath = Path.Combine(app, "Contents", "Info.plist");
        var root = XDocument.Load(plistPath).Root!;
        var dictionary = root.Element("dict")!;
        Check(Entry(dictionary, "CFBundleIdentifier") == identifier, "bundle identifier does not match the fixture");
        var types = MacPropertyList.Get(dictionary, "CFBundleURLTypes") ?? throw new Exception("CFBundleURLTypes is missing.");
        var entries = types.Elements("dict").ToArray();
        Check(entries.Length == 1, "CFBundleURLTypes must contain exactly one declaration");
        Check(Entry(entries[0], "CFBundleURLName") == identifier, "URL type name does not match the bundle identifier");
        Check(Entry(entries[0], "CFBundleTypeRole") == "Editor", "URL type role must be Editor");
        var schemes = MacPropertyList.Get(entries[0], "CFBundleURLSchemes")?.Elements("string").Select(value => value.Value).ToArray();
        Check(schemes is [var actual] && actual == scheme, "the declared URL scheme does not exactly match the fixture");
    }

    private static string? Entry(XElement dictionary, string key) => MacPropertyList.Get(dictionary, key)?.Value;

    private static async Task<string?> DefaultHandler(string scheme, string root)
    {
        var result = await RunProcess("/usr/bin/swift", ["-e", HandlerScript, scheme], root);
        var handler = result.StandardOutput.Trim();
        return handler.Length == 0 ? null : handler;
    }

    private static async Task Open(string scheme, string name, string root) =>
        await RequireSuccess("/usr/bin/open", [Url(scheme, name)], root);

    private static string Url(string scheme, string path) => scheme + "://activation/" + path;

    private static async Task RequireSuccess(string command, IReadOnlyList<string> args, string root)
    {
        var result = await RunProcess(command, args, root);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(command)} failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
    }

    private static async Task<ProcessResult> RunProcess(string command, IReadOnlyList<string> args, string root)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await ProcessRunner.RunAsync(new ProcessSpec(command, args, root, CaptureOutput: true), timeout.Token);
        if (result.ExitCode != 0 && command == "/usr/bin/swift")
            throw new InvalidOperationException("LaunchServices helper failed: " + result.StandardError.Trim());
        return result;
    }

    private static async Task<Exception?> Cleanup(LaunchServicesPipe pipe, int? receiverPid, string app, string root,
        string scheme, string identifier, bool registered, bool requestQuit)
    {
        Exception? failure = null;
        if (requestQuit)
        {
            try { await pipe.SendCommand("quit", TimeSpan.FromSeconds(2)); }
            catch { }
        }

        if (receiverPid is null && registered)
        {
            try
            {
                // Startup can fail before the pipe reports its PID. Query only this unique bundle identifier.
                var result = await RunProcess("/usr/bin/swift", ["-e", RunningApplicationScript, identifier], root);
                var pids = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                if (pids.Length > 1) throw new InvalidOperationException("The fixture bundle has multiple running instances.");
                if (pids.Length == 1)
                    receiverPid = int.TryParse(pids[0], out var value) && value > 0 ? value : throw new InvalidOperationException("The fixture PID lookup was invalid.");
            }
            catch (Exception error) { failure = error; }
        }

        if (receiverPid is int pid)
        {
            try { await WaitForExit(pid, app, TimeSpan.FromSeconds(8)); }
            catch
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    var executable = process.MainModule?.FileName;
                    if (executable is null)
                        throw new InvalidOperationException("The receiver PID no longer resolves to the fixture app.");
                    if (IsInsideApp(executable, app))
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
                    }
                    else if (!process.HasExited)
                    {
                        throw new InvalidOperationException("The receiver PID is live but no longer resolves to the fixture app.");
                    }
                }
                catch (ArgumentException) { }
                catch (Exception error) { failure = error; }
            }
        }

        if (registered)
        {
            try
            {
                await RequireSuccess(LaunchServicesRegister, ["-u", app], root);
                var handler = await DefaultHandler(scheme, root);
                if (string.Equals(handler, identifier, StringComparison.OrdinalIgnoreCase))
                    failure ??= new InvalidOperationException("the fixture scheme still resolves to its app after unregistering");
            }
            catch (Exception error) { failure ??= error; }
        }

        try { await pipe.DisposeAsync(); }
        catch (Exception error) { failure ??= error; }

        if (failure is null)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    RequireNoLinks(root);
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception error) { failure = error; }
        }
        return failure;
    }

    private static void RequireNoLinks(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The fixture output path must have existing directories without symbolic links.");
    }

    private static async Task WaitForExit(int pid, string app, TimeSpan timeout)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (process)
        {
            if (process.HasExited) return;
            if (!IsInsideApp(process.MainModule?.FileName, app))
                throw new InvalidOperationException("The receiver PID does not resolve to the fixture app.");
            await process.WaitForExitAsync().WaitAsync(timeout);
        }
    }

    private static bool IsInsideApp(string? executable, string app)
    {
        if (executable is null) return false;
        var appRoot = Path.GetFullPath(app).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var actual = Path.GetFullPath(executable);
        return actual.StartsWith(appRoot, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "NetWiv.Cli", "NetWiv.Cli.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the NetWiv repository root.");
    }

    private static void CheckInstance(JsonObject message, string instance) =>
        Check(RequiredString(message, "instance") == instance, "receiver instance changed across LaunchServices activations");

    private static void CheckUrls(JsonObject message, IReadOnlyList<string> expected)
    {
        var urls = message["urls"] as JsonArray ?? throw new InvalidOperationException("receiver event has no URL array");
        Check(urls.Select(node => node?.GetValue<string>()).SequenceEqual(expected), "receiver URL aggregate differs from the exact activation sequence");
    }

    private static void CheckInt32(JsonObject message, string name, int expected) =>
        Check(RequiredInt32(message, name) == expected, $"receiver {name} did not match the expected value");

    private static string RequiredString(JsonObject message, string name) =>
        message[name]?.GetValue<string>() ?? throw new InvalidOperationException($"receiver event is missing {name}");

    private static int RequiredInt32(JsonObject message, string name) =>
        message[name]?.GetValue<int>() ?? throw new InvalidOperationException($"receiver event is missing {name}");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("LaunchServices assertion failed: " + message);
    }

    private sealed class LaunchServicesPipe(string identifier) : IAsyncDisposable
    {
        private const int TimeoutMilliseconds = 30000;
        private readonly NamedPipeServerStream _server = new(
            "nwls-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)))[..16].ToLowerInvariant(),
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        private Task? _connection;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        internal void Start() => _connection = _server.WaitForConnectionAsync();

        internal async Task Connect()
        {
            if (_connection is null) throw new InvalidOperationException("The LaunchServices pipe was not started.");
            await _connection.WaitAsync(TimeSpan.FromMilliseconds(TimeoutMilliseconds));
            _reader = new StreamReader(_server, new UTF8Encoding(false), leaveOpen: true);
            _writer = new StreamWriter(_server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        }

        internal async Task<JsonObject> ReadEvent(string expectedPhase, TimeSpan? timeout = null)
        {
            if (_reader is null) throw new InvalidOperationException("The LaunchServices pipe is not connected.");
            using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromMilliseconds(TimeoutMilliseconds));
            var line = await _reader.ReadLineAsync(cancellation.Token);
            if (line is null) throw new EndOfStreamException("The LaunchServices receiver closed its control pipe.");
            var message = JsonNode.Parse(line)?.AsObject() ?? throw new InvalidOperationException("The receiver sent invalid JSON.");
            var phase = RequiredString(message, "phase");
            if (phase == "error") throw new InvalidOperationException("Receiver assertion failed: " + RequiredString(message, "assertion"));
            if (phase != expectedPhase) throw new InvalidOperationException($"Expected receiver phase {expectedPhase}, got {phase}.");
            return message;
        }

        internal async Task SendCommand(string command, TimeSpan? timeout = null)
        {
            if (_writer is null) return;
            using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromMilliseconds(TimeoutMilliseconds));
            await _writer.WriteLineAsync(new JsonObject { ["command"] = command }.ToJsonString().AsMemory(), cancellation.Token);
            await _writer.FlushAsync(cancellation.Token);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _reader?.Dispose();
                _writer?.Dispose();
            }
            catch (IOException) { } // The receiver can already have closed the pipe during cleanup.
            finally { _server.Dispose(); }
            if (_connection is not null)
            {
                try { await _connection; }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }
    }
}
