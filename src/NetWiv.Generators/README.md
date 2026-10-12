# NetWiv command generator

The opt-in frontend JS/TypeScript exporter is described in
[the frontend generator contract](../../docs/frontend-generator.md). It is under
implementation and is not part of the earlier beta.1 package snapshot.

`NetWiv.Generators` converts static and DI-resolved instance methods marked with `NetWiv.CommandAttribute`
into direct `CommandHandler` delegates. It emits no reflection, runtime method
lookup, or runtime-generated code.

Instance receivers are resolved by their declaring class from `context.Services`.
Register that class with the desired DI lifetime; no automatic construction or
receiver caching occurs. Instance owners must be concrete, non-generic classes.
The existing invocation scope owns scoped/transient disposable receivers and
awaits cleanup after completion or failure; singleton ownership remains with DI.
See [instance commands and constructor injection](../../docs/hosting.md#instance-commands-and-constructor-injection).

The default external name is deterministic snake case. For example,
`GetUser` becomes `get_user` and `GetHTTPServerURL` becomes
`get_http_server_url`. An explicit name is preserved exactly:

```csharp
[Command("account.current")]
public static Task<Account> GetCurrentAccount() { /* ... */ }
```

Typed JSON arguments and results require a context declared in application
source so that `System.Text.Json`'s generator can see it in its own input pass:

```csharp
[assembly: CommandJsonContext(typeof(AppJsonContext))]

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Account))]
public partial class AppJsonContext : JsonSerializerContext;
```

Do not emit this context from another source generator. Roslyn does not define
an ordering in which one generator can consume another generator's output.

`AppHandle`, `InvokeContext`, `State<T>`, and `CancellationToken` parameters are
injected. Parameters marked `[FromServices]` or `[FromKeyedServices(key)]`
resolve from the invocation's service scope; see
[Generic Host integration](../../docs/hosting.md). Other parameters bind from an
object property with the same C# parameter name. `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, synchronous
values, and `void` are supported.

An attribute generates a binding but does not expose the command. Select the
commands for an application explicitly, as with TAURI®'s `generate_handler!`:

```csharp
new Builder()
    .InvokeHandler("account.current", "save_settings")
    // ...
    .Run();
```

`RegisterGeneratedCommands()` is the explicit convenience opt-in for every
generated command in the process. Top-level `byte[]` command parameters and
results use Tauri's JSON array-of-integers representation instead of the
default `System.Text.Json` base64 string representation. Byte arrays nested in
DTOs require an application JSON converter that uses that same wire shape.

Classes marked `[Plugin]` must implement `IPlugin` and have an accessible
parameterless constructor. The generator registers a static factory with
`GeneratedPluginRegistry` and emits an assembly-level plugin export. A consuming
application that references `NetWiv.Generators` as an analyzer discovers those
exports in referenced plugin assemblies and emits typed factories that keep the
plugins reachable under NativeAOT. No reflection or runtime activation is used.

---

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
