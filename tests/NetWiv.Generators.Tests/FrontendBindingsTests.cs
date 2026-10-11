using NetWiv;
using NetWiv.Build.Frontend;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class FrontendBindingsTests
{
    private const string Preamble = """
        using System;
        using System.Text.Json.Serialization;
        using System.Threading.Tasks;
        using NetWiv;
        [assembly: GenerateFrontendBindings]
        [assembly: CommandJsonContext(typeof(Context))]
        [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
        public abstract class Context : JsonSerializerContext {
            protected Context() : base(null) { }
            public static Context Default => throw new Exception("Application must not execute during export");
        }
        """;

    [Fact]
    public void ExportsRealWireNamesInjectedParametersAndSeparateDtoDirections()
    {
        var result = Generate(Preamble + """
            public sealed record Item(string DisplayName, byte[] Payload) {
                [JsonPropertyName("exact-name")] public int Renamed { get; init; }
                [JsonIgnore] public string Secret => "hidden";
                public Item? Next { get; init; }
            }
            public sealed class Counter { }
            public static class Commands {
                [Command("account.fetch")] public static Task<Item> Get(Item request, State<Counter> counter, AppHandle app) => throw null!;
                [Command] public static byte[] Echo(byte[] data) => data;
                [Command] public static void Ping() { }
            }
            """);
        Assert.Empty(result.Errors);
        var bindings = Read(result.Compilation);
        Assert.Contains("[\"account.fetch\"]: (args, options) => invoke(\"account.fetch\", args, options)", bindings.JavaScript);
        Assert.DoesNotContain("counter", bindings.TypeScript);
        Assert.DoesNotContain("Secret", bindings.TypeScript);
        Assert.DoesNotContain("\"displayName\"?: string", bindings.TypeScript);
        Assert.Contains("\"displayName\": string", bindings.TypeScript);
        Assert.Contains("\"payload\": string", bindings.TypeScript);
        Assert.Contains("\"exact-name\": number", bindings.TypeScript);
        Assert.Contains("\"data\": Array<number>", bindings.TypeScript);
        Assert.Contains("Promise<Array<number>>", bindings.TypeScript);
        Assert.Contains("\"ping\": (args?: Record<string, never>, options?: InvokeOptions) => Promise<null>", bindings.TypeScript);
        Assert.Contains("\"next\": Item", bindings.TypeScript);
        Assert.Contains(" | null;", bindings.TypeScript);
    }

    [Fact]
    public void DtoNamesFollowClrTypesAndStayStableWhenCommandsAreAdded()
    {
        const string model = """
            public sealed record Customer(string Name, Customer? Next);
            public sealed record Unrelated(int Id);
            """;
        var original = Generate(Preamble + model + "public static class Commands { [Command] public static Customer Echo(Customer value) => value; }");
        var expanded = Generate(Preamble + model + "public static class Commands { [Command] public static Unrelated Aardvark(Unrelated value) => value; [Command] public static Customer Echo(Customer value) => value; }");
        Assert.Empty(original.Errors);
        Assert.Empty(expanded.Errors);
        var first = Read(original.Compilation).TypeScript;
        var second = Read(expanded.Compilation).TypeScript;
        foreach (var direction in new[] { "Input", "Output" })
        {
            var declaration = "export interface Customer" + direction + " {";
            Assert.Contains(declaration, first);
            Assert.Contains(declaration, second);
            Assert.Equal(first.Split(declaration)[1].Split('}')[0], second.Split(declaration)[1].Split('}')[0]);
            Assert.Matches("\"next\"\\??: Customer" + direction + " \\| null", first);
            Assert.Equal(1, first.Split(declaration).Length - 1);
        }
        Assert.DoesNotMatch(@"\bDto\d+(Input|Output)?\b", second);
    }

    [Fact]
    public void NamesDistinguishNamespacesNestedTypesAndConstructedGenerics()
    {
        var result = Generate(Preamble + """
            namespace Sales { public sealed record User(string Name); }
            namespace Support { public sealed record User(int Id); }
            public sealed record Page<T>(T Value);
            public sealed class Envelope { public sealed record Entry(string Name); }
            public static class Commands {
                [Command] public static Sales.User SalesUser(Sales.User value) => value;
                [Command] public static Support.User SupportUser(Support.User value) => value;
                [Command] public static Page<Sales.User> SalesPage(Page<Sales.User> value) => value;
                [Command] public static Page<Support.User> SupportPage(Page<Support.User> value) => value;
                [Command] public static Envelope.Entry Entry(Envelope.Entry value) => value;
            }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        foreach (var name in new[] { "Sales_User", "Support_User", "PageOfSales_User", "PageOfSupport_User", "Envelope_Entry" })
            foreach (var direction in new[] { "Input", "Output" })
                Assert.Contains("export interface " + name + direction + " {", types);
        Assert.Contains("\"value\": Sales_UserOutput", types);
        Assert.Contains("\"value\": Support_UserOutput", types);
    }

    [Fact]
    public void FlattenedNameCollisionsHaveDeterministicIdentitySuffixes()
    {
        const string models = """
            namespace A.B { public sealed record User(int Id); }
            namespace A_B { public sealed record User(string Name); }
            """;
        const string first = "[Command] public static A.B.User First(A.B.User value) => value;";
        const string second = "[Command] public static A_B.User Second(A_B.User value) => value;";
        var one = Generate(Preamble + models + "public static class Commands {" + first + second + "}");
        var two = Generate(Preamble + models + "public static class Commands {" + second + first + "}");
        Assert.Empty(one.Errors);
        Assert.Empty(two.Errors);
        var types = Read(one.Compilation).TypeScript;
        Assert.Equal(types, Read(two.Compilation).TypeScript);
        var names = System.Text.RegularExpressions.Regex.Matches(types, @"export interface (\w+) \{")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(4, names.Length);
        Assert.Equal(4, names.Distinct().Count());
        Assert.All(names, name => Assert.Matches(@"^A_B_User(Input|Output)_[0-9a-f]{64}$", name));
    }

    [Fact]
    public void PreservesDangerousAndEscapedNamesAsComputedKeys()
    {
        var result = Generate(Preamble + """
            public static class Commands {
                [Command("__proto__")] public static void Prototype() { }
                [Command("quote\"\\\n\a")] public static string Escape(string @default = "value") => @default;
            }
            """);
        Assert.Empty(result.Errors);
        var bindings = Read(result.Compilation);
        Assert.Contains("[\"__proto__\"]", bindings.JavaScript);
        Assert.Contains("quote\\\"\\\\\\u000a\\u0007", bindings.JavaScript);
        Assert.Contains("\"default\": string", bindings.TypeScript);
    }

    [Fact]
    public void InputAndOutputFollowAccessiblePropertyDirections()
    {
        var result = Generate(Preamble + """
            public sealed class Item {
                public int Computed => 42;
                public int PrivateSetter { get; private set; }
                public int Input { set {} }
                public int Both { get; set; }
                [JsonIgnore] public int Ignored { get; set; }
            }
            public static class Commands { [Command] public static Item Echo(Item input) => input; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        var input = types[types.IndexOf("export interface ItemInput", StringComparison.Ordinal)..];
        input = input[..input.IndexOf('}')];
        var output = types[types.IndexOf("export interface ItemOutput", StringComparison.Ordinal)..];
        Assert.Contains("\"input\"?: number", input);
        Assert.Contains("\"both\"?: number", input);
        Assert.DoesNotContain("computed", input);
        Assert.DoesNotContain("privateSetter", input);
        Assert.DoesNotContain("ignored", types);
        Assert.Contains("\"computed\": number", output);
        Assert.Contains("\"privateSetter\": number", output);
        Assert.DoesNotContain("\"input\"", output);
    }

    [Fact]
    public void ReadOnlyConstructorPropertiesRemainInputs()
    {
        var result = Generate(Preamble + """
            public sealed class Item {
                public Item(string name) { Name = name; }
                public string Name { get; }
                public int Computed => 42;
            }
            public static class Commands { [Command] public static void Echo(Item input) {} }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"name\": string", types);
        Assert.DoesNotContain("computed", types);
    }

    [Fact]
    public void DirectionalIgnoreConditionsMatchInputAndOutput()
    {
        var result = Generate(Preamble + """
            public sealed class Item {
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public int Input { get; set; }
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public int Output { get; set; }
                public int Both { get; set; }
            }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        var input = types.Split("export interface ItemInput {")[1].Split('}')[0];
        var output = types.Split("export interface ItemOutput {")[1].Split('}')[0];
        Assert.Contains("\"input\"?: number", input);
        Assert.DoesNotContain("\"output\"", input);
        Assert.Contains("\"output\": number", output);
        Assert.DoesNotContain("\"input\"", output);
        Assert.Contains("\"both\"", input);
        Assert.Contains("\"both\"", output);
    }

    [Theory]
    [InlineData("WhenReading", "public static void Read(Item value) {}")]
    [InlineData("WhenWriting", "public static Item Write() => throw null!;")]
    public void DirectionallyIgnoredUnsupportedTypesDoNotRequireMappings(string condition, string command)
    {
        var result = Generate(Preamble + "public sealed class Item { [JsonIgnore(Condition = JsonIgnoreCondition." +
            condition + ")] public System.Action Value { get; set; } = null!; } public static class Commands { [Command] " + command + " }");
        Assert.Empty(result.Errors);
        Assert.DoesNotContain("\"value\"?:", Read(result.Compilation).TypeScript);
    }

    [Fact]
    public void WhenReadingStillIncludesConstructorBoundInputs()
    {
        var result = Generate(Preamble + """
            public sealed record Item([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] string Name);
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        Assert.Equal(2, Read(result.Compilation).TypeScript.Split("\"name\": string;").Length - 1);
    }

    [Fact]
    public void NullablePropertyEnumConverterPreservesNullAndNamedValues()
    {
        var result = Generate(Preamble + """
            public enum Status { Ready, [JsonStringEnumMemberName("in-progress")] Running }
            public sealed class Item {
                [JsonConverter(typeof(JsonStringEnumConverter<Status>))] public Status? State { get; set; }
            }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"state\"?: \"Ready\" | \"in-progress\" | number | null;", types);
        Assert.Contains("\"state\": \"Ready\" | \"in-progress\" | number | null;", types);
    }

    [Fact]
    public void ExplicitPublicFieldsHonorJsonIncludeAndNames()
    {
        var result = Generate(Preamble + """
            public sealed class Item {
                [JsonInclude, JsonPropertyName("wire-field")] public int Included;
                public int Ignored;
                [JsonInclude] public int Property { get; set; }
            }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"wire-field\"?: number", types);
        Assert.Contains("\"wire-field\": number", types);
        Assert.DoesNotContain("\"ignored\"", types);
        Assert.Contains("\"property\"?: number", types);
    }

    [Fact]
    public void IncludedFieldsPreserveReadonlyConstructorInputsAndIgnoredFields()
    {
        var source = Preamble.Replace("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase",
            "PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, IncludeFields = true");
        var result = Generate(source + """
            public sealed class Item {
                public Item(string name) { Name = name; }
                public readonly string Name;
                public readonly int OutputOnly = 7;
                public required int Count;
                [JsonIgnore] public int Hidden;
            }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        var input = types.Split("export interface ItemInput {")[1].Split('}')[0];
        var output = types.Split("export interface ItemOutput {")[1].Split('}')[0];
        Assert.Contains("\"name\": string", input);
        Assert.Contains("\"count\": number", input);
        Assert.DoesNotContain("outputOnly", input);
        Assert.Contains("\"outputOnly\": number", output);
        Assert.DoesNotContain("\"hidden\"", types);
    }

    [Fact]
    public void IncludedNullableEnumFieldsUseTheSameConverterRulesAsProperties()
    {
        var result = Generate(Preamble + """
            public enum Status { Ready }
            public sealed class Item {
                [JsonInclude, JsonConverter(typeof(JsonStringEnumConverter<Status>))] public Status? State;
            }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        Assert.Contains("\"state\"?: \"Ready\" | number | null", Read(result.Compilation).TypeScript);
    }

    [Theory]
    [InlineData("[JsonInclude] private int Hidden;")]
    [InlineData("[JsonInclude] public int Value { get; private set; }")]
    public void NonPublicJsonIncludeRequiresAnAccessibilityMapping(string member)
    {
        var result = Generate(Preamble + "public sealed class Item { " + member +
            " } public static class Commands { [Command] public static Item Echo(Item value) => value; }");
        Assert.Contains(result.Errors, diagnostic => diagnostic.Id == "DTOXGEN009");
    }

    [Fact]
    public void IncludedFieldsCannotCollideWithProperties()
    {
        var result = Generate(Preamble + """
            public sealed class Item {
                public int Value { get; set; }
                [JsonInclude, JsonPropertyName("value")] public int Field;
            }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Id == "DTOXGEN009");
    }

    [Theory]
    [InlineData("WhenReading", false)]
    [InlineData("WhenWriting", true)]
    public void DirectionalContextDefaultsRespectPropertyOverrides(string condition, bool optionalOutput)
    {
        var source = Preamble.Replace("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase",
            "PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition." + condition);
        var result = Generate(source + """
            public sealed class Item {
                public int Value { get; set; }
                [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int Included { get; set; }
            }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        var input = types.Split("export interface ItemInput {")[1].Split('}')[0];
        var output = types.Split("export interface ItemOutput {")[1].Split('}')[0];
        Assert.Contains("\"value\"?: number", input);
        Assert.Contains(optionalOutput ? "\"value\"?: number" : "\"value\": number", output);
        Assert.Contains("\"included\"?: number", input);
        Assert.Contains("\"included\": number", output);
    }

    [Fact]
    public void UnmappedPropertyPopulationIsDiagnosed()
    {
        var result = Generate(Preamble + """
            public sealed class Item {
                [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
                public System.Collections.Generic.List<int> Values { get; } = new();
            }
            public static class Commands { [Command] public static void Echo(Item input) {} }
            """);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Id == "DTOXGEN009");
    }

    [Theory]
    [InlineData("Dictionary", "{ get; set; }", "")]
    [InlineData("IDictionary", "{ get; set; }", "")]
    [InlineData("Dictionary", "= new();", "[JsonInclude]")]
    public void ExtensionDataIsFlattenedWithoutWeakeningKnownMembers(string dictionary, string accessors, string attributes)
    {
        var result = Generate(Preamble + "public sealed class Item { public required string Name { get; set; } " +
            attributes + " [JsonExtensionData] public System.Collections.Generic." + dictionary +
            "<string, System.Text.Json.JsonElement> Extra " + accessors + " } " +
            "public static class Commands { [Command] public static Item Echo(Item input) => input; }");
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Equal(2, types.Split("[key: string]: unknown;").Length - 1);
        Assert.Equal(2, types.Split("\"name\": string;").Length - 1);
        Assert.DoesNotContain("\"extra\"", types);
        Assert.DoesNotContain(": any", types);
    }

    [Theory]
    [InlineData("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]", "{ get; set; }", true, false)]
    [InlineData("[JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]", "{ get; set; }", false, true)]
    [InlineData("", "{ get; } = new();", false, true)]
    [InlineData("[JsonIgnore]", "{ get; set; } = new();", false, false)]
    public void ExtensionDataPreservesSerializerDirections(string attributes, string accessors, bool input, bool output)
    {
        var result = Generate(Preamble + "public sealed class Item { " + attributes +
            " [JsonExtensionData] public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> Extra " + accessors +
            " } public static class Commands { [Command] public static Item Echo(Item input) => input; }");
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        var inputInterface = types[types.IndexOf("export interface ItemInput", StringComparison.Ordinal)..];
        inputInterface = inputInterface[..inputInterface.IndexOf('}')];
        var outputInterface = types[types.IndexOf("export interface ItemOutput", StringComparison.Ordinal)..];
        outputInterface = outputInterface[..outputInterface.IndexOf('}')];
        Assert.Equal(input, inputInterface.Contains("[key: string]: unknown;", StringComparison.Ordinal));
        Assert.Equal(output, outputInterface.Contains("[key: string]: unknown;", StringComparison.Ordinal));
        Assert.DoesNotContain("\"extra\"", types);
    }

    [Theory]
    [InlineData("[JsonExtensionData] public System.Collections.Generic.Dictionary<string, object> Extra { get; set; }")]
    [InlineData("[JsonExtensionData] public System.Collections.Generic.Dictionary<string, int> Extra { get; set; }")]
    [InlineData("[JsonExtensionData] public required System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> Extra { get; set; }")]
    [InlineData("[JsonExtensionData] public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> Extra { get; set; } [JsonExtensionData] public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> Other { get; set; }")]
    [InlineData("[JsonExtensionData, JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> Extra { get; set; } [JsonExtensionData, JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> Other { get; set; }")]
    public void UnsupportedExtensionContractsAreDiagnosed(string members)
    {
        var result = Generate(Preamble + "public sealed class Item { " + members +
            " } public static class Commands { [Command] public static Item Echo(Item input) => input; }");
        Assert.Contains(result.Errors, diagnostic => diagnostic.Id == "DTOXGEN009");
    }

    [Fact]
    public void IgnoredMembersDoNotRequireWireMappings()
    {
        var result = Generate(Preamble + """
            public sealed class Item {
                [JsonIgnore, JsonInclude] public System.Action IgnoredField;
                [JsonIgnore, JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
                public System.Collections.Generic.List<int> Values { get; } = new();
            }
            public static class Commands { [Command] public static Item Echo(Item input) => input; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.DoesNotContain("ignoredField", types);
        Assert.DoesNotContain("values", types);
    }

    [Fact]
    public void OutputOnlyConvertersDoNotAffectAnInputOnlyContract()
    {
        var result = Generate(Preamble + """
            public sealed class Item {
                [JsonConverter(typeof(JsonStringEnumConverter))]
                public DayOfWeek Computed => DayOfWeek.Monday;
                public int Value { get; set; }
            }
            public static class Commands { [Command] public static void Read(Item input) {} }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"value\"?: number", types);
        Assert.DoesNotContain("computed", types);
    }

    [Theory]
    [InlineData("[JsonConverter(typeof(MyConverter))]", "public sealed class MyConverter : JsonConverter<Item> { public override Item Read(ref System.Text.Json.Utf8JsonReader r, Type t, System.Text.Json.JsonSerializerOptions o) => throw null!; public override void Write(System.Text.Json.Utf8JsonWriter w, Item v, System.Text.Json.JsonSerializerOptions o) {} }")]
    [InlineData("[JsonPolymorphic]", "")]
    public void RejectsUnmappedSerializationInsteadOfEmittingFalseTypes(string attributes, string extra)
    {
        var result = Generate(Preamble + extra + attributes + """
            public sealed record Item(string Value);
            public static class Commands { [Command] public static Item Read() => throw null!; }
            """);
        Assert.Contains(result.Errors, diagnostic => diagnostic.Id == "DTOXGEN009");
        Assert.Throws<InvalidOperationException>(() => Read(result.Compilation));
    }

    [Fact]
    public void OptInDoesNotChangeCommandExposureAndMetadataIsReadWithoutExecution()
    {
        var result = Generate(Preamble + """
            public static class Commands {
                [System.Runtime.CompilerServices.ModuleInitializer]
                public static void MustNotRun() => throw new Exception("DO NOT EXECUTE");
                [Command] public static string Hello(string name) => name;
            }
            """);
        Assert.Empty(result.Errors);
        var bindings = Read(result.Compilation);
        Assert.Contains("invoke(\"hello\", args, options)", bindings.JavaScript);
        Assert.DoesNotContain("InvokeHandler", bindings.JavaScript);
        var without = Generate(Preamble.Replace("[assembly: GenerateFrontendBindings]", "") +
            "public static class Commands { [Command] public static void Ping() {} }");
        Assert.Throws<InvalidOperationException>(() => Read(without.Compilation));
    }

    [Fact]
    public void WritesUtf8WithoutTouchingUnchangedFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "netwiv-bindings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bindings = new FrontendBindings("// Generated by NetWiv. Do not edit.\n// 한글\n", "// Generated by NetWiv. Do not edit.\nexport {};\n");
            bindings.WriteTo(directory);
            var js = Path.Combine(directory, "commands.js");
            File.SetLastWriteTimeUtc(js, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var timestamp = File.GetLastWriteTimeUtc(js);
            bindings.WriteTo(directory);
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(js));
            Assert.Equal(bindings.JavaScript, File.ReadAllText(js));
            Assert.False(File.ReadAllBytes(js).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RemovingAllCommandsEmitsAnEmptyContractInsteadOfLeavingStaleExports()
    {
        var result = Generate("using NetWiv; [assembly: GenerateFrontendBindings] public class Empty { }");
        Assert.Empty(result.Errors);
        var bindings = Read(result.Compilation);
        Assert.Contains("Object.freeze({\n});", bindings.JavaScript);
        Assert.DoesNotContain("Promise<", bindings.TypeScript);
    }

    [Fact]
    public void HandwrittenDeclarationPreventsPartialOverwriteOfExistingJavascript()
    {
        var directory = Path.Combine(Path.GetTempPath(), "netwiv-bindings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var js = Path.Combine(directory, "commands.js");
            var dts = Path.Combine(directory, "commands.d.ts");
            File.WriteAllText(js, "// Generated by NetWiv. Do not edit.\n// previous");
            File.WriteAllText(dts, "// user-authored declarations");
            Assert.Throws<InvalidOperationException>(() => new FrontendBindings("new", "new").WriteTo(directory));
            Assert.Contains("previous", File.ReadAllText(js));
            Assert.Equal("// user-authored declarations", File.ReadAllText(dts));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void NamingPoliciesMatchDotNetForAcronymsUnicodeSpacesAndSurrogates()
    {
        System.Text.Json.JsonNamingPolicy?[] policies = [null, System.Text.Json.JsonNamingPolicy.CamelCase,
            System.Text.Json.JsonNamingPolicy.SnakeCaseLower, System.Text.Json.JsonNamingPolicy.SnakeCaseUpper,
            System.Text.Json.JsonNamingPolicy.KebabCaseLower, System.Text.Json.JsonNamingPolicy.KebabCaseUpper];
        var names = new List<string> { "", "URLValue", "SHA512Hash", "XMLReader", "  hello WORLD  ", "ABC???def", "ÅBCName", "ǅValue", "Test😀Value", "URL value", "A B", " ABc", "a\tB" };
        var random = new Random(91837);
        const string alphabet = "aAbBzZ09 _-?!\tÅåÄäǅǆ\u00a0\ud801\udc00";
        for (var i = 0; i < 2500; i++) names.Add(new string(Enumerable.Range(0, random.Next(1, 32)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray()));
        for (var policy = 0; policy < policies.Length; policy++)
            foreach (var name in names) Assert.Equal(policies[policy]?.ConvertName(name) ?? name, JsonWireNames.ConvertName(name, policy));
    }

    [Theory]
    [InlineData("Unspecified", "URLValue")]
    [InlineData("CamelCase", "urlValue")]
    [InlineData("SnakeCaseLower", "url_value")]
    [InlineData("SnakeCaseUpper", "URL_VALUE")]
    [InlineData("KebabCaseLower", "url-value")]
    [InlineData("KebabCaseUpper", "URL-VALUE")]
    public void EmitsEveryBuiltInJsonNamingPolicy(string policy, string expected)
    {
        var result = Generate(Preamble.Replace("JsonKnownNamingPolicy.CamelCase", "JsonKnownNamingPolicy." + policy) + """
            public sealed record Item(string URLValue, [property: JsonPropertyName("exact-name")] string Override);
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"" + expected + "\": string", types);
        Assert.Contains("\"exact-name\": string", types);
    }

    [Fact]
    public void ConditionalOmissionChangesOnlyOutputPresenceAndNeverOverridesNever()
    {
        var result = Generate(Preamble.Replace("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase", "DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull") + """
            public sealed class Item {
                public required string? Nullable { get; set; }
                public int AlwaysNumber { get; set; }
                [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int OmittedDefault { get; set; }
                [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? KeptNull { get; set; }
            }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"Nullable\": string | null", types); // Required input.
        Assert.Contains("\"Nullable\"?: string | null", types); // May be omitted on output.
        Assert.Contains("\"AlwaysNumber\": number", types);
        Assert.DoesNotContain("\"OmittedDefault\": number", types);
        Assert.Contains("\"KeptNull\": string | null", types);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StringEnumBindingsPreserveNamedValuesAndUnnamedIntegerFallback(bool contextOption)
    {
        var preamble = contextOption ? Preamble.Replace("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase", "UseStringEnumConverter = true") : Preamble;
        var result = Generate(preamble + (contextOption ? "" : "[JsonConverter(typeof(JsonStringEnumConverter<Mode>))]") + """
            public enum Mode { Ready, [JsonStringEnumMemberName("in-progress")] Running }
            [Flags] public enum Flags { One = 1, Two = 2 }
            public sealed record Item(Mode Mode);
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"Ready\" | \"in-progress\" | number", types);
    }

    [Fact]
    public void PropertyStringEnumConvertersAndFlagsAreMapped()
    {
        var result = Generate(Preamble + """
            public enum Mode { Ready, Done }
            [Flags, JsonConverter(typeof(JsonStringEnumConverter<Bits>))] public enum Bits { One = 1, Two = 2 }
            public sealed record Item([property: JsonConverter(typeof(JsonStringEnumConverter<Mode>))] Mode Mode, Bits Bits);
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"Ready\" | \"Done\" | number", types);
        Assert.Contains("\"bits\": string | number", types);
    }

    [Fact]
    public void OverridesKeepTheMostDerivedPropertyWithoutDuplicateWireNames()
    {
        var result = Generate(Preamble + """
            public class Base { public virtual string Name { get; set; } = ""; }
            public sealed class Item : Base { public override string Name { get; set; } = ""; }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Equal(2, types.Split("\"name\"").Length - 1); // Once per direction.
    }

    [Fact]
    public void IgnoredOverrideDoesNotExposeTheBasePropertyAgain()
    {
        Assert.Equal("{}", System.Text.Json.JsonSerializer.Serialize(new IgnoredDerived()));
        var result = Generate(Preamble + """
            public class Base { public virtual string Name { get; set; } = ""; }
            public sealed class Item : Base { [JsonIgnore] public override string Name { get; set; } = ""; }
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        Assert.DoesNotContain("\"name\"", Read(result.Compilation).TypeScript);
    }

    [Theory]
    [InlineData("public sealed record Item(string Name, int Count, string? Note = null, int Retry = 3);", true)]
    [InlineData("public sealed class Item { public string Name { get; } public Item(string name) => Name = name; }", true)]
    [InlineData("public sealed class Item { public string Name { get; set; } = \"\"; public Item() {} public Item(string name) => Name = name; }", false)]
    [InlineData("public struct Item { public string Name { get; set; } public Item(string name) => Name = name; }", false)]
    [InlineData("public struct Item { public string Name { get; set; } [JsonConstructor] public Item(string name) => Name = name; }", true)]
    public void ConstructorBoundInputsMatchRuntimeRequiredFields(string declaration, bool required)
    {
        var result = Generate(Preamble + declaration + "public static class Commands { [Command] public static void Take(Item value) {} }");
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains(required ? "\"name\": string" : "\"name\"?: string", types);
        if (declaration.StartsWith("public sealed record", StringComparison.Ordinal))
        {
            Assert.Contains("\"count\": number", types);
            Assert.Contains("\"note\"?: string | null", types);
            Assert.Contains("\"retry\"?: number", types);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void NumberPoliciesMapEachDirectionAndKeepTopLevelBytesNumeric(int flags)
    {
        var source = Preamble.Replace("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase",
            "PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, NumberHandling = (JsonNumberHandling)" + flags);
        var result = Generate(source + """
            public sealed record Item(int Count, double Ratio, int? Optional, System.Collections.Generic.List<double> Values);
            public static class Commands {
                [Command] public static Item Echo(Item value) => value;
                [Command] public static byte[] Bytes(byte[] value) => value;
            }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        var input = (flags & 1) != 0 ? "number | string" : "number";
        var output = (flags & 2) != 0 ? "string" : "number";
        var literals = " | \"NaN\" | \"Infinity\" | \"-Infinity\"";
        var floatInput = input + ((flags & 1) == 0 && (flags & 4) != 0 ? literals : "");
        var floatOutput = output + ((flags & 2) == 0 && (flags & 4) != 0 ? literals : "");
        Assert.Contains("\"count\": " + input + ";", types);
        Assert.Contains("\"count\": " + output + ";", types);
        Assert.Contains("\"ratio\": " + floatInput + ";", types);
        Assert.Contains("\"ratio\": " + floatOutput + ";", types);
        Assert.Contains("\"optional\"?: " + input + " | null;", types);
        Assert.Contains("\"optional\": " + output + " | null;", types);
        Assert.Contains("Array<" + floatInput + ">", types);
        Assert.Contains("Array<" + floatOutput + ">", types);
        Assert.Contains("\"value\": Array<number>", types);
        Assert.Contains("Promise<Array<number>>", types);
    }

    [Fact]
    public void NumberPropertyOverridesDoNotLeakIntoNestedObjects()
    {
        var result = Generate(Preamble + """
            public sealed record Nested(int Count);
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
            public sealed record Item(int Loose,
                [property: JsonNumberHandling(JsonNumberHandling.Strict)] int Strict,
                System.Collections.Generic.Dictionary<string, int> Values, Nested Nested);
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"loose\": number | string;", types);
        Assert.Contains("\"loose\": string;", types);
        Assert.Equal(2, types.Split("\"strict\": number;").Length - 1);
        Assert.Equal(2, types.Split("\"count\": number;").Length - 1);
        Assert.Contains("Record<string, number | string>", types);
        Assert.Contains("Record<string, string>", types);
    }

    [Theory]
    [InlineData("General", "Count", "number")]
    [InlineData("Web", "count", "number | string")]
    [InlineData("Strict", "Count", "number")]
    [InlineData("Web, NumberHandling = JsonNumberHandling.Strict", "count", "number")]
    public void JsonNumberDefaultsAndExplicitOverridesMatchContext(string preset, string name, string input)
    {
        var source = Preamble.Replace("PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase", "System.Text.Json.JsonSerializerDefaults." + preset);
        var result = Generate(source + """
            public sealed record Item(int Count);
            public static class Commands { [Command] public static Item Echo(Item value) => value; }
            """);
        Assert.Empty(result.Errors);
        var types = Read(result.Compilation).TypeScript;
        Assert.Contains("\"" + name + "\": " + input + ";", types);
        Assert.Contains("\"" + name + "\": number;", types);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("byte[]")]
    [InlineData("System.Collections.Generic.List<string>")]
    [InlineData("Item")]
    public void RejectsNumberHandlingOnPropertiesTheSerializerCannotConfigure(string type)
    {
        var result = Generate(Preamble + "public sealed class Item { [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public " +
            type + " Value { get; set; } = default!; } public static class Commands { [Command] public static Item Echo(Item value) => value; }");
        Assert.Contains(result.Errors, diagnostic => diagnostic.Id == "DTOXGEN009");
    }

    public class IgnoredBase { public virtual string Name { get; set; } = ""; }
    public sealed class IgnoredDerived : IgnoredBase { [System.Text.Json.Serialization.JsonIgnore] public override string Name { get; set; } = ""; }

    private static (Compilation Compilation, Diagnostic[] Errors) Generate(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(CommandAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create("FrontendBindingsTest",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CommandGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
        return (updated, diagnostics.Concat(updated.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray());
    }

    private static FrontendBindings Read(Compilation compilation)
    {
        var path = Path.Combine(Path.GetTempPath(), "netwiv-bindings-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            var result = compilation.Emit(path);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            return FrontendBindings.Read(path);
        }
        finally { File.Delete(path); }
    }
}
