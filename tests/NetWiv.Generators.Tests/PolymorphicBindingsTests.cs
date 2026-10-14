using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NetWiv;
using NetWiv.Build.Frontend;
using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class PolymorphicBindingsTests
{
    private const string Preamble = """
        using System;
        using System.Text.Json.Serialization;
        using NetWiv;
        [assembly: GenerateFrontendBindings]
        [assembly: CommandJsonContext(typeof(Context))]
        [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
        public abstract class Context : JsonSerializerContext {
            protected Context() : base(null) { }
            public static Context Default => throw new Exception("Export must not execute code");
        }
        """;

    private const string Contract = """
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
        [JsonDerivedType(typeof(TextItem), "text\"item")]
        [JsonDerivedType(typeof(NumberItem), -7)]
        public abstract class Item { public string? Note { get; set; } }
        public sealed class TextItem : Item {
            public required string Text { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public int InputOnly { get; set; }
            public Item? Next { get; set; }
        }
        public sealed class NumberItem : Item { public required int Count { get; set; } }
        public static class Commands { [Command] public static Item? Echo(Item? value) => value; }
        """;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LocalAndReferencedContractsRetainTaggedDirectionalTypes(bool referenced)
    {
        var producer = Generate("Producer", Preamble + Contract);
        Assert.Empty(producer.Errors);
        var result = referenced ? Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", Reference(producer.Compilation)) : producer;
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("export type ItemInput = (TextItemInput & { \"kind\": \"text\\\"item\" }) | (NumberItemInput & { \"kind\": -7 });", types);
        Assert.Contains("export type ItemOutput = (TextItemOutput & { \"kind\": \"text\\\"item\" }) | (NumberItemOutput & { \"kind\": -7 });", types);
        Assert.Contains("\"value\": ItemInput | null", types);
        Assert.Contains("Promise<ItemOutput | null>", types);
        Assert.Contains("\"text\": string;", types);
        Assert.Contains("\"next\"?: ItemInput | null;", types);
        Assert.Contains("\"next\": ItemOutput | null;", types);
        Assert.Equal(1, types.Split("\"inputOnly\"?: number;").Length - 1);
        if (referenced) Assert.Equal(Read(producer.Compilation).TypeScript, types);
    }

    [Fact]
    public void InterfacesAndDefaultDiscriminatorAreMapped()
    {
        var result = Generate("Interface", Preamble + """
            [JsonPolymorphic, JsonDerivedType(typeof(Entry), 1)] public interface IItem { }
            public sealed record Entry(string Value) : IItem;
            public static class Commands { [Command] public static IItem Echo(IItem value) => value; }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains("export type IItemInput = (EntryInput & { \"$type\": 1 });", Read(result.Compilation).TypeScript);
    }

    [Fact]
    public void ExplicitStrictDefaultsAndNumericStringTagsAreDistinct()
    {
        var result = Generate("Strict", Preamble + """
            [JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = false, UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
            [JsonDerivedType(typeof(StringItem), "1"), JsonDerivedType(typeof(IntegerItem), 1)]
            public abstract class Item { }
            public sealed class StringItem : Item { }
            public sealed class IntegerItem : Item { }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains("(StringItemInput & { \"$type\": \"1\" }) | (IntegerItemInput & { \"$type\": 1 })", Read(result.Compilation).TypeScript);
    }

    public static IEnumerable<object[]> EnumerableContracts()
    {
        const string asyncEnumerator = "public System.Collections.Generic.IAsyncEnumerator<int> GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default) => throw null!;";
        foreach (var (root, variant) in new[] {
            ("public abstract class Item : System.Collections.Generic.List<int> {}", "public sealed class Derived : Item {}"),
            ("public abstract class Item : System.Collections.Generic.Dictionary<string, int> {}", "public sealed class Derived : Item {}"),
            ("public interface Item : System.Collections.Generic.IEnumerable<int> {}", "public sealed class Derived : System.Collections.Generic.List<int>, Item {}"),
            ("public interface Item : System.Collections.Generic.IAsyncEnumerable<int> {}", "public sealed class Derived : Item { " + asyncEnumerator + " }"),
            ("public abstract class Item : System.Collections.Generic.IAsyncEnumerable<int> { public abstract System.Collections.Generic.IAsyncEnumerator<int> GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default); }", "public sealed class Derived : Item { " + asyncEnumerator.Replace("public ", "public override ") + " }"),
            ("public interface Item {}", "public sealed class Derived : System.Collections.Generic.List<int>, Item {}"),
            ("public interface Item {}", "public sealed class Derived : System.Collections.Generic.Dictionary<string, int>, Item {}"),
            ("public abstract class Item {}", "public sealed class Derived : Item, System.Collections.Generic.IAsyncEnumerable<int> { " + asyncEnumerator + " }")
        })
            foreach (var input in new[] { true, false }) yield return [root, variant, input];
    }

    [Theory]
    [MemberData(nameof(EnumerableContracts))]
    public void EnumerableRootsAndVariantsAreRejectedForBothDirections(string root, string variant, bool input)
    {
        var command = input ? "[Command] public static void Accept(Item value) {}" : "[Command] public static Item Read() => throw null!;";
        var result = Generate("Enumerable", Preamble + "[JsonPolymorphic, JsonDerivedType(typeof(Derived), \"items\")] " + root + variant +
            "public static class Commands { " + command + " }");
        var error = Assert.Single(result.Errors, error => error.Id == "DTOXGEN009");
        Assert.Contains("enumerable polymorphic", error.GetMessage());
        Assert.DoesNotContain("NetWiv.FrontendBindings.g.cs", result.Sources);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ReferencedCollectionVariantsKeepTheUnsupportedDiagnostic(bool input)
    {
        var index = input ? "M:Commands.Accept(Item)" : "M:Commands.Read";
        var command = input ? "[Command] public static void Accept(Item value) {}" : "[Command] public static Item Read() => throw null!;";
        var route = input ? "accept" : "read";
        var source = Preamble.Replace("[assembly: GenerateFrontendBindings]", "[assembly: GenerateFrontendBindings] " +
            "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Commands.v1\", \"1\")] " +
            "[assembly: System.Reflection.AssemblyMetadata(\"NetWiv.Frontend.Command.v1:" + index + "\", \"Commands\\n" + route + "\")]") + """
            [JsonPolymorphic, JsonDerivedType(typeof(Derived), "items")] public interface Item {}
            public sealed class Derived : System.Collections.Generic.List<int>, Item {}
            """ + "public static class Commands { " + command + " }";
        var result = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", Reference(Compile("Producer", source)));
        Assert.Contains(result.Errors, error => error.Id == "DTOXGEN009" && error.GetMessage().Contains("enumerable polymorphic"));
        Assert.DoesNotContain("NetWiv.FrontendBindings.g.cs", result.Sources);
    }

    [Theory]
    [InlineData("[JsonPolymorphic]", "public abstract class Item", "public sealed class Derived : Item {}", "at least one")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived))]", "public abstract class Item", "public sealed class Derived : Item {}", "explicit string or integer")]
    [InlineData("[JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public sealed class Derived : Item {}", "abstract class or interface")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1)]", "public class Item", "public sealed class Derived : Item {}", "abstract class or interface")]
    [InlineData("[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true), JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public sealed class Derived : Item {}", "fallback")]
    [InlineData("[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType), JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public sealed class Derived : Item {}", "fallback")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public class Derived : Item {}", "sealed concrete")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public sealed class Derived {}", "not derived")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1), JsonDerivedType(typeof(Other), 1)]", "public abstract class Item", "public sealed class Derived : Item {} public sealed class Other : Item {}", "duplicate")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1), JsonDerivedType(typeof(Derived), 2)]", "public abstract class Item", "public sealed class Derived : Item {}", "duplicate")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public sealed class Derived : Item { [JsonPropertyName(\"$type\")] public string Tag { get; set; } = \"\"; }", "colliding")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public sealed class Derived : Item { [JsonIgnore, JsonPropertyName(\"$type\")] public string Ignored { get; set; } = \"\"; }", "colliding")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "[JsonPolymorphic] public sealed class Derived : Item {}", "nested polymorphism")]
    [InlineData("[JsonPolymorphic(TypeDiscriminatorPropertyName = \"$ref\"), JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public sealed class Derived : Item {}", "reserved")]
    [InlineData("[JsonPolymorphic, JsonDerivedType(typeof(Derived), 1)]", "public abstract class Item", "public sealed class Derived : Item { [JsonExtensionData] public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; } }", "extension data")]
    public void UnsupportedContractsFailWithoutPublishingBindings(string attributes, string root, string derived, string reason)
    {
        var source = Preamble + attributes + root + " {} " + derived + " public static class Commands { [Command] public static Item Echo(Item value) => value; }";
        var result = Generate("Unsupported", source);
        var error = Assert.Single(result.Errors, error => error.Id == "DTOXGEN009");
        Assert.Contains(reason, error.GetMessage());
        Assert.DoesNotContain("NetWiv.FrontendBindings.g.cs", result.Sources);
    }

    [Fact]
    public void UnmappedReferencedPolymorphismKeepsItsDiagnostic()
    {
        // An older producer can compile a command index without mapping its wire type.
        var source = Preamble.Replace("[assembly: GenerateFrontendBindings]", """
            [assembly: GenerateFrontendBindings]
            [assembly: System.Reflection.AssemblyMetadata("NetWiv.Frontend.Commands.v1", "1")]
            [assembly: System.Reflection.AssemblyMetadata("NetWiv.Frontend.Command.v1:M:Commands.Echo(Item)", "Commands\necho")]
            """) + Contract.Replace("TypeDiscriminatorPropertyName = \"kind\"", "IgnoreUnrecognizedTypeDiscriminators = true");
        var producer = Compile("UnsupportedProducer", source);
        var result = Generate("Consumer", "using NetWiv; [assembly: GenerateFrontendBindings]", Reference(producer));
        Assert.Contains(result.Errors, error => error.Id == "DTOXGEN009");
        Assert.DoesNotContain("NetWiv.FrontendBindings.g.cs", result.Sources);
    }

    private static (Compilation Compilation, Diagnostic[] Errors, string[] Sources) Generate(string name, string source, params MetadataReference[] extra)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CommandGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(Compile(name, source, extra), out var updated, out var diagnostics);
        return (updated, diagnostics.Concat(updated.GetDiagnostics()).Where(error => error.Severity == DiagnosticSeverity.Error).ToArray(),
            driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Select(source => source.HintName).ToArray());
    }

    private static CSharpCompilation Compile(string name, string source, params MetadataReference[] extra)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(typeof(CommandAttribute).Assembly.Location)).Concat(extra);
        return CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static MetadataReference Reference(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return MetadataReference.CreateFromImage(ImmutableArray.CreateRange(stream.ToArray()), filePath: "Producer.dll");
    }

    private static FrontendBindings Read(Compilation compilation)
    {
        var path = Path.Combine(Path.GetTempPath(), "netwiv-polymorphic-bindings-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            var emitted = compilation.Emit(path);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            return FrontendBindings.Read(path);
        }
        finally { File.Delete(path); }
    }
}
