using System.Runtime.InteropServices;
using NetWiv;
using NetWiv.Platform.Desktop;

internal static class MacOsEffectsProbe
{
    // The pinned window-vibrancy subclass tags its views for effect removal.
    private const nint EffectTag = 91376254;

    internal static int Run()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This probe requires macOS.");
        var result = 1;
        var runtime = new DesktopRuntime();
        var builder = new Builder().UseRuntime(runtime).UseEmbeddedAssets()
            .Window("main", "NetWiv effect probe host", "quiet.html")
            .Setup(app => { Program.VerifyBackend(runtime); _ = VerifyAndExitAsync(app.Handle); });
        using var app = builder.Build();
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
        app.Run();
        return result;

        async Task VerifyAndExitAsync(AppHandle handle)
        {
            try { await VerifyAsync(handle); result = 0; }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally { handle.Exit(result); }
        }
    }

    private static async Task VerifyAsync(AppHandle app)
    {
        for (int iteration = 0; iteration < 12; iteration++)
        {
            string title = $"NetWiv effect lifecycle probe {iteration}";
            Window window = iteration % 2 == 0
                ? await new WindowBuilder(app, "effect-lifecycle").Title(title)
                    .WindowEffects(new Effects([Effect.Sidebar], EffectState.Active, 4)).BuildAsync()
                : await new WebviewWindowBuilder(app, "effect-lifecycle").Title(title).Url("quiet.html")
                    .WindowEffects(new Effects([Effect.Sidebar], EffectState.Active, 4)).BuildAsync();
            try
            {
                await AssertEffectAsync(app, title, 1, material: 7, state: 1);
                var view = await new WebviewBuilder(app, window, "effect-child").Url("quiet.html").BuildAsync();
                await AssertEffectAsync(app, title, 1, material: 7, state: 1);
                for (int change = 0; change < 4; change++)
                {
                    await window.SetEffectsAsync(new Effects([Effect.HeaderView], EffectState.Inactive, 8));
                    await AssertEffectAsync(app, title, 1, material: 10, state: 2);
                    await window.SetEffectsAsync(new Effects([Effect.Sidebar], EffectState.FollowsWindowActiveState, 0));
                    await AssertEffectAsync(app, title, 1, material: 7, state: 0);
                }
                await window.SetEffectsAsync(new Effects([]));
                await window.SetEffectsAsync(new Effects([Effect.Mica]));
                await AssertEffectAsync(app, title, 1, material: 7, state: 0);
                await window.ClearEffectsAsync();
                await AssertEffectAsync(app, title, 0);
                await window.ClearEffectsAsync();
                await AssertEffectAsync(app, title, 0);
                await window.SetEffectsAsync(new Effects([Effect.Sidebar], EffectState.Active, 4));
                await AssertEffectAsync(app, title, 1, material: 7, state: 1);
                if (iteration % 2 == 0) await view.CloseAsync();
                // Close/destroy while an effect is still attached, then reuse both labels.
                if (iteration % 3 == 0) await window.CloseAsync();
                else await window.DestroyAsync();
                if (await app.GetWindowAsync(window.Label) is not null || await app.GetWebviewAsync(view.Label) is not null ||
                    await app.GetWebviewAsync("effect-lifecycle") is not null)
                    throw new InvalidOperationException("Effect window or child survived close/destroy.");
            }
            finally
            {
                if (await app.GetWindowAsync(window.Label) is not null) await window.DestroyAsync();
            }
        }
        Console.WriteLine("PASS macOS creation effects, repeated replacement, native material/state, clear, close/destroy and label reuse");
    }

    private static async Task AssertEffectAsync(AppHandle app, string expectedTitle, int count, nint? material = null, nint? state = null)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Dispatch(() =>
        {
            try
            {
                // Read only this process's test window on the AppKit thread. No
                // native object pointer survives the dispatch or window closure.
                var application = Send(GetClass("NSApplication"), Selector("sharedApplication"));
                var windows = Send(application, Selector("windows"));
                nint content = 0;
                for (nuint i = 0; i < (nuint)Send(windows, Selector("count")); i++)
                {
                    var window = SendIndex(windows, Selector("objectAtIndex:"), i);
                    var title = Send(Send(window, Selector("title")), Selector("UTF8String"));
                    if (Marshal.PtrToStringUTF8(title) == expectedTitle) content = Send(window, Selector("contentView"));
                }
                if (content == 0) throw new InvalidOperationException("Effect probe native content view is missing.");
                var children = Send(content, Selector("subviews"));
                int found = 0;
                nint effect = 0;
                for (nuint i = 0; i < (nuint)Send(children, Selector("count")); i++)
                {
                    var child = SendIndex(children, Selector("objectAtIndex:"), i);
                    if (Send(child, Selector("tag")) != EffectTag) continue;
                    found++;
                    effect = child;
                }
                if (found != count) throw new InvalidOperationException($"Expected {count} native effect views, found {found}.");
                if (material is { } expectedMaterial && Send(effect, Selector("material")) != expectedMaterial)
                    throw new InvalidOperationException("Native effect material differs from the last setter.");
                if (state is { } expectedState && Send(effect, Selector("state")) != expectedState)
                    throw new InvalidOperationException("Native effect state differs from the last setter.");
                complete.TrySetResult();
            }
            catch (Exception error) { complete.TrySetException(error); }
        });
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
    private static extern nint GetClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
    private static extern nint Selector(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendIndex(nint receiver, nint selector, nuint index);
}
