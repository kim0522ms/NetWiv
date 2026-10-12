using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NetWiv.Generators;

[Generator(LanguageNames.CSharp)]
public sealed partial class CommandGenerator : IIncrementalGenerator
{
    private const string CommandAttributeName = "NetWiv.CommandAttribute";
    private const string JsonContextAttributeName = "NetWiv.CommandJsonContextAttribute";
    private const string PluginAttributeName = "NetWiv.PluginAttribute";
    private const string PluginExportAttributeName = "NetWiv.PluginExportAttribute";
    private const string FromServicesAttributeName = "NetWiv.FromServicesAttribute";
    private const string FromKeyedServicesAttributeName = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";

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
}
