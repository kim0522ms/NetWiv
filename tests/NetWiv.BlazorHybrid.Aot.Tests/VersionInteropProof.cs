using Microsoft.JSInterop;

namespace NetWiv.BlazorHybrid.Aot.Tests;

internal static class VersionInteropProof
{
    internal static async Task RunAsync(IJSRuntime runtime)
    {
#if NET10_0_OR_GREATER
        await runtime.SetValueAsync("proof.versionValue", 41);
        ProofGate.Check(await runtime.GetValueAsync<int>("proof.versionValue", CancellationToken.None) == 41,
            "Global JS property access failed.");
        await using var value = await runtime.InvokeConstructorAsync("proof.VersionBox", [42]);
        ProofGate.Check(await value.GetValueAsync<int>("value") == 42, "Global JS construction failed.");
        await value.SetValueAsync("value", 43, CancellationToken.None);
        ProofGate.Check(await value.GetValueAsync<int>("value", CancellationToken.None) == 43, "Object JS property access failed.");
        await using var module = await runtime.InvokeAsync<IJSObjectReference>("import", ["./proof-module.js"]);
        await using var nested = await module.InvokeConstructorAsync("VersionBox", CancellationToken.None, [44]);
        ProofGate.Check(await nested.GetValueAsync<int>("value") == 44, "Module JS construction lost its object target.");
        await nested.SetValueAsync("value", 45);
        ProofGate.Check(await nested.GetValueAsync<int>("value") == 45, "Module object's JS property update failed.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await runtime.GetValueAsync<int>("proof.versionValue", canceled.Token); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Canceled JS property access executed.");
#else
        await Task.CompletedTask;
#endif
    }
}
