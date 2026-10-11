// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0
// SPDX-License-Identifier: MIT
using NetWiv;
using NetWiv.Runtime;

// Ports the three 10,000-case properties and recursive-once regression in
// tauri 2.11.5 event/listener.rs. Fixed seeds make failures reproducible.
internal static class UpstreamEventTests
{
    public static Task ListenersCheckKey() => Property(101, (bus, key, _) =>
    {
        using var subscription = bus.Listen(key, _ => { });
        Assert.True(bus.HasListener(key, _ => true), "Registered event key is missing.");
    });

    public static Task ListenersCheckFn() => Property(102, (bus, key, _) =>
    {
        var calls = 0;
        using var subscription = bus.Listen(key, _ => calls++);
        bus.Publish(key, RuntimeEventTarget.Any, JsonValue.Null, _ => true);
        Assert.Equal(1, calls);
    });

    public static Task CheckOnEvent() => Property(103, (bus, key, data) =>
    {
        string? received = null;
        using var subscription = bus.Listen(key, value => received = value.Payload.GetString());
        bus.Publish(key, RuntimeEventTarget.Any, JsonValue.From(data), _ => true);
        Assert.Equal(data, received);
        Assert.True(bus.HasListener(key, _ => true), "Emitting removed a regular listener.");
    });

    public static async Task EventNoDeadlocks()
    {
        using var bus = new EventBus();
        var calls = 0;
        using var subscription = bus.Once("test-event", _ =>
        {
            calls++;
            bus.Publish("test-event", RuntimeEventTarget.Any, JsonValue.Null, _ => true);
        });
        await Task.Run(() => bus.Publish("test-event", RuntimeEventTarget.Any, JsonValue.Null, _ => true))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
    }

    private static Task Property(int seed, Action<EventBus, string, string> assertion)
    {
        var random = new Random(seed);
        for (var iteration = 0; iteration < 10000; iteration++)
        {
            using var bus = new EventBus();
            string Word() => new(Enumerable.Range(0, random.Next(1, 257)).Select(_ => (char)random.Next('a', 'z' + 1)).ToArray());
            assertion(bus, Word(), Word());
        }
        return Task.CompletedTask;
    }
}
