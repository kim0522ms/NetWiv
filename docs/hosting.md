# Generic Host integration

`NetWiv.Hosting` runs a NetWiv app inside the .NET Generic Host. It gives a
desktop app the same foundations as an ASP.NET Core or worker app:

| Library | What NetWiv does with it |
| --- | --- |
| `Microsoft.Extensions.DependencyInjection` | Commands receive services through `[FromServices]` and `[FromKeyedServices]`. Each invocation runs in its own service scope. `AppHandle` is a singleton service. |
| `Microsoft.Extensions.Configuration` and `Options` | `appsettings.json`, `appsettings.{Environment}.json`, environment variables, command-line arguments and user secrets, bound through `IOptions<T>`. |
| `Microsoft.Extensions.Logging` | NetWiv's IPC and command diagnostics and the official log plugin's webview records go through `ILogger`. |
| `Microsoft.Extensions.Hosting` | Hosted services start before the native event loop and stop after it. `IHostApplicationLifetime.StopApplication` closes the app. |

The package is opt-in. `NetWiv.Core` depends only on the dependency-injection
and logging abstractions, and the existing `Builder` API is unchanged.

For a package-only app, use the
[Hosting sample](../samples/NetWiv.HostingApp/README.md). Follow its frontend SDK
preparation step: its npm manifest has not yet migrated to `@netwiv/api`, which
current generated bindings require. The sample combines constructor
injection, invocation-scoped application and plugin commands, options, logging,
explicit capabilities and generated frontend bindings. Its verification helper
separately records managed/AOT publication and real frontend IPC execution;
it does not require expanding or changing the `netwiv new` templates.

## Create an application

Reference `NetWiv.Hosting` next to `NetWiv.Platform.Desktop` and
`NetWiv.Generators`, all at the same version. Desktop settings that previously
went on `Builder` go on `builder.Desktop`.

The package targets .NET 8 and is also consumable from .NET 9/10 applications.
Its transitive build properties enable `EnableConfigurationBindingGenerator`
for NativeAOT-safe configuration binding on SDK 8/9. Source project references
outside this repository should enable that property in the application project
as well (NuGet imports the property automatically).

Example:

```csharp
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetWiv;
using NetWiv.Hosting;
using NetWiv.Platform.Desktop;

[assembly: CommandJsonContext(typeof(AppJsonContext))]

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = NetWivApplication.CreateBuilder(args);
        builder.Services.Configure<GreetingOptions>(builder.Configuration.GetSection("Greeting"));
        builder.Services.AddSingleton<GreetingCounter>();

        builder.Desktop
            .UseRuntime(new DesktopRuntime())
            .Metadata(new("Greeting", "0.1.0", "dev.example.greeting"))
            .Window("main", "Greeting", "index.html")
            .UseEmbeddedAssets()
            .UseEmbeddedConfig(builder.Environment.IsDevelopment()
                ? EmbeddedConfigMode.Development : EmbeddedConfigMode.Production)
            .InvokeHandler("greet");

        using var app = builder.Build();
        app.Run();
    }
}

public sealed class GreetingOptions
{
    public string Prefix { get; set; } = "Hello";
}

public sealed class GreetingCounter
{
    private int _count;
    public int Next() => Interlocked.Increment(ref _count);
}

public static class Commands
{
    [Command]
    public static string Greet(
        string name,
        [FromServices] IOptions<GreetingOptions> options,
        [FromServices] GreetingCounter counter,
        [FromServices] ILogger<GreetingCounter> logger)
    {
        logger.LogInformation("Greeting {Name}", name);
        return $"{options.Value.Prefix}, {name} (#{counter.Next()})";
    }
}

[JsonSerializable(typeof(string))]
public partial class AppJsonContext : JsonSerializerContext;
```

`NetWivApplicationBuilder` implements `IHostApplicationBuilder`, so library
extension methods written for that interface work unchanged.
`NetWivApplication` exposes `Services`, `Configuration`, `Environment`,
`Lifetime`, `Logger`, the underlying `App` and its `Handle`.

The optional [NetWiv.BlazorHybrid host](blazor-hybrid.md) uses the same root
provider with a separate scope for each Razor page. On Windows x64,
`AddNetWivBlazorHybrid()` selects the default managed host;
`AddNetWivBlazorHybridAot(...)` selects a separate experimental host with explicit
component, generated JSON and callback registrations. NativeAOT publishing also
requires `NetWivBlazorExperimentalAot=true`.
Its platform and component-library limits are separate from the Hosting
package's own NativeAOT support.

## Commands and services

### Instance commands and constructor injection

`[Command]` supports both static methods and instance methods on accessible,
non-abstract, non-generic classes. Register the **declaring class** in DI and
select the command explicitly, just as for a static command:

```csharp
var builder = NetWivApplication.CreateBuilder();
builder.Services.AddScoped<EquipmentService>();
builder.Services.AddScoped<EquipmentCommands>();
builder.Desktop.InvokeHandler("equipment_status");
// Configure the desktop runtime and windows, then Build/Run as usual.

public sealed class EquipmentCommands(EquipmentService equipment)
{
    [Command("equipment_status")]
    public Task<string> GetStatus(CancellationToken cancellationToken)
        => equipment.GetStatusAsync(cancellationToken);
}
```

`EquipmentService` is your application's service. Typed arguments/results still
need the application's `CommandJsonContext` and JSON serialization metadata.
The generated handler calls
`context.Services.GetRequiredService<EquipmentCommands>().GetStatus(...)`.
It does not construct the receiver, scan for constructors, or cache it. Registering
only an interface does not register the declaring class; use an explicit factory
registration if needed. Missing registrations or constructor failures fail that
invocation. `[FromServices]` and `[FromKeyedServices]` method parameters continue to
work and resolve from the same scope as the receiver's constructor dependencies.

The service registration controls receiver ownership and lifetime:

| Registration | Behavior |
| --- | --- |
| `AddScoped<EquipmentCommands>()` | One receiver per invocation scope; separate invocations, including concurrent ones, use separate receivers. |
| `AddTransient<EquipmentCommands>()` | One receiver per resolution; disposable receivers resolved by the handler are tracked by that invocation scope. |
| `AddSingleton<EquipmentCommands>()` | One receiver per root provider; it can receive concurrent calls and must be thread-safe. Container-created singletons are disposed with the provider, not after a command. |

The scope stays alive through awaited `Task`/`ValueTask` completion and result
serialization. It is asynchronously disposed before the invocation completes,
including exceptions and cancellation. Cancellation is cooperative: a handler
that ignores its token keeps running; cancellation does not immediately dispose
objects it is still using. Follow normal DI rules: do not inject scoped services
into singletons, and do not retain scoped objects for background work beyond the
invocation. A preconstructed singleton registered with `AddSingleton(instance)`
remains caller-owned under Microsoft DI rules.

NativeAOT remains supported: the generator emits a typed service resolution and
a direct method call, without dynamic code or reflection-based method discovery.
Use AOT-compatible dependencies and typed registrations such as `AddScoped<T>()`
or explicit factories; this does not make arbitrary third-party services AOT-safe.
Static commands and their registration syntax are unchanged. Instance methods on
structs, interfaces or abstract classes are rejected at compile time. Inherited
methods bind to their declaring class, not automatically to every derived class.

### Method parameter injection

- `[FromServices]` resolves a required service. A nullable parameter such as
  `[FromServices] IClock? clock` resolves to null when the service is not
  registered.
- `[FromKeyedServices(key)]` from `Microsoft.Extensions.DependencyInjection`
  resolves a keyed service. The key must be a constant string, integer, char,
  bool, enum or null. The parameterless form and floating-point keys are
  rejected at compile time (`DTOXGEN010`).
- Service parameters are never read from the guest JSON and are omitted from
  generated JavaScript and TypeScript bindings. Without an attribute, a
  parameter is bound from JSON as before; the generator cannot tell a service
  type from a DTO, so the attribute is required.
- Each guest or backend invocation creates one `AsyncServiceScope`, like an HTTP
  request, and disposes it after the command returns. Do not capture scoped
  services in channels, resources or work that outlives the command; resolve
  singletons or create a new scope instead.
- `InvokeContext.Services` is the invocation scope. `AppHandle.Services` is the
  root provider. Plugins and handlers registered with `RegisterCommand` can use
  either directly.
- `AppHandle` can be injected into singletons and hosted services. It is
  available once `Build` returns.

## Configuration and environment

The content root defaults to the application directory, not the working
directory, because desktop apps are often started from a shortcut or a Dock.
In a macOS `.app` bundle the content root is `Contents/Resources`, where the
bundler places every publish file except the executable and native libraries.
Pass `HostApplicationBuilderSettings` to `CreateBuilder` to choose another root.

`Microsoft.NET.Sdk` projects do not copy `appsettings.json` by default. Add:

```xml
<ItemGroup>
  <None Update="appsettings*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`netwiv dev` sets `DOTNET_ENVIRONMENT=Development` for the app process unless
the variable is already set, matching a `dotnet run` launch profile. Published
apps default to `Production`.

`netwiv.conf.json` remains the framework configuration for windows, security
and plugins. `IConfiguration` is the application's own configuration; the two
are not merged.

Under NativeAOT, the configuration binder source generator is enabled
automatically when `PublishAot` is true, so `Configure<T>(section)` and
`Get<T>()` do not need reflection. Option types must be `public` or `internal`;
a private nested type fails the NativeAOT build with `SYSLIB1104`.

## Logging

| Category | Event | Level | Meaning |
| --- | --- | --- | --- |
| `NetWiv.Ipc` | 1 | Warning | An IPC message could not be parsed and was discarded. |
| `NetWiv.Ipc` | 2 | Warning | An IPC request was rejected, for example because of an invalid invoke key. |
| `NetWiv.Commands` | 3 | Debug | The ACL denied a command. |
| `NetWiv.Commands` | 4 | Error | A command threw an exception other than `InvokeException` or a cancellation. |
| `NetWiv.Webview` | 1 | Trace–Error | A record from the `@netwiv/api/plugins/log` guest. |

Intentional `InvokeException` errors and cancellations are not logged as
failures. The guest still receives the same error payload as before.

When the app has a logger factory, `LogPlugin` writes guest records to
`NetWiv.Webview` and stops writing them directly to stdout, so log filters and
providers from configuration apply. It still writes its log file.

## Lifetime

`NetWivApplication.Run` must be called from the process's original main thread,
as with `App.Run`:

1. Start the host. Hosted services start before the native event loop exists,
   so do not touch windows or webviews in `StartAsync`. Use
   `builder.Desktop.Setup(...)` for work that needs the UI.
2. Run the native event loop on the calling thread.
3. After the loop returns, cancel commands still in flight and wait for them,
   including their scope disposal, for up to `HostOptions.ShutdownTimeout`
   (30 seconds by default). A command that ignores cancellation past the
   timeout is abandoned with a warning (event 2 on `NetWivApplication.Logger`), as the host
   abandons a hosted service that does not stop in time.
4. Stop the host. Commands are still accepted at this point, so a hosted
   service can invoke one from its `StopAsync`.

`IHostApplicationLifetime.StopApplication`, including Ctrl+C or SIGTERM from the
console lifetime, exits the event loop. A stop requested before the loop starts
skips it. There is no `RunAsync`, because an asynchronous continuation cannot
own the native UI loop.

`AppHandle.Restart` relaunches only after hosted services have stopped and both
the app and the service provider have been disposed. `App.Run` without the host
keeps its existing restart behavior.

Disposing `NetWivApplication` first stops accepting commands: from that point
`InvokeAsync` throws an `InvokeException` saying the application is shutting
down. It then performs the same bounded wait for commands in flight, disposes
the app and its plugins, and finally the service provider and its singletons.
Because admission closes atomically before the wait, no command can start
unobserved and outlive the services it uses. This also applies when the app is
disposed without running.

## Without the host

An existing provider can be connected directly:

```csharp
var services = new ServiceCollection().AddSingleton<GreetingCounter>().BuildServiceProvider();
using var app = new Builder()
    .UseRuntime(new DesktopRuntime())
    .UseServiceProvider(services)
    .UseLoggerFactory(loggerFactory)
    .InvokeHandler("greet")
    .Build();
app.Run();
services.Dispose();
```

The caller owns the provider and disposes it after the app.
`UseLoggerFactory` is optional; it defaults to the provider's `ILoggerFactory`,
then to a no-op factory.

## Limits and validation

- Plugins are still registered on `builder.Desktop` and are not resolved from
  the container.
- Instance command receivers must be registered in DI by their declaring class.
  Plugin registration does not automatically register a command receiver in DI.
- The optional `blazor-hybrid` template uses Generic Host. Other existing
  `netwiv new` templates retain their current startup model.

The package-only Hosting sample keeps plugin lifecycle registration separate
from DI command receivers and includes an opt-in embedded frontend smoke. It
checks scope identities, concurrent awaited calls, disposal before IPC response,
deny precedence and a generated-but-unregistered plugin command. Follow its
[restore, publish and verification instructions](../samples/NetWiv.HostingApp/README.md#automated-verification)
with an actual matching package feed. A `--build-only` result is publication
evidence; only `--run` executes its managed and NativeAOT consumers, and manual
desktop acceptance remains a separate check.

`tests/NetWiv.Generators.Tests` covers generated service resolution, keys,
optional services, instance command lifetimes and binding exclusion. The shared
instance-command scenarios also run in the Core NativeAOT executable, including
guest IPC, concurrent calls, constructor failure, exceptions and cancellation.
`tests/NetWiv.Core.Tests` covers logging, host wiring, options binding,
`StopApplication` and restart ordering with a real relaunched process.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
