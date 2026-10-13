using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace NetWiv.Generators;

public sealed partial class CommandGenerator
{
    private static INamedTypeSymbol? FindJsonContext(
        Compilation compilation,
        SourceProductionContext context,
        out bool invalid)
        => FindJsonContext(compilation.Assembly, context, out invalid);

    private static INamedTypeSymbol? FindJsonContext(
        IAssemblySymbol assembly,
        SourceProductionContext context,
        out bool invalid)
    {
        invalid = false;
        var attribute = assembly.GetAttributes()
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

        var isPluginCommand = method.ContainingType.AllInterfaces.Any(static type => type.ToDisplayString() == "NetWiv.IPlugin");
        if (isPluginCommand && (method.ContainingType.TypeKind == TypeKind.Interface || method.ContainingType.IsAbstract))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidPluginCommandOwner, method.Locations.FirstOrDefault(), method.Name, method.ContainingType.Name));
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate.ExternalName) ||
            isPluginCommand &&
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
        if (method.IsAbstract || method.IsGenericMethod || method.MethodKind != MethodKind.Ordinary ||
            (!method.IsStatic && (method.ContainingType.TypeKind != TypeKind.Class || method.ContainingType.IsAbstract)) ||
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
}
