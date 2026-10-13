using System.Collections.Concurrent;
using NetWiv;

internal static class MacOsDataStoreProbe
{
    private sealed record Probe(string Phase, byte[] Uuid, TaskCompletionSource Result);
    private static readonly ConcurrentDictionary<string, Probe> Probes = new();

    internal static void Register(Builder builder)
    {
        builder.Capability(new("data-store-probe", ["core:app:allow-fetch-data-store-identifiers", "core:app:allow-remove-data-store"], Windows: ["data-store-*"]));
        builder.RegisterCommand("data_store_probe_config", (context, _, _) =>
        {
            var probe = Probes[context.WebviewLabel!];
            return ValueTask.FromResult(JsonValue.Parse($$"""{"phase":"{{probe.Phase}}","uuid":[{{string.Join(',', probe.Uuid)}}]}"""));
        });
        builder.RegisterCommand("data_store_probe_result", (context, args, _) =>
        {
            var probe = Probes[context.WebviewLabel!];
            if (args.TryGetProperty("error", out var error)) probe.Result.TrySetException(new Exception(error.GetString()));
            else probe.Result.TrySetResult();
            return ValueTask.FromResult(JsonValue.Null);
        });
    }

    internal static async Task VerifyAsync(AppHandle app)
    {
        var bytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var identifier = new DataStoreIdentifier(bytes);
        if (!OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            foreach (var command in new Func<Task>[] {
                async () => { _ = await AppApi.FetchDataStoreIdentifiersAsync(app); },
                () => AppApi.RemoveDataStoreAsync(app, identifier) })
            {
                try { await command(); }
                catch (InvokeException error) when (error.Message.Contains("macOS 14")) { continue; }
                throw new Exception("Website data-store API did not reject an older macOS version.");
            }
            Console.WriteLine("PASS website data-store APIs reject macOS below 14");
            return;
        }

        try
        {
            await RunPhase("write", useIdentifier: true);
            await RunPhase("read", useIdentifier: true);
            var stores = await AppApi.FetchDataStoreIdentifiersAsync(app);
            if (!stores.Any(store => store.ToArray().SequenceEqual(bytes)))
                throw new Exception("SDK enumeration did not preserve the sixteen identifier bytes.");
            await RunPhase("remove", useIdentifier: false);
            await RunPhase("empty", useIdentifier: true);
        }
        finally
        {
            // Only this random test-owned identifier is removed, never other application stores.
            // WKWebView teardown can release its store asynchronously after Destroy.
            for (var attempt = 0; ; attempt++)
            {
                try { await AppApi.RemoveDataStoreAsync(app, identifier); break; }
                catch (InvokeException error) when (attempt < 100 && error.Message.Contains("data store is currently opened"))
                { await Task.Delay(50); }
            }
        }
        Console.WriteLine("PASS macOS website data-store guest/SDK enumeration, active-store error, persistence, removal and recreation");

        async Task RunPhase(string phase, bool useIdentifier)
        {
            var label = "data-store-" + phase;
            var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Probes[label] = new(phase, bytes, result);
            WebviewWindow? window = null;
            try
            {
                var builder = new WebviewWindowBuilder(app, label).Url("data-store.html").Visible(false);
                if (useIdentifier) builder.DataStoreIdentifier(identifier);
                window = await builder.BuildAsync();
                await result.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally
            {
                if (window is not null) await window.DestroyAsync();
                Probes.TryRemove(label, out _);
            }
        }
    }
}
