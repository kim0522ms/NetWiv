using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using NetWiv.BlazorHybrid.Aot;

namespace NetWiv.BlazorHybrid.Aot.Tests;

internal static class ProofGate
{
    internal const string Assembly = "NetWiv.BlazorHybrid.Aot.Tests";

    internal static void AssertManagedHost()
    {
        Check(RuntimeFeature.IsDynamicCodeSupported && JsonSerializer.IsReflectionEnabledByDefault,
            "The Microsoft managed host test requires PublishAot=false and JsonSerializerIsReflectionEnabledByDefault=true.");
        Console.WriteLine("MODE Microsoft managed host; dynamic code and JSON reflection enabled");
    }

    internal static void AssertRuntime(bool managed)
    {
        Check(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection must remain disabled.");
        if (!managed) Check(!RuntimeFeature.IsDynamicCodeSupported, "The required proof must execute a NativeAOT artifact.");
        Console.WriteLine(managed ? "MODE managed debugging only" : "PASS native execution: dynamic code disabled; JSON reflection disabled");
    }

    internal static void Configure(BlazorAotOptions options)
    {
        options.Components.Register<CounterComponent>().Register<ProbeChild>().Register<CascadingValue<string>>();
        options.JsonTypeInfoResolver = ProofJsonContext.Default;
        var registry = options.Interop;
        var json = ProofJsonContext.Default;
        registry.RegisterStatic<ProbeDto, ProbeDto>(Assembly, "StaticTask", input => new(InvokableFixture.StaticTask(input)), json.ProbeDto, json.ProbeDto);
        registry.RegisterStatic<ProbeDto, ProbeDto>(Assembly, "StaticValueTask", InvokableFixture.StaticValueTask, json.ProbeDto, json.ProbeDto);
        registry.RegisterStatic<int, int>(Assembly, "StaticNumber", InvokableFixture.StaticNumber, json.Int32, json.Int32);
        registry.RegisterInstance<InvokableReceiver, ProbeDto, ProbeDto>("InstanceTask", (target, input) => new(target.InstanceTask(input)), json.ProbeDto, json.ProbeDto);
        registry.RegisterInstance<InvokableReceiver, ProbeDto, ProbeDto>("InstanceValueTask", (target, input) => target.InstanceValueTask(input), json.ProbeDto, json.ProbeDto);
        registry.RegisterInstance<InvokableReceiver, int, int>("InstanceNumber", (target, input) => target.InstanceNumber(input), json.Int32, json.Int32);
    }

    internal static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
