using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NetWiv.Generators;

public sealed partial class CommandGenerator
{
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
