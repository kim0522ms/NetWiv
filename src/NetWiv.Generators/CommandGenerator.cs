using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetWiv.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class CommandGenerator : IIncrementalGenerator
{
    private const string CommandAttributeName = "NetWiv.CommandAttribute";
    private const string JsonContextAttributeName = "NetWiv.CommandJsonContextAttribute";
    private const string PluginAttributeName = "NetWiv.PluginAttribute";
    private const string PluginExportAttributeName = "NetWiv.PluginExportAttribute";
    private const string FromServicesAttributeName = "NetWiv.FromServicesAttribute";
    private const string FromKeyedServicesAttributeName = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";

    private static readonly DiagnosticDescriptor InvalidMethod = new(
        "DTOXGEN001", "Invalid command method",
        "Command method '{0}' must be static, non-generic, non-async-void, and accessible from generated code",
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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commands = context.SyntaxProvider.ForAttributeWithMetadataName(
                CommandAttributeName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (syntaxContext, _) => CreateCandidate(syntaxContext))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

        var input = context.CompilationProvider.Combine(commands.Collect());
        context.RegisterSourceOutput(input, static (productionContext, value) =>
            Emit(productionContext, value.Left, value.Right));

        var plugins = context.SyntaxProvider.ForAttributeWithMetadataName(
                PluginAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, _) => syntaxContext.TargetSymbol as INamedTypeSymbol)
            .Where(static plugin => plugin is not null)
            .Select(static (plugin, _) => plugin!);
        context.RegisterSourceOutput(context.CompilationProvider.Combine(plugins.Collect()), static (productionContext, value) =>
            EmitPlugins(productionContext, value.Left, value.Right));
    }

    private static void EmitPlugins(SourceProductionContext context, Compilation compilation, ImmutableArray<INamedTypeSymbol> candidates)
    {
        var local = new List<INamedTypeSymbol>(candidates.Length);
        foreach (var candidate in candidates.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            if (!IsPlugin(candidate))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidPlugin, candidate.Locations.FirstOrDefault(), candidate.Name));
                continue;
            }
            local.Add(candidate);
        }
        local.Sort(static (left, right) => StringComparer.Ordinal.Compare(
            left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        var registrations = new List<INamedTypeSymbol>(local);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != PluginExportAttributeName ||
                    attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not INamedTypeSymbol plugin ||
                    !IsExternallyCreatablePlugin(plugin) ||
                    registrations.Contains(plugin, SymbolEqualityComparer.Default))
                    continue;
                registrations.Add(plugin);
            }
        }
        if (registrations.Count == 0) return;
        registrations.Sort(static (left, right) => StringComparer.Ordinal.Compare(
            left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        foreach (var plugin in local)
        {
            builder.Append("[assembly: global::NetWiv.PluginExportAttribute(typeof(")
                .Append(plugin.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .AppendLine("))]");
        }
        if (local.Count > 0) builder.AppendLine();
        builder.AppendLine("namespace NetWiv.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"NetWiv.Generators\", \"1.0.0\")]");
        builder.AppendLine("    internal static class PluginBindings");
        builder.AppendLine("    {");
        builder.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializerAttribute]");
        builder.AppendLine("        internal static void Register()");
        builder.AppendLine("        {");
        foreach (var plugin in registrations)
        {
            var typeName = plugin.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            builder.Append("            global::NetWiv.GeneratedPluginRegistry.Register<")
                .Append(typeName)
                .Append(">(static () => new ")
                .Append(typeName)
                .AppendLine("());");
        }
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        context.AddSource("NetWiv.Plugins.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static bool IsPlugin(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType || !IsAccessible(type))
            return false;
        if (!type.AllInterfaces.Any(static contract => contract.ToDisplayString() == "NetWiv.IPlugin"))
            return false;
        return type.InstanceConstructors.Any(static constructor =>
            constructor.Parameters.Length == 0 &&
            constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal);
    }

    private static bool IsExternallyCreatablePlugin(INamedTypeSymbol type)
    {
        if (!IsPlugin(type)) return false;
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public) return false;
        return type.InstanceConstructors.Any(static constructor =>
            constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public);
    }

    private static bool IsAccessible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType ||
                current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
                return false;
        }
        return true;
    }

    private static CommandCandidate? CreateCandidate(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IMethodSymbol method)
        {
            return null;
        }

        string? explicitName = null;
        var attribute = context.Attributes.FirstOrDefault();
        if (attribute is not null && attribute.ConstructorArguments.Length == 1)
        {
            explicitName = attribute.ConstructorArguments[0].Value as string;
        }

        return new CommandCandidate(method, explicitName ?? ToSnakeCase(method.Name));
    }

    private static void Emit(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<CommandCandidate> candidates)
    {
        var exportFrontend = compilation.Assembly.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "NetWiv.GenerateFrontendBindingsAttribute");
        if (candidates.IsDefaultOrEmpty)
        {
            if (exportFrontend) FrontendBindingEmitter.Emit(context, null, Array.Empty<FrontendCommand>());
            return;
        }

        var jsonContext = FindJsonContext(compilation, context, out var contextIsInvalid);
        if (contextIsInvalid)
        {
            return;
        }

        var valid = new List<CommandModel>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (TryCreateModel(context, candidate, jsonContext, out var model))
            {
                valid.Add(model!);
            }
        }

        var duplicates = valid.GroupBy(static command => (command.IsPluginCommand
                ? command.Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "\0" : "") + command.ExternalName, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .ToArray();
        if (duplicates.Length != 0)
        {
            var duplicateMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var duplicate in duplicates)
            {
                foreach (var command in duplicate)
                {
                    duplicateMethods.Add(command.Method);
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateName,
                        command.Method.Locations.FirstOrDefault(),
                        command.ExternalName));
                }
            }

            valid.RemoveAll(command => duplicateMethods.Contains(command.Method));
        }

        if (valid.Count == 0)
        {
            if (exportFrontend) FrontendBindingEmitter.Emit(context, jsonContext, Array.Empty<FrontendCommand>());
            return;
        }

        valid.Sort(static (left, right) => StringComparer.Ordinal.Compare(
            left.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            right.Method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        var source = Render(valid, jsonContext);
        context.AddSource("NetWiv.Commands.g.cs", SourceText.From(source, Encoding.UTF8));
        if (exportFrontend)
        {
            var frontend = new List<FrontendCommand>();
            foreach (var command in valid)
            {
                var name = command.ExternalName;
                if (command.IsPluginCommand)
                {
                    var type = command.Method.ContainingType;
                    var contract = type.AllInterfaces.First(static item => item.ToDisplayString() == "NetWiv.IPlugin");
                    var property = type.FindImplementationForInterfaceMember(contract.GetMembers("Name").Single()) as IPropertySymbol;
                    var syntax = property?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as PropertyDeclarationSyntax;
                    var expression = syntax?.ExpressionBody?.Expression;
                    var constant = expression is null ? default : compilation.GetSemanticModel(expression.SyntaxTree).GetConstantValue(expression);
                    if (property is null || property.IsVirtual || property.IsOverride || constant.Value is not string pluginName || string.IsNullOrWhiteSpace(pluginName))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(PluginFrontendName, type.Locations.FirstOrDefault(), type.Name));
                        return;
                    }
                    name = "plugin:" + pluginName + "|" + name;
                }
                frontend.Add(new FrontendCommand(name,
                    command.Parameters.Where(static parameter => parameter.Injection is null)
                        .Select(static parameter => parameter.Symbol).ToArray(),
                    command.Return.ValueType, command.Method));
            }
            var collisions = frontend.GroupBy(static command => command.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1).ToArray();
            if (collisions.Length > 0)
            {
                foreach (var group in collisions)
                    foreach (var command in group)
                        context.ReportDiagnostic(Diagnostic.Create(DuplicateName, command.Method.Locations.FirstOrDefault(), group.Key));
                return;
            }
            FrontendBindingEmitter.Emit(context, jsonContext, frontend);
        }
    }

    private static INamedTypeSymbol? FindJsonContext(
        Compilation compilation,
        SourceProductionContext context,
        out bool invalid)
    {
        invalid = false;
        var attribute = compilation.Assembly.GetAttributes()
            .FirstOrDefault(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == JsonContextAttributeName);

        if (attribute is null)
        {
            return null;
        }

        var type = attribute.ConstructorArguments.Length == 1
            ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
            : null;
        var baseType = type;
        while (baseType is not null &&
               baseType.ToDisplayString() != "System.Text.Json.Serialization.JsonSerializerContext")
        {
            baseType = baseType.BaseType;
        }

        if (type is null || baseType is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidJsonContext,
                attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
            invalid = true;
            return null;
        }

        return type;
    }

    private static bool TryCreateModel(
        SourceProductionContext context,
        CommandCandidate candidate,
        INamedTypeSymbol? jsonContext,
        out CommandModel? model)
    {
        model = null;
        var method = candidate.Method;
        if (!IsCallable(method))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidMethod, method.Locations.FirstOrDefault(), method.Name));
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate.ExternalName) ||
            method.ContainingType.AllInterfaces.Any(static type => type.ToDisplayString() == "NetWiv.IPlugin") &&
            (candidate.ExternalName.IndexOf(':') >= 0 || candidate.ExternalName.IndexOf('|') >= 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidName, method.Locations.FirstOrDefault(), method.Name));
            return false;
        }

        var parameters = ImmutableArray.CreateBuilder<ParameterModel>(method.Parameters.Length);
        var injectionKinds = new HashSet<string>(StringComparer.Ordinal);
        var needsJson = false;
        foreach (var parameter in method.Parameters)
        {
            var service = GetServiceBinding(parameter, out var invalidService);
            if (invalidService)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidServiceParameter, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name));
                return false;
            }

            // A service attribute takes precedence over type-based injection.
            var injection = service is not null ? "Service:" + parameter.Name : GetInjection(parameter.Type);
            if (injection is not null && !injectionKinds.Add(injection))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateInjection, parameter.Locations.FirstOrDefault(), method.Name, injection));
                return false;
            }

            if (parameter.RefKind != RefKind.None || !IsClosedManagedType(parameter.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidParameter, parameter.Locations.FirstOrDefault(), parameter.Name, method.Name));
                return false;
            }

            if (injection is null)
            {
                needsJson |= !IsJsonElement(parameter.Type) && !IsChannel(parameter.Type) && !IsByteArray(parameter.Type);
            }

            parameters.Add(new ParameterModel(parameter, injection, service));
        }

        var returnModel = GetReturnModel(method.ReturnType);
        if (returnModel is null || method.ReturnsByRef || method.ReturnsByRefReadonly ||
            (method.IsAsync && method.ReturnsVoid))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidMethod, method.Locations.FirstOrDefault(), method.Name));
            return false;
        }

        needsJson |= returnModel.Value.HasValue &&
            !IsJsonElement(returnModel.Value.ValueType!) && !IsByteArray(returnModel.Value.ValueType!);
        if (needsJson && jsonContext is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingJsonContext, method.Locations.FirstOrDefault(), candidate.ExternalName));
            return false;
        }

        model = new CommandModel(method, candidate.ExternalName, parameters.ToImmutable(), returnModel.Value);
        return true;
    }

    private static bool IsCallable(IMethodSymbol method)
    {
        if (!method.IsStatic || method.IsAbstract || method.IsGenericMethod || method.MethodKind != MethodKind.Ordinary ||
            method.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
        {
            return false;
        }

        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsGenericType ||
                type.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsClosedManagedType(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
            type is INamedTypeSymbol { IsRefLikeType: true } || ContainsTypeParameter(type))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.TypeParameter)
        {
            return true;
        }

        return type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsTypeParameter);
    }

    private static string? GetInjection(ITypeSymbol type)
    {
        var original = type is INamedTypeSymbol named ? named.OriginalDefinition : type;
        return original.ToDisplayString() switch
        {
            "NetWiv.AppHandle" => "AppHandle",
            "NetWiv.InvokeContext" => "InvokeContext",
            "NetWiv.Request" => "Request",
            "NetWiv.State<T>" => "State:" + ((INamedTypeSymbol)type).TypeArguments[0]
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            "System.Threading.CancellationToken" => "CancellationToken",
            _ => null,
        };
    }

    private static ServiceBinding? GetServiceBinding(IParameterSymbol parameter, out bool invalid)
    {
        invalid = false;
        ServiceBinding? binding = null;
        foreach (var attribute in parameter.GetAttributes())
        {
            ServiceBinding? current;
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case FromServicesAttributeName:
                    current = new ServiceBinding(null);
                    break;
                case FromKeyedServicesAttributeName:
                    // The parameterless constructor selects runtime key inheritance, which has no command scope meaning.
                    var key = attribute.ConstructorArguments.Length == 1 ? RenderServiceKey(attribute.ConstructorArguments[0]) : null;
                    if (key is null)
                    {
                        invalid = true;
                        return null;
                    }
                    current = new ServiceBinding(key);
                    break;
                default:
                    continue;
            }

            if (binding is not null)
            {
                invalid = true;
                return null;
            }
            binding = current;
        }

        return binding;
    }

    private static string? RenderServiceKey(TypedConstant key)
    {
        if (key.IsNull)
        {
            return "null";
        }

        if (key.Kind == TypedConstantKind.Enum)
        {
            return "(" + key.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" +
                Convert.ToString(key.Value, CultureInfo.InvariantCulture) + ")";
        }

        if (key.Value is string text)
        {
            return Literal(text);
        }

        // Keys are compared as boxed objects, so the literal keeps its exact primitive type.
        return key.Kind == TypedConstantKind.Primitive && key.Value is not (float or double)
            ? "(" + key.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" +
                Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(key.Value!, quoteStrings: true, useHexadecimalNumbers: false)
            : null;
    }

    private static string RenderServiceResolution(IParameterSymbol parameter, ServiceBinding service)
    {
        var optional = parameter.NullableAnnotation == NullableAnnotation.Annotated;
        var type = parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return service.KeyExpression is null
            ? "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions." +
                (optional ? "GetService<" : "GetRequiredService<") + type + ">(context.Services)"
            : "global::Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions." +
                (optional ? "GetKeyedService<" : "GetRequiredKeyedService<") + type + ">(context.Services, " + service.KeyExpression + ")";
    }

    private static ReturnModel? GetReturnModel(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Void)
        {
            return new ReturnModel(false, false, null);
        }

        if (type is INamedTypeSymbol named)
        {
            var original = named.OriginalDefinition.ToDisplayString();
            if (original == "System.Threading.Tasks.Task")
            {
                return new ReturnModel(true, false, null);
            }

            if (original == "System.Threading.Tasks.Task<TResult>")
            {
                return new ReturnModel(true, true, named.TypeArguments[0]);
            }

            if (original == "System.Threading.Tasks.ValueTask")
            {
                return new ReturnModel(true, false, null);
            }

            if (original == "System.Threading.Tasks.ValueTask<TResult>")
            {
                return new ReturnModel(true, true, named.TypeArguments[0]);
            }
        }

        return new ReturnModel(false, true, type);
    }

    private static bool IsJsonElement(ITypeSymbol type) =>
        type.ToDisplayString() == "System.Text.Json.JsonElement";

    private static bool IsChannel(ITypeSymbol type) =>
        type.ToDisplayString() == "NetWiv.Channel";

    private static bool IsGenericChannel(ITypeSymbol type) =>
        type is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == "NetWiv.Channel<T>";

    private static bool IsByteArray(ITypeSymbol type) =>
        type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte };

    private static string Render(IReadOnlyList<CommandModel> commands, INamedTypeSymbol? jsonContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace NetWiv.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"NetWiv.Generators\", \"1.0.0\")]");
        builder.AppendLine("    internal static class CommandBindings");
        builder.AppendLine("    {");
        builder.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializerAttribute]");
        builder.AppendLine("        internal static void Register()");
        builder.AppendLine("        {");
        for (var index = 0; index < commands.Count; index++)
        {
            if (commands[index].IsPluginCommand)
            {
                builder.Append("            global::NetWiv.GeneratedPluginCommandRegistry.Register<")
                    .Append(commands[index].Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(">(").Append(Literal(commands[index].ExternalName)).Append(", Handle_")
                    .Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
            }
            else
            {
                builder.Append("            global::NetWiv.GeneratedCommandRegistry.Register(")
                    .Append(Literal(commands[index].ExternalName))
                    .Append(", static registry => registry.Register(new global::NetWiv.CommandDescriptor(")
                    .Append(Literal(commands[index].ExternalName)).Append(", Handle_")
                    .Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(")));");
            }
        }
        builder.AppendLine("        }");

        for (var index = 0; index < commands.Count; index++)
        {
            RenderHandler(builder, commands[index], index);
        }

        if (commands.Any(static command => command.NeedsNullResult))
        {
            builder.AppendLine();
            builder.AppendLine("        private static global::System.Text.Json.JsonElement NullResult()");
            builder.AppendLine("        {");
            builder.AppendLine("            using var document = global::System.Text.Json.JsonDocument.Parse(\"null\");");
            builder.AppendLine("            return document.RootElement.Clone();");
            builder.AppendLine("        }");
        }

        if (jsonContext is not null)
        {
            builder.AppendLine();
            builder.AppendLine("        private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> JsonTypeInfo<T>()");
            builder.AppendLine("        {");
            builder.Append("            return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)(")
                .Append(jsonContext.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .AppendLine(".Default.GetTypeInfo(typeof(T))");
            builder.AppendLine("                ?? throw new global::NetWiv.InvokeException($\"The configured JSON context does not contain metadata for '{typeof(T)}'.\"));");
            builder.AppendLine("        }");

            builder.AppendLine();
            builder.AppendLine("        private static T DeserializeArgument<T>(global::System.Text.Json.JsonElement value, string command, string argument, bool nullable)");
            builder.AppendLine("        {");
            builder.AppendLine("            try");
            builder.AppendLine("            {");
            builder.AppendLine("                return global::NetWiv.CommandArgumentJson.Deserialize(value, JsonTypeInfo<T>(), command, argument, nullable);");
            builder.AppendLine("            }");
            builder.AppendLine("            catch (global::System.Exception exception) when (exception is global::System.Text.Json.JsonException or global::System.NotSupportedException)");
            builder.AppendLine("            {");
            builder.AppendLine("                throw new global::NetWiv.InvokeException($\"invalid args `{argument}` for command `{command}`: {exception.Message}\", exception);");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
        }

        if (commands.Any(static command => command.NeedsByteArrayHelper))
        {
            builder.AppendLine();
            builder.AppendLine("        private static byte[]? DeserializeByteArray(global::System.Text.Json.JsonElement value, string command, string argument)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (value.ValueKind == global::System.Text.Json.JsonValueKind.Null) return null;");
            builder.AppendLine("            if (value.ValueKind != global::System.Text.Json.JsonValueKind.Array)");
            builder.AppendLine("                throw new global::NetWiv.InvokeException($\"invalid args `{argument}` for command `{command}`: invalid type, expected a byte array\");");
            builder.AppendLine("            try");
            builder.AppendLine("            {");
            builder.AppendLine("                var bytes = new byte[value.GetArrayLength()];");
            builder.AppendLine("                var index = 0;");
            builder.AppendLine("                foreach (var item in value.EnumerateArray()) bytes[index++] = item.GetByte();");
            builder.AppendLine("                return bytes;");
            builder.AppendLine("            }");
            builder.AppendLine("            catch (global::System.Exception exception) when (exception is global::System.InvalidOperationException or global::System.FormatException)");
            builder.AppendLine("            {");
            builder.AppendLine("                throw new global::NetWiv.InvokeException($\"invalid args `{argument}` for command `{command}`: expected integers from 0 through 255\", exception);");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        private static global::System.Text.Json.JsonElement SerializeByteArray(byte[]? values)");
            builder.AppendLine("        {");
            builder.AppendLine("            using var stream = new global::System.IO.MemoryStream();");
            builder.AppendLine("            using (var writer = new global::System.Text.Json.Utf8JsonWriter(stream))");
            builder.AppendLine("            {");
            builder.AppendLine("                if (values is null) writer.WriteNullValue();");
            builder.AppendLine("                else");
            builder.AppendLine("                {");
            builder.AppendLine("                    writer.WriteStartArray();");
            builder.AppendLine("                    foreach (var value in values) writer.WriteNumberValue(value);");
            builder.AppendLine("                    writer.WriteEndArray();");
            builder.AppendLine("                }");
            builder.AppendLine("            }");
            builder.AppendLine("            using var document = global::System.Text.Json.JsonDocument.Parse(stream.ToArray());");
            builder.AppendLine("            return document.RootElement.Clone();");
            builder.AppendLine("        }");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void RenderHandler(
        StringBuilder builder,
        CommandModel command,
        int index)
    {
        builder.AppendLine();
        builder.Append("        private static ");
        if (command.Return.IsAwaitable)
        {
            builder.Append("async ");
        }
        builder.Append("global::System.Threading.Tasks.ValueTask<global::System.Text.Json.JsonElement> Handle_")
            .Append(index.ToString(CultureInfo.InvariantCulture))
            .AppendLine("(global::NetWiv.InvokeContext context, global::System.Text.Json.JsonElement arguments, global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("        {");

        for (var parameterIndex = 0; parameterIndex < command.Parameters.Length; parameterIndex++)
        {
            var parameter = command.Parameters[parameterIndex];
            if (parameter.Injection is not null)
            {
                continue;
            }

            var local = "value_" + parameterIndex.ToString(CultureInfo.InvariantCulture);
            var element = "json_" + parameterIndex.ToString(CultureInfo.InvariantCulture);
            builder.Append("            if (arguments.ValueKind != global::System.Text.Json.JsonValueKind.Object || !arguments.TryGetProperty(")
                .Append(Literal(parameter.Symbol.Name)).Append(", out var ").Append(element).AppendLine("))");
            builder.AppendLine("            {");
            builder.Append("                throw new global::NetWiv.InvokeException(")
                .Append(Literal("invalid args `" + parameter.Symbol.Name + "` for command `" + command.ExternalName + "`: command " + command.ExternalName + " missing required key " + parameter.Symbol.Name))
                .AppendLine(");");
            builder.AppendLine("            }");
            builder.Append("            var ").Append(local).Append(" = ");
            if (IsJsonElement(parameter.Symbol.Type))
            {
                builder.Append(element).AppendLine(".Clone();");
            }
            else if (IsChannel(parameter.Symbol.Type))
            {
                builder.Append("global::NetWiv.Channel.FromJson(context, ").Append(element).AppendLine(");");
            }
            else if (IsGenericChannel(parameter.Symbol.Type))
            {
                var itemType = ((INamedTypeSymbol)parameter.Symbol.Type).TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                builder.Append("new global::NetWiv.Channel<").Append(itemType)
                    .Append(">(global::NetWiv.Channel.FromJson(context, ").Append(element)
                    .Append("), JsonTypeInfo<").Append(itemType).AppendLine(">());");
            }
            else if (IsByteArray(parameter.Symbol.Type))
            {
                builder.Append("DeserializeByteArray(").Append(element).Append(", ")
                    .Append(Literal(command.ExternalName)).Append(", ")
                    .Append(Literal(parameter.Symbol.Name)).AppendLine(");");
            }
            else
            {
                builder.Append("DeserializeArgument<")
                    .Append(parameter.Symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(">(").Append(element).Append(", ")
                    .Append(Literal(command.ExternalName)).Append(", ")
                    .Append(Literal(parameter.Symbol.Name)).Append(", ")
                    .Append(parameter.Symbol.NullableAnnotation == NullableAnnotation.Annotated ? "true" : "false").AppendLine(");");
            }
            if (parameter.Symbol.Type.IsReferenceType && parameter.Symbol.NullableAnnotation != NullableAnnotation.Annotated)
            {
                builder.Append("            if (").Append(local).AppendLine(" is null)");
                builder.AppendLine("            {");
                builder.Append("                throw new global::NetWiv.InvokeException(")
                    .Append(Literal("invalid args `" + parameter.Symbol.Name + "` for command `" + command.ExternalName + "`: invalid type: null, expected " + parameter.Symbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))
                    .AppendLine(");");
                builder.AppendLine("            }");
            }
        }

        builder.Append("            ");
        if (command.Return.HasValue)
        {
            builder.Append("var result = ");
        }
        if (command.Return.IsAwaitable)
        {
            builder.Append("await ");
        }
        builder.Append(command.Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Append('.').Append(command.Method.Name).Append('(');
        for (var parameterIndex = 0; parameterIndex < command.Parameters.Length; parameterIndex++)
        {
            if (parameterIndex != 0)
            {
                builder.Append(", ");
            }
            var parameter = command.Parameters[parameterIndex];
            if (parameter.Service is { } service)
            {
                builder.Append(RenderServiceResolution(parameter.Symbol, service));
            }
            else if (parameter.Injection is { } injection && injection.StartsWith("State:", StringComparison.Ordinal))
            {
                builder.Append("context.State<")
                    .Append(((INamedTypeSymbol)parameter.Symbol.Type).TypeArguments[0]
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(">()");
            }
            else
            {
                builder.Append(parameter.Injection switch
                {
                    "AppHandle" => "context.App",
                    "InvokeContext" => "context",
                    "Request" => "context.Request",
                    "CancellationToken" => "cancellationToken",
                    _ => "value_" + parameterIndex.ToString(CultureInfo.InvariantCulture),
                });
            }
        }
        builder.Append(')');
        if (command.Return.IsAwaitable)
        {
            builder.Append(".ConfigureAwait(false)");
        }
        builder.AppendLine(";");

        if (!command.Return.HasValue)
        {
            RenderReturn(builder, command.Return.IsAwaitable, "NullResult()");
        }
        else if (IsJsonElement(command.Return.ValueType!))
        {
            RenderReturn(builder, command.Return.IsAwaitable, "result.Clone()");
        }
        else if (IsByteArray(command.Return.ValueType!))
        {
            RenderReturn(builder, command.Return.IsAwaitable, "SerializeByteArray(result)");
        }
        else
        {
            var expression = new StringBuilder("global::System.Text.Json.JsonSerializer.SerializeToElement(result, JsonTypeInfo<")
                .Append(command.Return.ValueType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(">())")
                .ToString();
            RenderReturn(builder, command.Return.IsAwaitable, expression);
        }
        builder.AppendLine("        }");
    }

    private static void RenderReturn(StringBuilder builder, bool isAwaitable, string expression)
    {
        builder.Append("            return ");
        if (!isAwaitable)
        {
            builder.Append("new global::System.Threading.Tasks.ValueTask<global::System.Text.Json.JsonElement>(");
        }
        builder.Append(expression);
        if (!isAwaitable)
        {
            builder.Append(')');
        }
        builder.AppendLine(";");
    }

    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    // Treat a run of capitals as one word: GetURLValue -> get_url_value. Unicode
    // letters are preserved and use their invariant lowercase representation.
    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            var previousIsLowerOrDigit = index > 0 &&
                (char.IsLower(value[index - 1]) || char.IsDigit(value[index - 1]));
            var endsAcronym = index > 0 && index + 1 < value.Length &&
                char.IsUpper(value[index - 1]) && char.IsUpper(current) && char.IsLower(value[index + 1]);
            if (builder.Length != 0 && char.IsUpper(current) && (previousIsLowerOrDigit || endsAcronym))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private sealed class CommandCandidate
    {
        public CommandCandidate(IMethodSymbol method, string externalName)
        {
            Method = method;
            ExternalName = externalName;
        }

        public IMethodSymbol Method { get; }
        public string ExternalName { get; }
    }

    private sealed class CommandModel
    {
        public CommandModel(
            IMethodSymbol method,
            string externalName,
            ImmutableArray<ParameterModel> parameters,
            ReturnModel @return)
        {
            Method = method;
            ExternalName = externalName;
            Parameters = parameters;
            Return = @return;
        }

        public IMethodSymbol Method { get; }
        public string ExternalName { get; }
        public ImmutableArray<ParameterModel> Parameters { get; }
        public ReturnModel Return { get; }
        public bool IsPluginCommand => Method.ContainingType.AllInterfaces.Any(static type => type.ToDisplayString() == "NetWiv.IPlugin");
        public bool NeedsNullResult => !Return.HasValue;
        public bool NeedsByteArrayHelper =>
            Parameters.Any(static parameter => IsByteArray(parameter.Symbol.Type)) ||
            (Return.HasValue && IsByteArray(Return.ValueType!));
    }

    private sealed class ParameterModel
    {
        public ParameterModel(IParameterSymbol symbol, string? injection, ServiceBinding? service)
        {
            Symbol = symbol;
            Injection = injection;
            Service = service;
        }

        public IParameterSymbol Symbol { get; }
        public string? Injection { get; }
        public ServiceBinding? Service { get; }
    }

    private sealed class ServiceBinding
    {
        public ServiceBinding(string? keyExpression) => KeyExpression = keyExpression;

        /// <summary>A C# expression for the service key, or null for an unkeyed service.</summary>
        public string? KeyExpression { get; }
    }

    private struct ReturnModel
    {
        public ReturnModel(bool isAwaitable, bool hasValue, ITypeSymbol? valueType)
        {
            IsAwaitable = isAwaitable;
            HasValue = hasValue;
            ValueType = valueType;
        }

        public bool IsAwaitable { get; }
        public bool HasValue { get; }
        public ITypeSymbol? ValueType { get; }
    }
}
