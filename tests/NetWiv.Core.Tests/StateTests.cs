// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0
// SPDX-License-Identifier: MIT

using NetWiv;

// Behavioral ports of tauri/src/state.rs at the pinned runtime commit.
internal static class StateTests
{
    public static Task GetPanics()
    {
        using var app = NewApp();
        Assert.Throws<InvalidOperationException>(() => app.State<string>());
        Assert.True(app.TryState<string>() is null, "Missing state must be absent.");
        return Task.CompletedTask;
    }

    public static Task SimpleSetGet()
    {
        using var app = NewApp();
        Assert.True(app.Manage(1u), "First registration failed.");
        Assert.Equal(1u, app.Handle.State<uint>().Inner());
        return Task.CompletedTask;
    }

    public static Task SimpleSetGetUnmanage()
    {
        using var app = NewApp();
        Assert.True(app.Handle.Manage(1u), "First registration failed.");
        Assert.True(app.Unmanage<uint>(out var value), "Removal failed.");
        Assert.Equal(1u, value);
        Assert.True(!app.Handle.Unmanage<uint>(out _), "State was removed twice.");
        Assert.True(app.Handle.TryState<uint>() is null, "Removed state was retained.");
        Assert.True(app.Manage(2u), "Re-registration failed.");
        Assert.Equal(2u, app.State<uint>().Value);
        return Task.CompletedTask;
    }

    public static Task DstSetGet()
    {
        using var app = NewApp();
        Assert.True(app.Manage(new uint[] { 1, 2, 3, 4 }), "Array registration failed.");
        Assert.True(app.State<uint[]>().Value.SequenceEqual(new uint[] { 1, 2, 3, 4 }), "Array contents changed.");
        return Task.CompletedTask;
    }

    public static async Task SetGetRemote()
    {
        using var app = NewApp();
        await Task.Run(() => Assert.True(app.Handle.Manage(10u), "Registration on another thread failed."));
        Assert.Equal(10u, app.State<uint>().Value);
    }

    public static Task TwoPutGet()
    {
        using var app = NewApp();
        Assert.True(app.Manage("Hello!"), "First registration failed.");
        var state = app.State<string>();
        Assert.True(!app.Handle.Manage("Goodbye!"), "Duplicate registration replaced state.");
        Assert.Equal("Hello!", app.State<string>().Value);
        Assert.Equal("Hello!", state.Value);
        Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(new FakeRuntime()).Manage(1).Manage(2).Build());
        return Task.CompletedTask;
    }

    public static Task ManyPutsOnlyOneSucceeds()
    {
        using var app = NewApp();
        var successful = 0;
        Parallel.For(0, 1000, _ => { if (app.Handle.Manage(10u)) Interlocked.Increment(ref successful); });
        Assert.Equal(1, successful);
        Assert.Equal(10u, app.State<uint>().Value);
        return Task.CompletedTask;
    }

    public static Task NoDropOnSet()
    {
        var first = new FirstState();
        using var rejected = new FirstState();
        using var app = NewApp();
        Assert.True(app.Manage(first), "First registration failed.");
        Assert.True(!app.Manage(rejected), "Duplicate registration succeeded.");
        Assert.Equal(0, first.DisposeCount);
        // C# does not consume method arguments: rejected objects remain caller-owned.
        Assert.Equal(0, rejected.DisposeCount);
        return Task.CompletedTask;
    }

    public static Task DropInnersOnDrop()
    {
        var first = new FirstState();
        var second = new SecondState();
        var app = new Builder().UseRuntime(new FakeRuntime()).Manage(first).Manage(second).Build();
        Assert.Equal(0, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);
        app.Dispose();
        app.Dispose();
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => app.State<FirstState>());
        Assert.Throws<ObjectDisposedException>(() => app.Manage(1));
        return Task.CompletedTask;
    }

    public static Task OwnershipAndDisposalErrors()
    {
        var detached = new FirstState();
        var alias = new FirstState();
        var throwing = new ThrowingState();
        var app = NewApp();
        app.Manage(detached);
        Assert.True(app.Unmanage<FirstState>(out var removed) && ReferenceEquals(detached, removed), "Removal did not transfer the original value.");
        app.Manage(alias);
        app.Manage<IDisposable>(alias);
        app.Manage(throwing);
        Assert.Throws<AggregateException>(app.Dispose);
        Assert.Equal(0, detached.DisposeCount);
        Assert.Equal(1, alias.DisposeCount);
        Assert.Equal(1, throwing.DisposeCount);
        app.Dispose();
        detached.Dispose();
        return Task.CompletedTask;
    }

    public static Task FailedBuildReleasesState()
    {
        var first = new FirstState();
        var rejected = new FirstState();
        Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(new FakeRuntime()).Manage(first).Manage(rejected).Build());
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(0, rejected.DisposeCount);
        rejected.Dispose();
        var plugin = new FailingPlugin();
        var state = new FirstState();
        Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(new FakeRuntime()).Manage(state).Plugin(plugin).Build());
        Assert.Equal(1, state.DisposeCount);
        Assert.Equal(1, plugin.DisposeCount);
        return Task.CompletedTask;
    }

    private static App NewApp() => new Builder().UseRuntime(new FakeRuntime()).Build();
    private sealed class FirstState : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
    private sealed class SecondState : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
    private sealed class ThrowingState : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() { DisposeCount++; throw new InvalidOperationException("Expected state disposal failure."); }
    }
    private sealed class FailingPlugin : IPlugin
    {
        public string Name => "failing";
        public int DisposeCount { get; private set; }
        public void Initialize(PluginContext context) => throw new InvalidOperationException("Expected initialization failure.");
        public void Dispose() => DisposeCount++;
    }
}
