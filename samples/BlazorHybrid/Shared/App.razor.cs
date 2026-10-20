using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using NetWiv.Generated;

namespace NetWiv.BlazorHybrid.Shared;

public partial class App : IAsyncDisposable
{
    [Inject] private NetWivCommands Commands { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private UiSession Session { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private UiInteropCallbacks Callbacks { get; set; } = null!;
    [Inject] private IUiInteropReferenceFactory ReferenceFactory { get; set; } = null!;

    private IJSObjectReference? _module;
    private string ExecutionMode = "Starting Blazor…";
    private string Name = "Blazor";
    private string GreetingText = "No command called yet.";
    private int BackendCount;
    private string InteropText = "No JavaScript call yet.";
    private string Error = "";
    private bool Busy;
    private IUiInteropReference? _reference;
    private readonly NameForm FormModel = new();
    private EditContext FormContext = null!;
    private ValidationMessageStore FormMessages = null!;
    private string FormResult = "No form submitted yet.";
    private string CurrentRoute = "";
    private string DtoResult = "No DTO call yet.";

    protected override void OnInitialized()
    {
        FormContext = new(FormModel);
        FormMessages = new(FormContext);
        CurrentRoute = Navigation.ToBaseRelativePath(Navigation.Uri);
        Navigation.LocationChanged += LocationChanged;
    }

    private void LocationChanged(object? sender, LocationChangedEventArgs args)
    {
        CurrentRoute = Navigation.ToBaseRelativePath(args.Location);
        _ = InvokeAsync(StateHasChanged);
    }

    private void Navigate(string route) => Navigation.NavigateTo(route);

    private void SubmitForm()
    {
        FormMessages.Clear();
        if (string.IsNullOrWhiteSpace(FormModel.Name))
        {
            FormMessages.Add(new FieldIdentifier(FormModel, nameof(NameForm.Name)), "Name is required.");
            FormResult = "Invalid";
        }
        else FormResult = "Valid: " + FormModel.Name;
        FormContext.NotifyValidationStateChanged();
    }

    private sealed class NameForm
    {
        public string Name { get; set; } = "";
    }

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

    private async Task RoundtripAsync()
    {
        _reference ??= ReferenceFactory.Create(Callbacks);
        var reply = await _module!.InvokeAsync<UiInteropReply>("roundtrip", _reference.Value,
            new UiInteropRequest
            {
                Name = Name,
                Items = [new() { Text = "large integer", Value = long.MaxValue }, null],
            });
        DtoResult = reply.Message + " / " + reply.Items[0]!.Value + " / null=" +
            (reply.Items[1] is null && reply.Optional is null);
    }

    private Task ReloadAsync() => _module!.InvokeVoidAsync("reload").AsTask();
    private Task CloseAsync() => _module!.InvokeVoidAsync("closeWindow").AsTask();

    public async ValueTask DisposeAsync()
    {
        Navigation.LocationChanged -= LocationChanged;
        _reference?.Dispose();
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
