using System.Collections.Immutable;
using NetWiv.Build.Frontend;
using NetWiv.LegacyJsonFixture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class ReferencedFrontendBindingsTests
{
    private const string Preamble = """
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using NetWiv;
        [assembly: GenerateFrontendBindings]
        [assembly: CommandJsonContext(typeof(Context))]
        [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
        internal abstract class Context : JsonSerializerContext {
            protected Context() : base(null) { }
            public static Context Default => throw new Exception("Producer code must not execute");
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsPublicCommandsFromImplementationAndReferenceAssemblies(bool referenceAssembly)
    {
        var producer = Reference("Producer", Generate("Producer", Preamble + """
            public sealed record Item(string DisplayName, int Count) {
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public string InputOnly { get; set; } = "";
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public string OutputOnly { get; set; } = "";
            }
            public sealed class Feature : IPlugin {
                string IPlugin.Name => "catalog";
                public void Initialize(PluginContext context) { }
                [Command("item.fetch")] public static Item Fetch(Item request, AppHandle app) => request;
            }
            public static class Commands { [Command("app.external")] public static Item Echo(Item value) => value; }
            """), referenceAssembly);
        var consumer = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", producer);
        Assert.Empty(consumer.Errors);
        var bindings = Read(consumer.Compilation);
        Assert.Contains("invoke(\"plugin:catalog|item.fetch\", args, options)", bindings.JavaScript);
        Assert.Contains("\"app.external\"", bindings.TypeScript);
        Assert.Contains("\"display_name\": string;", bindings.TypeScript);
        Assert.DoesNotContain("\"display_name\": string | null;", bindings.TypeScript);
        Assert.Contains("\"count\": number | string;", bindings.TypeScript);
        Assert.Contains("\"count\": string;", bindings.TypeScript);
        Assert.Contains("\"input_only\"?: string;", bindings.TypeScript);
        Assert.Contains("\"output_only\": string;", bindings.TypeScript);
        Assert.DoesNotContain("\"app\":", bindings.TypeScript);
        Assert.DoesNotContain("NetWiv.Commands.g.cs", consumer.Sources);
        var input = bindings.TypeScript.Split("export interface ItemInput")[1].Split("}")[0];
        var output = bindings.TypeScript.Split("export interface ItemOutput")[1].Split("}")[0];
        Assert.DoesNotContain("output_only", input);
        Assert.DoesNotContain("input_only", output);
    }

    [Fact]
    public void Stj8ReferencedContextEmitsTheLegacyInputContract()
    {
        var fixture = MetadataReference.CreateFromFile(typeof(LegacyJson).Assembly.Location);
        var producer = Generate("LegacyConsumer", """
            using NetWiv;
            using NetWiv.LegacyJsonFixture;
            [assembly: GenerateFrontendBindings]
            [assembly: CommandJsonContext(typeof(LegacyJson))]
            public static class Commands {
                [Command] public static LegacyRequest Echo(LegacyRequest value) => value;
                [Command] public static LegacyRequired Required(LegacyRequired value) => value;
                [Command] public static LegacyEnvelope Envelope(LegacyEnvelope value) => value;
            }
            """, fixture);
        Assert.Empty(producer.Errors);

        var types = Read(producer.Compilation).TypeScript;
        var requestInput = types.Split("export interface LegacyRequestInput")[1].Split('}')[0];
        var requestOutput = types.Split("export interface LegacyRequestOutput")[1].Split('}')[0];
        var requiredInput = types.Split("export interface LegacyRequiredInput")[1].Split('}')[0];

        Assert.Contains("\"id\"?: string | null", requestInput);
        Assert.Contains("\"note\"?: string | null", requestInput);
        Assert.Contains("\"id\": string", requestOutput);
        Assert.Contains("\"note\": string | null", requestOutput);
        Assert.Contains("\"note\": string | null", requiredInput);
        Assert.DoesNotContain("\"note\"?:", requiredInput);
        Assert.Contains("\"value\": LegacyRequestInput", types);
        Assert.DoesNotContain("\"value\": LegacyRequestInput | null", types);
        var envelopeInput = types.Split("export interface LegacyEnvelopeInput")[1].Split('}')[0];
        var envelopeOutput = types.Split("export interface LegacyEnvelopeOutput")[1].Split('}')[0];
        Assert.Contains("\"label\"?: string | null", envelopeInput);
        Assert.Contains("\"request\"?: LegacyRequestInput | null", envelopeInput);
        Assert.Contains("\"values\"?: Array<string> | null", envelopeInput);
        Assert.Contains("\"requiredNote\": string | null", envelopeInput);
        Assert.DoesNotContain("\"requiredNote\"?:", envelopeInput);
        Assert.Contains("\"label\": string;", envelopeOutput);
    }

    [Fact]
    public void ReferencedApplicationWireNamesArePreservedExactly()
    {
        var producer = Reference("Names", Generate("Names", """
            using NetWiv;
            [assembly: GenerateFrontendBindings]
            public static class Commands { [Command("exact.name\nsecond-line")] public static void Ping() { } }
            """));
        var result = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", producer);
        Assert.Empty(result.Errors);
        Assert.Contains("invoke(\"exact.name\\u000asecond-line\", args, options)", Read(result.Compilation).JavaScript);
    }

    [Fact]
    public void OnlyPublicMethodsAndContainingTypesAreExportedToConsumers()
    {
        var producer = Reference("Visibility", Generate("Visibility", Preamble + """
            public static class Commands {
                [Command] public static void Public() { }
                [Command] internal static void Internal() { }
                internal static class Hidden { [Command] public static void NestedHidden() { } }
                public static class Nested { [Command] public static void NestedPublic() { } }
            }
            internal static class HiddenCommands { [Command] public static void Hidden() { } }
            """));
        var consumer = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", producer);
        Assert.Empty(consumer.Errors);
        var js = Read(consumer.Compilation).JavaScript;
        Assert.Contains("\"public\"", js);
        Assert.Contains("\"nested_public\"", js);
        Assert.DoesNotContain("\"internal\"", js);
        Assert.DoesNotContain("\"nested_hidden\"", js);
        Assert.DoesNotContain("\"hidden\"", js);
    }

    [Fact]
    public void DependenciesAreNotReexportedAndNonOptedProducersAreIgnored()
    {
        var leaf = Reference("Leaf", Generate("Leaf", Preamble + "public static class LeafCommands { [Command] public static void Leaf() { } }"));
        var middle = Reference("Middle", Generate("Middle", "using NetWiv; [assembly: GenerateFrontendBindings] public static class MiddleCommands { [Command] public static void Middle() { } }", leaf));
        var runtimeOnly = Reference("RuntimeOnly", Generate("RuntimeOnly", "using NetWiv; public static class Commands { [Command] public static void RuntimeOnly() { } }"));
        var consumer = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", middle, runtimeOnly);
        Assert.Empty(consumer.Errors);
        var js = Read(consumer.Compilation).JavaScript;
        Assert.Contains("\"middle\"", js);
        Assert.DoesNotContain("\"leaf\"", js);
        Assert.DoesNotContain("\"runtime_only\"", js);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateRoutesAcrossLocalAndReferencedCommandsAreErrors(bool plugin)
    {
        var declaration = plugin ? "public sealed class Feature : IPlugin { public string Name => \"feature\"; public void Initialize(PluginContext context) { } [Command] public static void Ping() { } }" :
            "public static class Commands { [Command] public static void Ping() { } }";
        var producer = Reference("Duplicate", Generate("Duplicate", Preamble + declaration));
        var result = Generate("Consumer", Preamble + declaration.Replace("Feature :", "OtherFeature :").Replace("class Commands", "class OtherCommands"), producer);
        Assert.Equal(2, result.Errors.Count(error => error.Id == "DTOXGEN003"));
    }

    [Fact]
    public void DuplicateRoutesAcrossTwoReferencesAreErrors()
    {
        var declaration = Preamble + "public static class Commands { [Command] public static void Ping() { } }";
        var first = Reference("First", Generate("First", declaration));
        var second = Reference("Second", Generate("Second", declaration));
        var result = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", second, first);
        Assert.Equal(2, result.Errors.Count(error => error.Id == "DTOXGEN003"));
    }

    [Fact]
    public void EmptyLocalContractsKeepTheirExistingUnusedContextBehavior()
    {
        var unused = Preamble.Replace("NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString", "DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase");
        var producer = Generate("Empty", unused);
        Assert.Empty(producer.Errors);
        Assert.DoesNotContain("Promise<", Read(producer.Compilation).TypeScript);
        var consumer = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", Reference("Empty", producer));
        Assert.Empty(consumer.Errors);
        Assert.DoesNotContain("Promise<", Read(consumer.Compilation).TypeScript);
    }

    [Fact]
    public void SameDtoUnderDifferentJsonContextsUsesDistinctStableContracts()
    {
        var producer = Reference("Producer", Generate("Producer", Preamble + "public sealed record Item(int Count); public static class Commands { [Command] public static Item Remote(Item item) => item; }"));
        var local = Preamble.Replace("JsonKnownNamingPolicy.SnakeCaseLower", "JsonKnownNamingPolicy.CamelCase")
            .Replace("JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString", "JsonNumberHandling.Strict") +
            "public static class Commands { [Command] public static Item Local(Item item) => item; }";
        var first = Generate("Consumer", local, producer);
        var second = Generate("Consumer", local + "public static class Extra { [Command] public static void Aardvark() { } }", producer);
        Assert.Empty(first.Errors);
        Assert.Empty(second.Errors);
        var types = Read(first.Compilation).TypeScript;
        Assert.Contains("\"count\": number;", types);
        Assert.Contains("\"count\": number | string;", types);
        Assert.Contains("\"count\": string;", types);
        var interfaces = types.Split('\n').Where(line => line.StartsWith("export interface ")).ToArray();
        Assert.Equal(4, interfaces.Length);
        Assert.Equal(4, interfaces.Distinct().Count());
        Assert.Equal(interfaces, Read(second.Compilation).TypeScript.Split('\n').Where(line => line.StartsWith("export interface ")).ToArray());
    }

    [Theory]
    [InlineData(false, "2.0.0.0", "1.0.0.0")]
    [InlineData(false, "1.0.0.0", "2.0.0.0")]
    [InlineData(false, "2.0.0.0", "2.0.0.0")]
    [InlineData(true, "2.0.0.0", "1.0.0.0")]
    [InlineData(true, "1.0.0.0", "2.0.0.0")]
    [InlineData(true, "2.0.0.0", "2.0.0.0")]
    public void AssemblyVersionChangesPreserveMultiContextBindings(bool referenceAssembly, string producerVersion, string consumerVersion)
    {
        FrontendBindings Bindings(string producerAssemblyVersion, string consumerAssemblyVersion)
        {
            var producerSource = Preamble.Replace("[assembly: GenerateFrontendBindings]",
                "[assembly: GenerateFrontendBindings]\n[assembly: System.Reflection.AssemblyVersion(\"" + producerAssemblyVersion + "\")]") +
                "public sealed record Item(int Count); public static class Commands { [Command] public static Item Remote(Item item) => item; }";
            var producer = Reference("Producer", Generate("Producer", producerSource), referenceAssembly);
            var local = Preamble.Replace("[assembly: GenerateFrontendBindings]",
                    "[assembly: GenerateFrontendBindings]\n[assembly: System.Reflection.AssemblyVersion(\"" + consumerAssemblyVersion + "\")]")
                .Replace("JsonKnownNamingPolicy.SnakeCaseLower", "JsonKnownNamingPolicy.CamelCase")
                .Replace("JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString", "JsonNumberHandling.Strict") +
                "public static class Commands { [Command] public static Item Local(Item item) => item; }";
            var consumer = Generate("Consumer", local, producer);
            Assert.Empty(consumer.Errors);
            return Read(consumer.Compilation);
        }

        var original = Bindings("1.0.0.0", "1.0.0.0");
        var upgraded = Bindings(producerVersion, consumerVersion);
        Assert.Equal(4, original.TypeScript.Split('\n').Where(line => line.StartsWith("export interface ")).Distinct().Count());
        Assert.Equal(original.TypeScript, upgraded.TypeScript);
        Assert.Equal(original.JavaScript, upgraded.JavaScript);
    }

    [Theory]
    [InlineData("NetWiv.Frontend.Commands.v2", "1", "", "unsupported")]
    [InlineData("NetWiv.Frontend.Commands.v1", "2", "", "marker")]
    [InlineData("NetWiv.Frontend.Commands.v1", "1", "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v1:M:Commands.Ping\", \"invalid\")]", "malformed")]
    [InlineData("NetWiv.Frontend.Commands.v1", "1", "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v1:M:Commands.Ping\", \"Commands\\nwrong\")]", "route differs")]
    [InlineData("NetWiv.Frontend.Commands.v1", "1", "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v1:M:Commands.Internal\", \"Commands\\ninternal\")]", "public accessible")]
    [InlineData("NetWiv.Frontend.Commands.v1", "1", "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Commands.v1\", \"2\")]", "duplicate")]
    [InlineData("NetWiv.Frontend.Commands.v1", "1", "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v2:M:Commands.Ping\", \"Commands\\nping\")]", "unsupported")]
    [InlineData("NetWiv.Frontend.Commands.v1", "1", "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v1:M:Commands.Missing\", \"Commands\\nmissing\")]", "public accessible")]
    [InlineData("NetWiv.Frontend.Commands.v1", "1", "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v1:M:Commands.Plain\", \"Commands\\nplain\")]", "no Command attribute")]
    [InlineData("NetWiv.Frontend.Commands.v1", "1", "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v1:M:Commands.Ping\", \"Commands\\nping\")][assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v1:M:Commands.Ping\", \"Commands\\nwrong\")]", "duplicate")]
    public void MalformedOrUnsupportedReferenceMetadataFailsClearly(string marker, string version, string metadata, string reason)
    {
        var producer = Compile("Invalid", $$"""
            using NetWiv;
            [assembly: GenerateFrontendBindings]
            [assembly: System.Reflection.AssemblyMetadata("{{marker}}", "{{version}}")]
            {{metadata}}
            public static class Commands { [Command] public static void Ping() { } [Command] internal static void Internal() { } public static void Plain() { } }
            """);
        var result = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", Reference("Invalid", (producer, [], [])));
        var error = Assert.Single(result.Errors);
        Assert.Equal("DTOXGEN013", error.Id);
        Assert.Contains(reason, error.GetMessage());
    }

    [Fact]
    public void OldProducerMetadataRequiresARebuild()
    {
        var producer = Compile("Old", "using NetWiv; [assembly: GenerateFrontendBindings]");
        var result = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", Reference("Old", (producer, [], [])));
        Assert.Contains(result.Errors, error => error.Id == "DTOXGEN013");
    }

    [Theory]
    [InlineData("public virtual string Name => \"feature\";", "plugin:feature|ping")]
    [InlineData("public string Name => \"feature\";", "ping")]
    [InlineData("public string Name => \"feature\";", "plugin:|ping")]
    [InlineData("public string Name => \"feature\";", "plugin:feature|wrong")]
    public void ReferencedPluginMetadataMustMatchThePluginShape(string property, string route)
    {
        var producer = Compile("InvalidPlugin", $$"""
            using NetWiv;
            [assembly: GenerateFrontendBindings]
            [assembly: System.Reflection.AssemblyMetadata("NetWiv.Frontend.Commands.v1", "1")]
            [assembly: System.Reflection.AssemblyMetadata("NetWiv.Frontend.Command.v1:M:Feature.Ping", "Feature\n{{route}}")]
            public class Feature : IPlugin { {{property}} public void Initialize(PluginContext context) { } [Command] public static void Ping() { } }
            """);
        var result = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", Reference("InvalidPlugin", (producer, [], [])));
        Assert.Contains(result.Errors, error => error.Id == "DTOXGEN013");
    }

    [Fact]
    public void UnsupportedReferencedJsonMetadataDoesNotProduceAFalseContract()
    {
        var metadata = """
            [assembly: System.Reflection.AssemblyMetadata("NetWiv.Frontend.Commands.v1", "1")]
            [assembly: System.Reflection.AssemblyMetadata("NetWiv.Frontend.Command.v1:M:Commands.Ping", "Commands\nping")]
            """;
        var producer = Compile("Unsupported", Preamble.Replace("NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString", "DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase")
            .Replace("[JsonSourceGenerationOptions", metadata + "\n[JsonSourceGenerationOptions") +
            "public static class Commands { [Command] public static void Ping() { } }");
        var result = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", Reference("Unsupported", (producer, [], [])));
        Assert.Contains(result.Errors, error => error.Id == "DTOXGEN009");
        Assert.DoesNotContain("NetWiv.FrontendBindings.g.cs", result.Sources);
    }

    private static CSharpCompilation Compile(string name, string source, params MetadataReference[] extra)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(typeof(CommandAttribute).Assembly.Location)).Concat(extra);
        return CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static (Compilation Compilation, Diagnostic[] Errors, string[] Sources) Generate(string name, string source, params MetadataReference[] references)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CommandGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(Compile(name, source, references), out var updated, out var diagnostics);
        return (updated, diagnostics.Concat(updated.GetDiagnostics()).Where(error => error.Severity == DiagnosticSeverity.Error).ToArray(),
            driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Select(source => source.HintName).ToArray());
    }

    private static MetadataReference Reference(string name, (Compilation Compilation, Diagnostic[] Errors, string[] Sources) result, bool referenceAssembly = false)
    {
        Assert.Empty(result.Errors);
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream, options: new EmitOptions(metadataOnly: referenceAssembly, includePrivateMembers: !referenceAssembly));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return MetadataReference.CreateFromImage(ImmutableArray.CreateRange(stream.ToArray()), filePath: name + ".dll");
    }

    private static FrontendBindings Read(Compilation compilation)
    {
        var path = Path.Combine(Path.GetTempPath(), "netwiv-reference-bindings-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            var emitted = compilation.Emit(path);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            return FrontendBindings.Read(path);
        }
        finally { File.Delete(path); }
    }
}
