var tests = new List<(string Name, Func<Task> Run)>
{
    ("Hybrid selects the consumer's framework and rejects substituted assemblies", FrameworkTests.MatchingTarget),
    ("Hybrid origins reject authority aliases and opaque foreign schemes", OriginTests.AuthorityBoundaries),
    ("AOT assets preserve routing, reject foreign paths and retain provider ownership", AotAssetTests.Assets),
    ("AOT registration snapshots and host selection reject conflicting setup", AotAssetTests.Registration),
    ("AOT event parsing covers framework built-ins and rejects custom composition events", AotEventDataTests.BuiltInEvents),
    ("bundled assets preserve routing and reject foreign origins", ManagerTests.Assets),
    ("foreign page messages cannot attach a managed renderer", ManagerTests.ForeignOrigin),
    ("page replacement awaits asynchronous scoped service disposal", ManagerTests.Replacement),
    ("manager disposal awaits asynchronous scoped service disposal", ManagerTests.Disposal),
    ("renderer dispatch serializes callers and restores access after asynchronous waits", ManagerTests.Dispatch),
    ("inline renderer failures return tasks without interrupting disposal", JsDisposalTests.InlineDispatcherFailure),
    ("tracked JS calls are bounded and use actual nested completion identities", JsDisposalTests.TrackingBound),
};
if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
{
    tests.Add(("Hybrid assets retain configured response policy and reject isolation", HybridSecurityTests.AssetsAndIsolation));
    tests.Add(("Hybrid transport rejects foreign windows origins and stale documents", RuntimeTests.TransportBoundaries));
    tests.Add(("Hybrid native protocol preserves asset paths and queries and rejects untrusted authorities", RuntimeTests.ProtocolAssets));
    tests.Add(("Hybrid reload retires its scope and ignores late document callbacks", RuntimeTests.Reload));
    tests.Add(("Hybrid close prevents retired document resurrection", RuntimeTests.Close));
    tests.Add(("Hybrid exit and native-loop return await asynchronous renderer cleanup", RuntimeTests.ExitAndReturn));
    tests.Add(("malformed current-page messages fail through controlled runtime cleanup", RuntimeTests.MalformedMessage));
    tests.Add(("retired-page JS disposal permits shutdown and scoped service cleanup", JsDisposalTests.RetiredPageInterop));
    tests.Add(("actual JS module disposal permits shutdown and scoped service cleanup", JsDisposalTests.ModuleReference));
    tests.Add(("reload cancels pending JS and rejects new calls from retired pages", RetirementTests.PendingAndFutureJs));
    tests.Add(("standard disconnect catches resume rendering and cleanup without hiding disposal faults", RetirementTests.AfterRenderAndCleanupFailure));
    tests.Add(("caller cancellation remains safe before and during page retirement", RetirementTests.CancellationDuringRetirement));
    tests.Add(("close releases a renderer with an unacknowledged render batch", RetirementTests.PendingRender));
    tests.Add(("rapid reload cancellation preserves the latest page's JS calls", RetirementTests.RapidReload));
}
else Console.WriteLine("SKIP Hybrid runtime scenarios: unsupported host platform.");

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {error.GetType().Name}: {error.Message}");
    }
}
return failures == 0 ? 0 : 1;
