using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using NetWiv;

namespace NetWiv.Generators.Tests;

public sealed class CommandGeneratorTests
{
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
