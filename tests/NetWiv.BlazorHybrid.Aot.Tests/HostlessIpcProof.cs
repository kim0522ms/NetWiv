using NetWiv.Platform.Desktop;
using NetWiv.Runtime;

namespace NetWiv.BlazorHybrid.Aot.Tests;

// Opaque URLs still identify one native document; they do not share a "null" origin.
internal static class HostlessIpcProof
{
    internal static int Run()
    {
        var expected = new Dictionary<string, string> { ["blank"] = "about:blank" };
        var documents = new Dictionary<string, ulong>();
        var received = new HashSet<string>();
        string? failure = null;
        var runtime = new DesktopRuntime();
        using var watchdog = new Timer(_ => runtime.Exit(1), null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        runtime.Run(new RuntimeContext(
            expected.Select(pair => new WindowOptions(pair.Key, "NetWiv hostless IPC", pair.Value, 400, 300, JsonValue.Null)).ToArray(),
            "if(window.top===window)window.__NETWIV_IPC__.postMessage(location.href);",
            message =>
            {
                if (!expected.TryGetValue(message.WebviewLabel, out var source) || message.Source.AbsoluteUri != source ||
                    message.Json != source || message.DocumentId == 0 ||
                    !documents.TryGetValue(message.WebviewLabel, out var epoch) || epoch != message.DocumentId)
                    failure = "Hostless IPC changed the exact native source or document epoch.";
                received.Add(message.WebviewLabel);
                if (failure is not null || received.Count == expected.Count) runtime.Exit(failure is null ? 0 : 1);
            },
            _ => { },
            _ => null,
            OnDocumentChanged: change => { if (!change.Closed) documents[change.WebviewLabel] = change.DocumentId; }));
        ProofGate.Check(failure is null && received.SetEquals(expected.Keys),
            failure ?? $"Hostless IPC did not complete for every document (received {received.Count}/{expected.Count}).");
        Console.WriteLine("PASS about:blank document authenticates its exact native source and epoch");
        return 0;
    }
}
