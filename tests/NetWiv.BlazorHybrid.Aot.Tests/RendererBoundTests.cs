using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetWiv.BlazorHybrid.Aot;
using NetWiv.BlazorHybrid.Aot.Hosting;
using NetWiv.BlazorHybrid.Aot.Interop;

namespace NetWiv.BlazorHybrid.Aot.Tests;

#pragma warning disable BL0006 // Exercise the product renderer's bounded display queue.
internal static class RendererBoundTests
{
    internal static async Task RunAsync()
    {
        await NavigationBoundaryAsync();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new BlazorDispatcher();
        var failures = new List<Exception>();
        var batches = 0;
        var component = new BoundedComponent();
        var registry = new BlazorAotComponentRegistry().Register<BoundedComponent>(_ => component);
        using var runtime = new AotJsRuntime(_ => { }, ProofJsonContext.Default, new());
        var renderer = new AotWebViewRenderer(services, NullLoggerFactory.Instance,
            registry.CreateActivator(services), dispatcher, runtime,
            message => { if (message.StartsWith("__bwv:[\"RenderBatch\"", StringComparison.Ordinal)) batches++; }, failures.Add);
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                _ = renderer.AddRootAsync(typeof(BoundedComponent), "#app", ParameterView.Empty);
                for (var index = 0; index < 256; index++) component.Change();
            });
            ProofGate.Check(batches == 256 && failures.Count == 1 &&
                failures[0].Message.Contains("unacknowledged", StringComparison.Ordinal),
                "A stalled browser did not stop at the product render-batch bound.");
        }
        finally
        {
            await dispatcher.InvokeAsync(async () =>
            {
                renderer.CancelPendingBatches();
                await renderer.DisposeAsync();
            });
        }
        Console.WriteLine("PASS unacknowledged render batches are bounded and renderer teardown completes");
    }

    private static async Task NavigationBoundaryAsync()
    {
        var dispatcher = new BlazorDispatcher();
        var messages = new List<string>();
        var callbacks = 0;
        var navigation = new AotNavigationManager(new("http://netwiv-blazor.localhost/"),
            "http://netwiv-blazor.localhost/", dispatcher, messages.Add, error => throw error);
        await dispatcher.InvokeAsync(() =>
        {
            var rejected = false;
            try
            {
                navigation.RegisterLocationChangingHandler(_ =>
                { callbacks++; return ValueTask.CompletedTask; });
            }
            catch (NotSupportedException) { rejected = true; }
            ProofGate.Check(rejected, "Navigation lock registration was silently accepted.");
            navigation.NavigateTo("next", new NavigationOptions { ReplaceHistoryEntry = true, HistoryEntryState = "state" });
        });
        ProofGate.Check(callbacks == 0 && messages.Count == 1, "Rejected navigation locks obstructed ordinary navigation.");
        using var parsed = System.Text.Json.JsonDocument.Parse(messages[0].AsMemory("__bwv:".Length));
        var values = parsed.RootElement;
        ProofGate.Check(values[0].GetString() == "Navigate" && values[1].GetString() == "next" &&
            !values[2].GetProperty("forceLoad").GetBoolean() && values[2].GetProperty("replaceHistoryEntry").GetBoolean() &&
            values[2].GetProperty("historyEntryState").GetString() == "state", "Navigation options did not preserve the pinned wire contract.");
        Console.WriteLine("PASS unsupported navigation locks fail explicitly without blocking ordinary navigation");
    }
}

public sealed class BoundedComponent : ComponentBase
{
    private int _value;
    public void Change() { _value++; StateHasChanged(); }
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, _value);
}
