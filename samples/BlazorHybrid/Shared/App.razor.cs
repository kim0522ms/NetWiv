using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NetWiv.Generated;

namespace NetWiv.BlazorHybrid.Shared;

public partial class App : IAsyncDisposable
{
    [Inject] private NetWivCommands Commands { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private UiSession Session { get; set; } = null!;

    private IJSObjectReference? _module;
    private string ExecutionMode = "Starting Blazor…";
    private string Name = "Blazor";
    private string GreetingText = "No command called yet.";
    private int BackendCount;
    private string InteropText = "No JavaScript call yet.";
    private string Error = "";
    private bool Busy;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _module = await JS.InvokeAsync<IJSObjectReference>("import", "./netwiv.js");
        ExecutionMode = await _module.InvokeAsync<string>("executionMode");
        StateHasChanged();
    }

    private async Task GreetAsync()
    {
        Busy = true;
        Error = "";
        var requestedName = Name;
        try
        {
            // Both hosts use this generated client and the same @netwiv/api bridge.
            var response = await Commands.GreetAsync(requestedName);
            GreetingText = response.Message;
            BackendCount = response.Count;
        }
        catch (JSException exception)
        {
            Error = exception.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ReadDocumentAsync()
    {
        var documentTitle = await _module!.InvokeAsync<string>("readDocumentTitle");
        InteropText = documentTitle;
    }

    private Task ReloadAsync() => _module!.InvokeVoidAsync("reload").AsTask();
    private Task CloseAsync() => _module!.InvokeVoidAsync("closeWindow").AsTask();

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
