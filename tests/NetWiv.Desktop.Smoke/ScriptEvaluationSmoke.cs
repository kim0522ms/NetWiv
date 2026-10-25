using System.Text.Json;
using NetWiv;
using NetWiv.Platform.Desktop;

internal static class ScriptEvaluationSmoke
{
    internal static int Run()
    {
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new DesktopRuntime();
        using var app = new Builder().UseRuntime(runtime).UseEmbeddedAssets()
            .Window("main", "NetWiv script evaluation", "quiet.html")
            .Setup(value =>
            {
                Program.VerifyBackend(runtime);
                _ = VerifyAsync(value);
            })
            .Build();
        using var watchdog = new Timer(_ =>
        {
            Console.Error.WriteLine("FAIL script evaluation smoke timed out");
            Environment.Exit(124);
        }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);

        app.Run();
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

        async Task VerifyAsync(App value)
        {
            var exitCode = 1;
            try
            {
                var handle = value.Handle;
                await WaitForDocumentReadyAsync(handle, "main");
                await VerifyValuesAndErrorsAsync(handle);
                await VerifyStrictCspAsync(handle, value.GetWebview("main")!);
                await VerifyFireAndForgetAsync(handle);
                await VerifyCallerCancellationAsync(handle);
                await VerifyReloadCancellationAsync(handle, value.GetWebview("main")!);
                await VerifyHistoryCancellationAsync(handle, value.GetWebview("main")!);
                await VerifyCloseCancellationAsync(handle, value.GetWindow("main")!);
                await VerifyShutdownCancellationAsync(handle);
                Console.WriteLine("PASS native script evaluation values, errors, cancellation and lifecycle");
                exitCode = 0;
            }
            catch (Exception error) { Console.Error.WriteLine("FAIL " + error); }
            finally
            {
                value.Handle.Exit(exitCode);
                completed.TrySetResult(exitCode);
            }
        }
    }

    private static async Task VerifyValuesAndErrorsAsync(AppHandle app)
    {
        var number = await app.EvaluateScriptAsync("main", "1 + 2");
        Require(number.ValueKind == JsonValueKind.Number && number.GetInt32() == 3, "number result");

        var undefined = await app.EvaluateScriptAsync("main", "undefined");
        Require(undefined.ValueKind == JsonValueKind.Null, "undefined maps to JSON null");

        var nullValue = await app.EvaluateScriptAsync("main", "null");
        Require(nullValue.ValueKind == JsonValueKind.Null, "null result");
        var stringValue = await app.EvaluateScriptAsync("main", "'script result'");
        Require(stringValue.GetString() == "script result", "string result");
        var boolean = await app.EvaluateScriptAsync("main", "true");
        Require(boolean.GetBoolean(), "boolean result");
        var array = await app.EvaluateScriptAsync("main", "[1, 'two', null]");
        Require(array.GetArrayLength() == 3 && array[0].GetInt32() == 1 && array[1].GetString() == "two" &&
            array[2].ValueKind == JsonValueKind.Null, "array result");

        var promise = await app.EvaluateScriptAsync("main", "Promise.resolve({ answer: 42 })");
        Require(promise.ValueKind == JsonValueKind.Object && promise.GetProperty("answer").GetInt32() == 42,
            "returned Promise is awaited and serialized");
        var delayed = await app.EvaluateScriptAsync("main", "new Promise(resolve => setTimeout(() => resolve(43), 100))");
        Require(delayed.GetInt32() == 43, "delayed Promise survives repeated native polls");

        var syncError = await ExpectJavaScriptExceptionAsync(
            () => app.EvaluateScriptAsync("main", "(() => { throw new RangeError('sync marker'); })()"),
            "RangeError", "sync marker");
        Require(!string.IsNullOrWhiteSpace(syncError.JavaScriptStackTrace), "JavaScript exception stack trace");

        await ExpectJavaScriptExceptionAsync(
            () => app.EvaluateScriptAsync("main", "Promise.reject(new TypeError('async marker'))"),
            "TypeError", "async marker");
        await ExpectJavaScriptExceptionAsync(
            () => app.EvaluateScriptAsync("main", "(() => { const value = {}; value.self = value; return value; })()"),
            "TypeError", null);
        await ExpectJavaScriptExceptionAsync(() => app.EvaluateScriptAsync("main", "1n"), "TypeError", null);
        Console.WriteLine("PASS script evaluation JSON values, Promise results, JavaScript errors and serialization errors");
    }

    private static async Task VerifyFireAndForgetAsync(AppHandle app)
    {
        app.GetWebview("main")!.EvaluateScript("window.__netwivFireAndForget = 17;");
        await WaitForBooleanAsync(app, "main", "window.__netwivFireAndForget === 17");
    }

    private static async Task VerifyStrictCspAsync(AppHandle app, Webview main)
    {
        main.EvaluateScript("location.href = 'strict-csp.html';");
        await WaitForBooleanAsync(app, "main", "document.title === 'NetWiv strict CSP' && document.readyState === 'complete'");

        var policy = await app.EvaluateScriptAsync("main",
            "document.querySelector('meta[http-equiv=\"Content-Security-Policy\"]')?.content || ''");
        Require(policy.GetString()!.Contains("script-src 'self'", StringComparison.Ordinal), "strict CSP script policy");
        var inlineBlocked = await app.EvaluateScriptAsync("main", "typeof window.__netwivStrictCspInline === 'undefined'");
        Require(inlineBlocked.GetBoolean(), "strict CSP blocked the inline page script");

        var promise = await app.EvaluateScriptAsync("main",
            "new Promise(resolve => setTimeout(() => resolve({ answer: 42 }), 40))");
        Require(promise.GetProperty("answer").GetInt32() == 42, "native expression evaluation with strict CSP");
        await ExpectJavaScriptExceptionAsync(
            () => app.EvaluateScriptAsync("main", "(() => { throw new Error('strict CSP marker'); })()"),
            "Error", "strict CSP marker");
        await ExpectMalformedExpressionAsync(() => app.EvaluateScriptAsync("main", "1 +"));

        main.EvaluateScript("location.href = 'quiet.html';");
        await WaitForBooleanAsync(app, "main", "document.title === 'Lifetime probe' && document.readyState === 'complete'");
        Console.WriteLine("PASS explicit script expressions under strict CSP, including errors and malformed syntax");
    }

    private static async Task VerifyCallerCancellationAsync(AppHandle app)
    {
        using var alreadyCanceled = new CancellationTokenSource();
        alreadyCanceled.Cancel();
        await ExpectCancellationAsync(
            app.EvaluateScriptAsync("main", "1 + 1", alreadyCanceled.Token), "pre-canceled caller token");

        using var cancellation = new CancellationTokenSource();
        var evaluation = app.EvaluateScriptAsync("main",
            "new Promise(() => { window.__netwivTokenEvaluationStarted = true; })", cancellation.Token);
        await WaitForBooleanAsync(app, "main", "window.__netwivTokenEvaluationStarted === true");
        cancellation.Cancel();
        await ExpectCancellationAsync(evaluation, "caller token");
    }

    private static async Task VerifyReloadCancellationAsync(AppHandle app, Webview main)
    {
        var evaluation = StartDelayedEvaluation(app, "main", "Navigation");
        await WaitForBooleanAsync(app, "main", "window.__netwivNavigationEvaluationStarted === true");
        main.EvaluateScript("location.reload();");
        await ExpectCancellationAsync(evaluation, "reload");
        await WaitForBooleanAsync(app, "main", "typeof window.__netwivNavigationEvaluationStarted === 'undefined'");
    }

    private static async Task VerifyCloseCancellationAsync(AppHandle app, Window parent)
    {
        var child = await new WebviewBuilder(app, parent, "evaluation-child").Url("quiet.html").Visible(false).BuildAsync();
        await WaitForDocumentReadyAsync(app, child.Label);
        var evaluation = StartDelayedEvaluation(app, child.Label, "ChildClose");
        await WaitForBooleanAsync(app, child.Label, "window.__netwivChildCloseEvaluationStarted === true");
        await child.CloseAsync();
        await ExpectCancellationAsync(evaluation, "child WebView close");
        Require(await app.GetWebviewAsync(child.Label) is null, "closed child WebView was removed");
    }

    private static async Task VerifyHistoryCancellationAsync(AppHandle app, Webview main)
    {
        await app.EvaluateScriptAsync("main", "(() => { window.__netwivHistorySource = true; addEventListener('pageshow', event => { window.__netwivHistoryPersisted = event.persisted; }); return true; })()");
        var evaluation = StartDelayedEvaluation(app, "main", "History");
        await WaitForBooleanAsync(app, "main", "window.__netwivHistoryEvaluationStarted === true");
        main.EvaluateScript("location.href = 'quiet.html?evaluation-history-target';");
        await ExpectCancellationAsync(evaluation, "history navigation");
        await WaitForBooleanAsync(app, "main", "location.search === '?evaluation-history-target' && document.readyState === 'complete'");
        main.EvaluateScript("history.back();");
        await WaitForBooleanAsync(app, "main", "location.search === '' && document.readyState === 'complete'");
        var restored = await app.EvaluateScriptAsync("main", "window.__netwivHistoryPersisted === true");
        Console.WriteLine($"PASS script evaluation after history return (BFCache restored: {restored.GetBoolean()})");
        Require(evaluation.IsCanceled, "history return revived a canceled evaluation");
    }

    private static async Task VerifyShutdownCancellationAsync(AppHandle app)
    {
        var evaluation = StartDelayedEvaluation(app, "main", "Shutdown");
        await WaitForBooleanAsync(app, "main", "window.__netwivShutdownEvaluationStarted === true");
        app.Exit();
        await ExpectCancellationAsync(evaluation, "runtime shutdown");
    }

    private static Task<JsonElement> StartDelayedEvaluation(AppHandle app, string label, string suffix) =>
        app.EvaluateScriptAsync(label,
            $"new Promise(resolve => {{ window.__netwiv{suffix}EvaluationStarted = true; setTimeout(() => resolve('late result'), 30000); }})");

    private static async Task WaitForDocumentReadyAsync(AppHandle app, string label)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var state = await app.EvaluateScriptAsync(label, "document.readyState");
                if (state.ValueKind == JsonValueKind.String && state.GetString() is "interactive" or "complete") return;
            }
            catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { }
            await Task.Delay(50);
        }
        throw new TimeoutException($"WebView '{label}' did not commit a document.");
    }

    private static async Task WaitForBooleanAsync(AppHandle app, string label, string script)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await app.EvaluateScriptAsync(label, script);
                if (result.ValueKind == JsonValueKind.True) return;
            }
            catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { }
            await Task.Delay(50);
        }
        throw new TimeoutException($"JavaScript condition was not met in WebView '{label}'.");
    }

    private static async Task<JavaScriptException> ExpectJavaScriptExceptionAsync(
        Func<Task<JsonElement>> action,
        string name,
        string? message)
    {
        try { await action(); }
        catch (JavaScriptException error)
        {
            Require(error.Name == name, $"JavaScript exception name was {error.Name}, expected {name}");
            if (message is not null) Require(error.Message.Contains(message, StringComparison.Ordinal), "JavaScript exception message");
            return error;
        }
        throw new InvalidOperationException($"Expected JavaScriptException named {name}.");
    }

    private static async Task ExpectMalformedExpressionAsync(Func<Task<JsonElement>> action)
    {
        try { await action(); }
        catch (JavaScriptException error)
        {
            Require(error.Name == "JavaScriptEvaluationError", "generic engine diagnostic for malformed expression");
            Require(error.JavaScriptStackTrace is null, "missing engine diagnostic must not invent a JavaScript stack");
            return;
        }
        throw new InvalidOperationException("Expected JavaScriptException for malformed expression.");
    }

    private static async Task ExpectCancellationAsync(Task<JsonElement> evaluation, string scenario)
    {
        try { await evaluation.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException($"Script evaluation was not canceled by {scenario}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
