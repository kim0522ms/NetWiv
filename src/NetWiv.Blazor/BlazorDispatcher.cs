using Microsoft.AspNetCore.Components;

namespace NetWiv.Blazor;

// Rendering has no native UI thread affinity. Reuse Blazor's serialized
// synchronization context so cleanup can continue after the native loop ends.
internal sealed class BlazorDispatcher : Dispatcher
{
    private readonly Dispatcher _dispatcher = CreateDefault();
    internal Action<Task>? OperationStarted { get; set; }

    public override bool CheckAccess() => _dispatcher.CheckAccess();
    public override Task InvokeAsync(Action workItem)
    {
        try { return Track(_dispatcher.InvokeAsync(workItem)); }
        catch (Exception error) { return Track(Task.FromException(error)); }
    }
    public override Task InvokeAsync(Func<Task> workItem)
    {
        try { return Track(_dispatcher.InvokeAsync(workItem)); }
        catch (Exception error) { return Track(Task.FromException(error)); }
    }
    public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
    {
        try { return Track(_dispatcher.InvokeAsync(workItem)); }
        catch (Exception error) { return Track(Task.FromException<TResult>(error)); }
    }
    public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
    {
        try { return Track(_dispatcher.InvokeAsync(workItem)); }
        catch (Exception error) { return Track(Task.FromException<TResult>(error)); }
    }

    private TTask Track<TTask>(TTask task) where TTask : Task
    {
        OperationStarted?.Invoke(task);
        return task;
    }
}
