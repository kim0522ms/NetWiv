using System.Text.Json;
using NetWiv;

namespace NetWiv.Conformance;

internal static class LifetimeFixture
{
    internal static async ValueTask<JsonElement> StreamNumbers(InvokeContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        using var channel = Channel.FromJson(context, arguments.GetProperty("channel"));
        if (arguments.TryGetProperty("resumeEvent", out var resumeEvent))
        {
            var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var listener = context.App.Events.Listen(resumeEvent.GetString()!, _ => resumed.TrySetResult());
            channel.Send(JsonValue.From(0));
            await resumed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        else channel.Send(JsonValue.From(0));
        channel.Send(JsonValue.From(1));
        channel.Send(JsonValue.From(2));
        return JsonValue.Null;
    }
}
