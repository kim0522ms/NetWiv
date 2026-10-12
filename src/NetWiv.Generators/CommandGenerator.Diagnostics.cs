using Microsoft.CodeAnalysis;

namespace NetWiv.Generators;

public sealed partial class CommandGenerator
{
    private static readonly DiagnosticDescriptor InvalidMethod = new(
        "DTOXGEN001", "Invalid command method",
        "Command method '{0}' must be non-generic, non-async-void, accessible from generated code, and static or declared on a concrete class",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor InvalidParameter = new(
        "DTOXGEN002", "Invalid command parameter",
        "Parameter '{0}' on command '{1}' cannot be ref, out, pointer, function pointer, or an open generic type",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor DuplicateName = new(
        "DTOXGEN003", "Duplicate command name",
        "Command name '{0}' is used by more than one method",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor MissingJsonContext = new(
        "DTOXGEN004", "Missing command JSON context",
        "Command '{0}' binds or returns JSON values; declare an AOT-safe source-generated context with [assembly: NetWiv.CommandJsonContext(typeof(YourJsonContext))]",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor InvalidJsonContext = new(
        "DTOXGEN005", "Invalid command JSON context",
        "The type supplied to CommandJsonContextAttribute must derive from System.Text.Json.Serialization.JsonSerializerContext",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor InvalidName = new(
        "DTOXGEN006", "Invalid command name",
        "Command method '{0}' has an empty external name or a plugin-local name containing ':' or '|'",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor DuplicateInjection = new(
        "DTOXGEN007", "Duplicate injected parameter",
        "Command '{0}' has more than one injected parameter of type '{1}'",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor InvalidServiceParameter = new(
        "DTOXGEN010", "Invalid service parameter",
        "Parameter '{0}' on command '{1}' must use exactly one of [FromServices] or [FromKeyedServices(key)] with a constant string, integer, char, bool, enum or null key",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor InvalidPlugin = new(
        "DTOXGEN008", "Invalid plugin type",
        "Plugin type '{0}' must be a non-abstract, non-generic implementation of NetWiv.IPlugin with an accessible parameterless constructor",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor PluginFrontendName = new(
        "DTOXGEN011", "Plugin frontend name cannot be inferred",
        "Plugin '{0}' requires a non-virtual, constant expression-bodied Name getter to export frontend bindings; runtime naming remains supported without GenerateFrontendBindings",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor InvalidPluginCommandOwner = new(
        "DTOXGEN012", "Invalid plugin command owner",
        "Command method '{0}' must be declared on a concrete plugin type; '{1}' is abstract or an interface",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);
}
