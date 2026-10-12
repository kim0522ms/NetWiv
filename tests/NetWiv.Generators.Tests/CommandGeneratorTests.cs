using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using NetWiv;

namespace NetWiv.Generators.Tests;

public sealed class CommandGeneratorTests
{
    [Theory]
    [InlineData("abstract class")]
    [InlineData("interface")]
    public void RejectsCommandsDeclaredOnNonConcretePluginOwners(string declaration)
    {
        var result = Run($$"""
            using NetWiv;
            public {{declaration}} PluginBase : IPlugin {
                string IPlugin.Name => "example";
                void IPlugin.Initialize(PluginContext context) { }
                [Command] public static void Ping() { }
            }
            public sealed class ConcretePlugin : PluginBase { }
            """);
        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("DTOXGEN012", diagnostic.Id);
        Assert.Contains("PluginBase", diagnostic.GetMessage());
        Assert.DoesNotContain(result.CompilationDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("GeneratedPluginCommandRegistry.Register", result.GeneratedSource);
    }

    [Fact]
    public void ConcretePluginCommandsAndOrdinaryAbstractClassCommandsRemainValid()
    {
        var result = Run("""
            using NetWiv;
            public abstract class PluginBase : IPlugin {
                public string Name => "example";
                public abstract void Initialize(PluginContext context);
            }
            public sealed class ConcretePlugin : PluginBase {
                public override void Initialize(PluginContext context) => context.RegisterGeneratedCommands<ConcretePlugin>("ping");
                [Command] public static void Ping() { }
            }
            public abstract class AppCommands { [Command] public static void Ping() { } }
            """);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.CompilationDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GeneratedPluginCommandRegistry.Register<global::ConcretePlugin>(\"ping\"", result.GeneratedSource);
        Assert.Contains("GeneratedCommandRegistry.Register(\"ping\"", result.GeneratedSource);
    }

    [Theory]
    [InlineData("plugin:other|ping")]
    [InlineData("other:ping")]
    [InlineData("other|ping")]
    public void PluginCommandsRejectQualifiedLocalNames(string name)
    {
        var result = Run($$"""
            using NetWiv;
            public sealed class Example : IPlugin {
                public string Name => "example";
                public void Initialize(PluginContext context) { }
                [Command("{{name}}")] public static void Ping() { }
            }
            """);
        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "DTOXGEN006");
    }

    [Fact]
    public void RuntimePluginNamesDoNotNeedToBeCompileTimeConstants()
    {
        var result = Run("""
            using NetWiv;
            public sealed class Example(string name) : IPlugin {
                public string Name => name;
                public void Initialize(PluginContext context) => context.RegisterGeneratedCommands<Example>("ping");
                [Command] public static void Ping() { }
            }
            """);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.CompilationDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void InjectsRequestWithoutGeneratingAFrontendArgumentOrSerializer()
    {
        var result = Run("""
            using NetWiv;
            [assembly: GenerateFrontendBindings]
            public static class Commands
            {
                [Command] public static void Upload(NetWiv.Request request) { }
            }
            """);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.CompilationDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("context.Request", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DeserializeArgument<global::NetWiv.Request>", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesServiceParametersWithoutFrontendArgumentsOrSerializers()
    {
        var result = Run("""
            using Microsoft.Extensions.DependencyInjection;
            using NetWiv;
            [assembly: GenerateFrontendBindings]
            public enum Slot { First, Second }
            public sealed class Clock { }
            public interface IOptional { }
            public static class Commands
            {
                [Command] public static void Tick(
                    [FromServices] Clock clock,
                    [FromServices] IOptional? optional,
                    [FromKeyedServices("utc")] Clock utc,
                    [FromKeyedServices(3L)] Clock numbered,
                    [FromKeyedServices(Slot.Second)] Clock slotted,
                    [FromServices] AppHandle handle) { }
            }
            """);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.CompilationDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("ServiceProviderServiceExtensions.GetRequiredService<global::Clock>(context.Services)", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("ServiceProviderServiceExtensions.GetService<global::IOptional>(context.Services)", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredKeyedService<global::Clock>(context.Services, \"utc\")", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredKeyedService<global::Clock>(context.Services, (long)3)", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredKeyedService<global::Clock>(context.Services, (global::Slot)(1))", result.GeneratedSource, StringComparison.Ordinal);
        // An explicit attribute wins over type-based injection.
        Assert.Contains("GetRequiredService<global::NetWiv.AppHandle>(context.Services)", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DeserializeArgument", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[FromServices, FromKeyedServices(\"a\")] Clock clock")]
    [InlineData("[FromKeyedServices] Clock clock")]
    [InlineData("[FromKeyedServices(1.5)] Clock clock")]
    public void RejectsAmbiguousOrUnsupportedServiceParameters(string parameter)
    {
        var result = Run($$"""
            using Microsoft.Extensions.DependencyInjection;
            using NetWiv;
            public sealed class Clock { }
            public static class Commands
            {
                [Command] public static void Tick({{parameter}}) { }
            }
            """);
        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "DTOXGEN010");
    }

    [Fact]
    public void GeneratesStaticAotBindingsForTypedAsyncCommand()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using NetWiv;

            [assembly: CommandJsonContext(typeof(AppJsonContext))]

            public sealed record Request(string Name);
            public sealed record Response(string Message);
            public sealed class Counter { }
            public abstract class AppJsonContext : JsonSerializerContext
            {
                protected AppJsonContext() : base(null) { }
                public static AppJsonContext Default => throw null!;
            }

            public static class Commands
            {
                [Command]
                public static Task<Response> GetURLValue(
                    AppHandle app,
                    State<Counter> state,
                    Request request,
                    CancellationToken cancellationToken) => throw null!;

                [Command("exact.external-name")]
                internal static void Ping() { }
            }
            """;

        var result = Run(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.CompilationDiagnostics, static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("\"get_url_value\"", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("\"exact.external-name\"", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("context.State<global::Counter>()", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("DeserializeArgument<global::Request>", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("JsonTypeInfo<global::Response>()", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("ConfigureAwait(false)", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("ModuleInitializerAttribute", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertsAcronymsAndUnicodeWithoutChangingExplicitNames()
    {
        const string source = """
            using NetWiv;

            public static class Commands
            {
                [Command] public static void GetHTTPServerURL() { }
                [Command] public static void ÖffneDatei() { }
                [Command("KeepTHIS")] public static void IgnoredName() { }
            }
            """;

        var result = Run(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Contains("\"get_http_server_url\"", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("\"öffne_datei\"", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("\"KeepTHIS\"", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsDuplicateExternalNames()
    {
        const string source = """
            using NetWiv;

            public static class First { [Command("same")] public static void Run() { } }
            public static class Second { [Command("same")] public static void Run() { } }
            """;

        var result = Run(source);

        Assert.Equal(2, result.GeneratorDiagnostics.Count(static diagnostic => diagnostic.Id == "DTOXGEN003"));
        Assert.DoesNotContain("registry.Register", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginCommandsHaveTypeScopedNamesAndDoNotRegisterAsAppCommands()
    {
        var result = Run("""
            using NetWiv;
            public sealed class First : IPlugin {
                public string Name => "first";
                public void Initialize(PluginContext context) => context.RegisterGeneratedCommands<First>("get_status");
                [Command] public static void GetStatus() { }
            }
            public sealed class Second : IPlugin {
                public string Name => "second";
                public void Initialize(PluginContext context) { }
                [Command] public static void GetStatus() { }
            }
            public static class AppCommands { [Command] public static void GetStatus() { } }
            """);
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.CompilationDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GeneratedPluginCommandRegistry.Register<global::First>(\"get_status\"", result.GeneratedSource);
        Assert.Contains("GeneratedPluginCommandRegistry.Register<global::Second>(\"get_status\"", result.GeneratedSource);
        Assert.Contains("GeneratedCommandRegistry.Register(\"get_status\"", result.GeneratedSource);
    }

    [Fact]
    public void DuplicateNamesWithinOnePluginRemainErrors()
    {
        var result = Run("""
            using NetWiv;
            public sealed class Plugin : IPlugin {
                public string Name => "example";
                public void Initialize(PluginContext context) { }
                [Command("same")] public static void One() { }
                [Command("same")] public static void Two() { }
            }
            """);
        Assert.Equal(2, result.GeneratorDiagnostics.Count(d => d.Id == "DTOXGEN003"));
        Assert.DoesNotContain("GeneratedPluginCommandRegistry.Register", result.GeneratedSource);
    }

    [Fact]
    public void ReportsInvalidMethodAndMissingJsonContext()
    {
        const string source = """
            using NetWiv;

            public sealed record Input(int Value);
            public class Commands
            {
                [Command] public Input Instance(Input input) => input;
                [Command] public static Input Typed(Input input) => input;
            }
            """;

        var result = Run(source);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "DTOXGEN001");
        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "DTOXGEN004");
    }

    [Fact]
    public void ReportsCommandInInaccessibleContainingType()
    {
        const string source = """
            using NetWiv;

            public class Outer
            {
                private static class Hidden
                {
                    [Command] internal static void CannotBeCalled() { }
                }
            }
            """;

        var result = Run(source);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "DTOXGEN001");
    }

    [Fact]
    public void CompilesAllSupportedTaskAndValueTaskReturnShapes()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;
            using NetWiv;

            [assembly: CommandJsonContext(typeof(AppJsonContext))]
            public abstract class AppJsonContext : JsonSerializerContext
            {
                protected AppJsonContext() : base(null) { }
                public static AppJsonContext Default => throw null!;
            }

            public static class Commands
            {
                [Command] public static Task CompleteTask() => Task.CompletedTask;
                [Command] public static ValueTask CompleteValueTask() => ValueTask.CompletedTask;
                [Command] public static ValueTask<string> ReadValueTask() => ValueTask.FromResult("value");
                [Command] public static string ReadSync() => "value";
            }
            """;

        var result = Run(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.CompilationDiagnostics, static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsDuplicateCancellationTokenInjection()
    {
        const string source = """
            using System.Threading;
            using NetWiv;

            public static class Commands
            {
                [Command]
                public static void Invalid(CancellationToken first, CancellationToken second) { }
            }
            """;

        var result = Run(source);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "DTOXGEN007");
    }

    [Fact]
    public void GeneratesStaticPluginFactory()
    {
        const string source = """
            using NetWiv;

            [Plugin]
            public sealed class SearchPlugin : IPlugin
            {
                public string Name => "search";
                public void Initialize(PluginContext context) { }
            }
            """;

        var result = Run(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Contains("[assembly: global::NetWiv.PluginExportAttribute(typeof(global::SearchPlugin))]", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("GeneratedPluginRegistry.Register<global::SearchPlugin>(static () => new global::SearchPlugin())", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratesTypedFactoryForPluginExportedByReferencedAssembly()
    {
        const string pluginSource = """
            using NetWiv;

            namespace External;

            [Plugin]
            public sealed class SearchPlugin : IPlugin
            {
                public string Name => "search";
                public void Initialize(PluginContext context) { }
            }
            """;
        var pluginReference = CompileGeneratedReference("ExternalPlugin", pluginSource);

        var result = Run("public static class Consumer { }", pluginReference);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.CompilationDiagnostics, static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("PluginExportAttribute", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("GeneratedPluginRegistry.Register<global::External.SearchPlugin>(static () => new global::External.SearchPlugin())", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsInvalidPluginConstructor()
    {
        const string source = """
            using NetWiv;

            [Plugin]
            public sealed class InvalidPlugin : IPlugin
            {
                private InvalidPlugin() { }
                public string Name => "invalid";
                public void Initialize(PluginContext context) { }
            }
            """;

        var result = Run(source);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "DTOXGEN008");
    }

    private static GeneratorResult Run(string source, params MetadataReference[] additionalReferences)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var syntaxTrees = new[] { CSharpSyntaxTree.ParseText(source, parseOptions, "Commands.cs") };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(CommandAttribute).Assembly.Location))
            .Concat(additionalReferences);
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CommandGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        var run = driver.GetRunResult();
        var generated = string.Join("\n", run.Results.SelectMany(static value => value.GeneratedSources)
            .Select(static value => value.SourceText.ToString()));
        return new GeneratorResult(diagnostics, outputCompilation.GetDiagnostics(), generated);
    }

    private static MetadataReference CompileGeneratedReference(string assemblyName, string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(CommandAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions, "Plugin.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CommandGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics));
        using var stream = new MemoryStream();
        var emit = outputCompilation.Emit(stream);
        if (!emit.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private sealed record GeneratorResult(
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompilationDiagnostics,
        string GeneratedSource);

}
