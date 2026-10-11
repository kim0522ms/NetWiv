using System.Diagnostics;
using System.Net.Sockets;
using NetWiv.Cli;

internal static class FrontendDevTests
{
    // Uses the fresh NuGet-only React consumer produced by frontend-bindings/run.mjs.
    // CLI cancellation exercises the same token path used by Ctrl+C in Program.Main.
    internal static async Task Run(string consumer)
    {
        consumer = Path.GetFullPath(consumer);
        if (await PortOpen()) throw new InvalidOperationException("Port 5173 is already occupied; no existing server will be stopped.");
        var sourceFile = Path.Combine(consumer, "src-netwiv", "Program.cs");
        var reactFile = Path.Combine(consumer, "src", "App.jsx");
        var source = await File.ReadAllTextAsync(sourceFile);
        var react = await File.ReadAllTextAsync(reactFile);
        var record = Path.Combine(consumer, "dev-restart-" + Guid.NewGuid().ToString("N") + ".log");
        var previous = Environment.GetEnvironmentVariable("NETWIV_DEV_PROBE_FILE");
        Environment.SetEnvironmentVariable("NETWIV_DEV_PROBE_FILE", record);
        var modified = source.Replace(".InvokeHandler(\"greet\")", ".InvokeHandler(\"greet\").InvokeHandler(\"dev_version\").InvokeHandler(\"dev_probe\")", StringComparison.Ordinal) + """

            public static class DevProbeCommands
            {
                [Command("dev_version")]
                public static string Version() => "before|" + Environment.ProcessId;
                [Command("dev_probe")]
                public static void Probe(string value)
                    => File.AppendAllText(Environment.GetEnvironmentVariable("NETWIV_DEV_PROBE_FILE")!, value + "\n");
            }
            """;
        var probe = """

            let lastDevProbe;
            const devProbeTimer = setInterval(async () => {
              if (!isTauri()) return;
              try {
                const value = await (commands.dev_version_v2 ?? commands.dev_version)();
                if (value !== lastDevProbe) { await commands.dev_probe({ value }); lastDevProbe = value; }
              } catch { /* A document or command can disappear during the restart under test. */ }
            }, 250);
            if (import.meta.hot) import.meta.hot.dispose(() => clearInterval(devProbeTimer));
            """;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        Task<int>? session = null;
        var pids = new List<int>();
        try
        {
            await File.WriteAllTextAsync(sourceFile, modified);
            await File.WriteAllTextAsync(reactFile, react + probe);
            session = CliApp.RunAsync(["dev", "--", "--dev"], Console.Out, Console.Error, consumer, stop.Token);
            pids.Add(await WaitForProbe("before"));
            await File.WriteAllTextAsync(sourceFile, modified.Replace("\"dev_version\"", "\"dev_version_v2\"", StringComparison.Ordinal)
                .Replace("\"before|\"", "\"after|\"", StringComparison.Ordinal));
            pids.Add(await WaitForProbe("after"));
            if (pids[0] == pids[1]) throw new Exception("C# changed without restarting the native app.");
            await WaitStopped(pids[0]);
            foreach (var file in new[] { "commands.js", "commands.d.ts" })
            {
                var bindings = await File.ReadAllTextAsync(Path.Combine(consumer, "src", "generated", file));
                if (!bindings.Contains("dev_version_v2", StringComparison.Ordinal) || bindings.Contains("\"dev_version\"", StringComparison.Ordinal))
                    throw new Exception("Generated " + file + " retained the old command.");
            }
            stop.Cancel();
            if (await session != 130) throw new Exception("CLI cancellation did not report exit code 130.");
            foreach (var pid in pids) await WaitStopped(pid);
            for (var attempt = 0; attempt < 40 && await PortOpen(); attempt++) await Task.Delay(100);
            if (await PortOpen()) throw new Exception("The CLI left its Vite server running after cancellation.");
            Console.WriteLine("PASS React dev: native IPC before/after C# rename, regenerated JS/TS, process restart, app and Vite cleanup");
            await File.AppendAllTextAsync(record, "PASS\n");
        }
        finally
        {
            stop.Cancel();
            if (session is not null) await session;
            await File.WriteAllTextAsync(sourceFile, source);
            await File.WriteAllTextAsync(reactFile, react);
            Environment.SetEnvironmentVariable("NETWIV_DEV_PROBE_FILE", previous);
            Console.WriteLine("Development evidence: " + record);
        }

        async Task<int> WaitForProbe(string phase)
        {
            while (!stop.IsCancellationRequested)
            {
                if (File.Exists(record))
                {
                    var line = (await File.ReadAllLinesAsync(record)).FirstOrDefault(line => line.StartsWith(phase + "|", StringComparison.Ordinal));
                    if (line is not null) return int.Parse(line.Split('|')[1]);
                }
                if (session!.IsCompleted) throw new Exception("CLI ended before the " + phase + " IPC result: " + await session);
                await Task.Delay(100, stop.Token);
            }
            throw new TimeoutException("Native WebView did not report " + phase + " before the deadline.");
        }
    }

    private static async Task WaitStopped(int pid)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try { using var process = Process.GetProcessById(pid); if (process.HasExited) return; }
            catch (ArgumentException) { return; }
            await Task.Delay(100);
        }
        throw new Exception("Application PID " + pid + " survived restart/cancellation.");
    }

    private static Task<bool> PortOpen()
    {
        // WSL can blackhole connections to unused forwarded Windows ports. Binding
        // checks actual listener ownership without waiting on a TCP retry timeout.
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 5173);
        if (OperatingSystem.IsWindows()) listener.ExclusiveAddressUse = true;
        else listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        try { listener.Start(); return Task.FromResult(false); }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        { return Task.FromResult(true); }
    }
}
