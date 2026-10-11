using System.Text.Json;

namespace NetWiv;

public abstract class Resource : IAsyncDisposable
{
    private bool _closed;

    protected Resource(AppHandle app, uint resourceId)
    {
        App = app;
        ResourceId = resourceId;
    }

    protected AppHandle App { get; }
    public uint ResourceId { get; }

    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        await App.InvokeAsync(
            "plugin:resources|close",
            ApiJson.Arguments(null, writer => writer.WriteNumber("rid", ResourceId)),
            cancellationToken);
        _closed = true;
    }

    public ValueTask DisposeAsync() => _closed ? ValueTask.CompletedTask : CloseAsync();
}
