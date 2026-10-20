using Microsoft.JSInterop;

namespace NetWiv.BlazorHybrid.Aot.Tests;

public static class InvokableFixture
{
    [JSInvokable("StaticTask")]
    public static async Task<ProbeDto> StaticTask(ProbeDto input)
    {
        await Task.Yield();
        return Change(input, "static-task:", 10);
    }

    [JSInvokable("StaticValueTask")]
    public static async ValueTask<ProbeDto> StaticValueTask(ProbeDto input)
    {
        await Task.Yield();
        return Change(input, "static-value-task:", 20);
    }

    [JSInvokable("StaticNumber")]
    public static ValueTask<int> StaticNumber(int input) => ValueTask.FromResult(checked(input * 2));

    internal static ProbeDto Change(ProbeDto input, string prefix, int increment) => new()
    {
        Name = prefix + input.Name,
        Count = checked(input.Count + increment),
        Items = input.Items,
        Optional = input.Optional,
    };
}

public sealed class InvokableReceiver(string prefix)
{
    [JSInvokable("InstanceTask")]
    public async Task<ProbeDto> InstanceTask(ProbeDto input)
    {
        await Task.Yield();
        return InvokableFixture.Change(input, prefix + "task:", 30);
    }

    [JSInvokable("InstanceValueTask")]
    public async ValueTask<ProbeDto> InstanceValueTask(ProbeDto input)
    {
        await Task.Yield();
        return InvokableFixture.Change(input, prefix + "value-task:", 40);
    }

    [JSInvokable("InstanceNumber")]
    public ValueTask<int> InstanceNumber(int input) => ValueTask.FromResult(checked(input + 1));
}
