using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NetWiv.Generators;

internal sealed class FrontendCommand
{
    public FrontendCommand(string name, IParameterSymbol[] parameters, ITypeSymbol? result, IMethodSymbol method, INamedTypeSymbol? jsonContext)
        => (Name, Parameters, Result, Method, JsonContext) = (name, parameters, result, method, jsonContext);
    public string Name { get; }
    public IParameterSymbol[] Parameters { get; }
    public ITypeSymbol? Result { get; }
    public IMethodSymbol Method { get; }
    public INamedTypeSymbol? JsonContext { get; }
}

internal sealed class FrontendBindingEmitter
{
    private static readonly DiagnosticDescriptor Unsupported = new(
        "DTOXGEN009", "Frontend wire type cannot be inferred",
        "Cannot generate frontend binding for '{0}': {1}",
        "NetWiv.Generators", DiagnosticSeverity.Error, true);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _preferredNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _qualifiedNames = new(StringComparer.Ordinal);
    private Dictionary<string, string>? _resolvedNames;
    private readonly SortedDictionary<string, string> _declarations = new(StringComparer.Ordinal);
    private readonly int _namingPolicy;
    private readonly int _defaultIgnoreCondition;
    private readonly bool _stringEnums;
    private readonly int _numberHandling;
    private readonly bool _includeFields;
    private readonly string _contextIdentity;

    private FrontendBindingEmitter(INamedTypeSymbol? context)
    {
        _contextIdentity = ContextIdentity(context);
        var options = Attribute(context, "JsonSourceGenerationOptionsAttribute");
        if (options?.ConstructorArguments.FirstOrDefault().Value is { } defaults)
        {
            var preset = Convert.ToInt32(defaults, CultureInfo.InvariantCulture);
            if (preset == 1) { _namingPolicy = 1; _numberHandling = 1; } // JsonSerializerDefaults.Web
            else if (preset is not (0 or 2)) throw new NotSupportedException("Unknown JSON serializer defaults");
        }
        foreach (var option in options?.NamedArguments ?? System.Collections.Immutable.ImmutableArray<KeyValuePair<string, TypedConstant>>.Empty)
        {
            // Unsupported custom serialization must never silently become `any`.
            if (option.Key == "PropertyNamingPolicy")
            {
                var policy = Convert.ToInt32(option.Value.Value, CultureInfo.InvariantCulture);
                if (policy < 0 || policy > 5) throw new NotSupportedException("Unknown JSON naming policy " + policy);
                _namingPolicy = policy;
            }
            else if (option.Key == "UseStringEnumConverter") _stringEnums = option.Value.Value is true;
            else if (option.Key == "NumberHandling") _numberHandling = ReadNumberHandling(option.Value.Value);
            else if (option.Key == "IncludeFields") _includeFields = option.Value.Value is true;
            else if (option.Key == "DefaultIgnoreCondition")
            {
                _defaultIgnoreCondition = Convert.ToInt32(option.Value.Value, CultureInfo.InvariantCulture);
                if (_defaultIgnoreCondition is not (0 or 2 or 3 or 4 or 5)) throw new NotSupportedException("Invalid default JSON ignore condition");
            }
            else if (option.Key is not ("GenerationMode" or "WriteIndented" or "PropertyNameCaseInsensitive"))
                throw new NotSupportedException("JSON context option " + option.Key + " needs an explicit wire mapping");
        }
    }

    internal static void Emit(SourceProductionContext output, INamedTypeSymbol? context, IEnumerable<FrontendCommand> commands)
    {
        var ordered = commands.OrderBy(static command => command.Name, StringComparer.Ordinal).ToArray();
        try
        {
            var emitters = new Dictionary<string, FrontendBindingEmitter>(StringComparer.Ordinal);
            FrontendBindingEmitter ForCommand(FrontendCommand command)
            {
                var identity = ContextIdentity(command.JsonContext);
                if (!emitters.TryGetValue(identity, out var result))
                    emitters.Add(identity, result = new FrontendBindingEmitter(command.JsonContext));
                return result;
            }
            // Validate even an empty local contract's serializer context.
            emitters.Add(ContextIdentity(context), new FrontendBindingEmitter(context));
            // Discover the complete wire graph before naming it. A later command must not
            // rename an earlier DTO according to traversal order, including recursive DTOs.
            foreach (var command in ordered)
            {
                try
                {
                    var emitter = ForCommand(command);
                    foreach (var parameter in command.Parameters) emitter.Type(parameter.Type, true, true);
                    if (command.Result is not null) emitter.Type(command.Result, false, true);
                }
                catch (NotSupportedException exception)
                {
                    output.ReportDiagnostic(Diagnostic.Create(Unsupported, command.Method.Locations.FirstOrDefault(), command.Name, exception.Message));
                    return;
                }
            }
            var preferred = emitters.Values.SelectMany(emitter => emitter._preferredNames).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var qualified = emitters.Values.SelectMany(emitter => emitter._qualifiedNames).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var emitter in emitters.Values) emitter.ResolveNames(preferred, qualified);
            var js = new StringBuilder("// Generated by NetWiv. Do not edit.\nimport { invoke } from '@tauri-apps/api/core';\n\nexport const commands = Object.freeze({\n");
            var types = new StringBuilder("// Generated by NetWiv. Do not edit.\nimport type { Channel, InvokeOptions } from '@tauri-apps/api/core';\n\nexport declare const commands: {\n");
            foreach (var command in ordered)
            {
                try
                {
                    var emitter = ForCommand(command);
                    var args = command.Parameters.Length == 0 ? "Record<string, never>" : "{ " + string.Join("; ", command.Parameters.Select(parameter =>
                        Quote(parameter.Name) + ": " + emitter.Type(parameter.Type, true, true))) + " }";
                    var result = command.Result is null ? "null" : emitter.Type(command.Result, false, true);
                    // Computed keys keep __proto__ an ordinary own property, and preserve exact command names.
                    js.Append("  [").Append(Quote(command.Name)).Append("]: (args")
                        .Append(command.Parameters.Length == 0 ? " = {}" : "")
                        .Append(", options) => invoke(").Append(Quote(command.Name)).AppendLine(", args, options),");
                    types.Append("  readonly ").Append(Quote(command.Name)).Append(": (args")
                        .Append(command.Parameters.Length == 0 ? "?" : "").Append(": ").Append(args)
                        .Append(", options?: InvokeOptions) => Promise<").Append(result).AppendLine(">;");
                }
                catch (NotSupportedException exception)
                {
                    output.ReportDiagnostic(Diagnostic.Create(Unsupported, command.Method.Locations.FirstOrDefault(), command.Name, exception.Message));
                    return;
                }
            }
            js.AppendLine("});");
            types.AppendLine("};");
            foreach (var declaration in emitters.Values.SelectMany(emitter => emitter._declarations)
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal)) types.AppendLine().AppendLine(declaration.Value);
            output.AddSource("NetWiv.FrontendBindings.g.cs", SourceText.From(
                "// <auto-generated />\n" +
                "[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"NetWiv.Frontend.JavaScript.v1\", " + Quote(js.ToString()) + ")]\n" +
                "[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"NetWiv.Frontend.TypeScript.v1\", " + Quote(types.ToString()) + ")]\n", Encoding.UTF8));
        }
        catch (NotSupportedException exception)
        {
            output.ReportDiagnostic(Diagnostic.Create(Unsupported, context?.Locations.FirstOrDefault(), "JSON context", exception.Message));
        }
    }

    private static string ContextIdentity(INamedTypeSymbol? context)
        => context is null ? "" : context.ContainingAssembly.Identity + ":" + context.ToDisplayString();

    private void ResolveNames(Dictionary<string, string> preferred, Dictionary<string, string> qualified)
    {
        var collisions = new HashSet<string>(preferred.Values.GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.Ordinal);
        var resolved = preferred.ToDictionary(pair => pair.Key,
            pair => collisions.Contains(pair.Value) ? qualified[pair.Key] : pair.Value, StringComparer.Ordinal);
        // Flattened namespaces/generics can themselves collide (A.B_C versus A_B.C).
        // Only those ambiguous names need an identity suffix, never an ordinal.
        foreach (var group in resolved.GroupBy(pair => pair.Value, StringComparer.Ordinal).Where(group => group.Count() > 1).ToArray())
        {
            var identities = group.Select(pair => pair.Key.Split('\0')[0]).ToArray();
            foreach (var pair in group)
            {
                // Preserve existing local hashes unless one CLR DTO needs multiple JSON contracts.
                var identity = pair.Key.Split('\0')[0];
                if (identities.Count(value => value == identity) > 1) identity = pair.Key;
                using var hash = SHA256.Create();
                var suffix = string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(identity)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
                resolved[pair.Key] = pair.Value + "_" + suffix;
            }
        }
        _resolvedNames = resolved;
        _names.Clear();
        _declarations.Clear();
    }

    private static string TypeName(ITypeSymbol type, bool qualified, bool argument = false)
    {
        string name;
        if (type is IArrayTypeSymbol array)
            name = "ArrayOf" + TypeName(array.ElementType, qualified, true);
        else if (type is INamedTypeSymbol named)
        {
            name = named.Name;
            if (named.TypeArguments.Length != 0)
                name += "Of" + string.Join("And", named.TypeArguments.Select(t => TypeName(t, qualified, true)));
            if (named.ContainingType is { } parent) name = TypeName(parent, qualified) + "_" + name;
            else if (qualified && !named.ContainingNamespace.IsGlobalNamespace)
                name = named.ContainingNamespace.ToDisplayString().Replace('.', '_') + "_" + name;
        }
        else name = type.Name;
        if (argument && type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated) name = "Nullable" + name;
        // C# permits some format characters that cannot occur in TS identifiers.
        return string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ||
            char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark ? c : '_'));
    }

    private string Type(ITypeSymbol type, bool input, bool topLevel = false, int? numberHandling = null)
    {
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return Type(nullable.TypeArguments[0], input, topLevel, numberHandling) + " | null";
        var result = NonNullableType(type, input, topLevel, numberHandling);
        return type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated ? result + " | null" : result;
    }

    private string NonNullableType(ITypeSymbol type, bool input, bool topLevel, int? numberHandling)
    {
        var handling = numberHandling ?? NumberHandling(type) ?? _numberHandling;
        var converter = Attribute(type, "JsonConverterAttribute");
        if (converter is not null && !(type.TypeKind == TypeKind.Enum && IsStringEnumConverter(converter, type)))
            throw new NotSupportedException(type.Name + " uses a custom JSON converter");
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Char: return "string";
            case SpecialType.System_Boolean: return "boolean";
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal: return NumberType(type, input, handling);
        }
        var fullName = type.ToDisplayString().TrimEnd('?');
        if (fullName is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan" or "System.Uri") return "string";
        if (fullName == "System.Text.Json.JsonElement") return "unknown";
        if (fullName == "NetWiv.Channel" && input && topLevel) return "Channel<unknown>";
        if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1) throw new NotSupportedException("multidimensional arrays have no default System.Text.Json wire format");
            // NetWiv has a special command-level byte[] array codec. Nested byte[] uses STJ base64.
            if (array.ElementType.SpecialType == SpecialType.System_Byte) return topLevel ? "Array<number>" : "string";
            return "Array<" + Type(array.ElementType, input, numberHandling: handling) + ">";
        }
        if (type.TypeKind == TypeKind.Enum) return _stringEnums || converter is not null ? StringEnum(type) : "number";
        if (type is not INamedTypeSymbol named) throw new NotSupportedException("unsupported wire type " + fullName);
        if (named.IsGenericType)
        {
            var definition = named.OriginalDefinition.ToDisplayString();
            if (definition == "NetWiv.Channel<T>" && input && topLevel) return "Channel<" + Type(named.TypeArguments[0], false) + ">";
            if (definition is "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>" or "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.ICollection<T>" or "System.Collections.Generic.IReadOnlyCollection<T>" or "System.Collections.Generic.HashSet<T>")
                return "Array<" + Type(named.TypeArguments[0], input, numberHandling: handling) + ">";
            if (definition is "System.Collections.Generic.Dictionary<TKey, TValue>" or "System.Collections.Generic.IDictionary<TKey, TValue>" or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                if (named.TypeArguments[0].SpecialType != SpecialType.System_String)
                    throw new NotSupportedException("dictionary keys other than string need a JSON property-name mapping");
                return "Record<string, " + Type(named.TypeArguments[1], input, numberHandling: handling) + ">";
            }
        }
        var ns = named.ContainingNamespace.ToDisplayString();
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || named.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            throw new NotSupportedException("unsupported wire type " + fullName);
        return ObjectType(named, input);
    }

    private string ObjectType(INamedTypeSymbol type, bool input)
    {
        var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        var key = type.ContainingAssembly.Identity.Name + ":" + type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(format) +
            (input ? ":input" : ":output") + "\0" + _contextIdentity;
        if (_names.TryGetValue(key, out var existing)) return existing;
        if (type.GetAttributes().Any(attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Text.Json.Serialization" &&
                attribute.AttributeClass.Name != "JsonNumberHandlingAttribute"))
            throw new NotSupportedException(type.Name + " has type-level JSON attributes that need an explicit wire mapping");
        var direction = input ? "Input" : "Output";
        var name = _resolvedNames is null ? "Dto" + _names.Count.ToString(CultureInfo.InvariantCulture) : _resolvedNames[key];
        _preferredNames[key] = TypeName(type, false) + direction;
        _qualifiedNames[key] = TypeName(type, true) + direction;
        _names.Add(key, name);
        var members = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var constructor = input ? DeserializationConstructor(type) : null;
        var selectedMembers = new Dictionary<string, ISymbol>(StringComparer.Ordinal);
        ISymbol? extensionMember = null;
        var extensionData = false;
        var ignoredOverrides = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member.IsStatic) continue;
                if (ignoredOverrides.Contains(member)) continue;
                var memberType = JsonMemberType(member);
                if (memberType is null) continue;
                var ignore = Attribute(member, "JsonIgnoreAttribute");
                var condition = ignore?.NamedArguments.FirstOrDefault(pair => pair.Key == "Condition").Value.Value;
                // Context directional defaults differ from property attributes in STJ: reads remain
                // enabled, and default WhenWriting omission depends on the serializer execution path.
                var ignoreCondition = ignore is null ? _defaultIgnoreCondition >= 4 ? 0 : _defaultIgnoreCondition
                    : condition is null ? 1 : Convert.ToInt32(condition, CultureInfo.InvariantCulture);
                // STJ still binds constructor parameters when a property ignores setter-based reads.
                var constructorBound = ConstructorParameter(constructor, member) is not null;
                var extension = Attribute(member, "JsonExtensionDataAttribute") is not null;
                if (extension && ignoreCondition != 1 && member.DeclaredAccessibility == Accessibility.Public &&
                    (member is IPropertySymbol { IsIndexer: false } || member is IFieldSymbol &&
                        (Attribute(member, "JsonIncludeAttribute") is not null || _includeFields)))
                {
                    if (extensionMember is not null)
                        throw new NotSupportedException(type.Name + " has multiple JSON extension data members");
                    extensionMember = member;
                }
                if (ignoreCondition == 1 || !input && ignoreCondition == 4 || input && ignoreCondition == 5 && !constructorBound)
                {
                    if (member is IPropertySymbol ignored)
                        for (var parent = ignored.OverriddenProperty; parent is not null; parent = parent.OverriddenProperty) ignoredOverrides.Add(parent);
                    continue;
                }
                var included = Attribute(member, "JsonIncludeAttribute") is not null;
                if (member.DeclaredAccessibility != Accessibility.Public)
                {
                    if (included) throw new NotSupportedException(type.Name + "." + member.Name + " non-public JsonInclude needs an explicit accessibility mapping");
                    continue;
                }
                if (member is IFieldSymbol && !included && !_includeFields) continue;
                if (member is IPropertySymbol { IsIndexer: true }) continue;
                var memberConverter = Attribute(member, "JsonConverterAttribute");
                var enumType = memberType;
                var nullableEnum = enumType is INamedTypeSymbol nullableType && nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                if (nullableEnum) enumType = ((INamedTypeSymbol)enumType!).TypeArguments[0];
                var stringEnumMember = enumType?.TypeKind == TypeKind.Enum && memberConverter is not null && IsStringEnumConverter(memberConverter, enumType);
                if (Attribute(member, "JsonObjectCreationHandlingAttribute") is not null)
                    throw new NotSupportedException(type.Name + "." + member.Name + " custom JSON member handling is not yet mapped");
                if (extension)
                {
                    if (memberType is not INamedTypeSymbol { IsGenericType: true } dictionary ||
                        dictionary.OriginalDefinition.ToDisplayString() is not ("System.Collections.Generic.Dictionary<TKey, TValue>" or "System.Collections.Generic.IDictionary<TKey, TValue>") ||
                        dictionary.TypeArguments[0].SpecialType != SpecialType.System_String ||
                        dictionary.TypeArguments[1].ToDisplayString() != "System.Text.Json.JsonElement" ||
                        memberConverter is not null || constructorBound || Attribute(member, "JsonRequiredAttribute") is not null ||
                        member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                        throw new NotSupportedException(type.Name + "." + member.Name + " extension data requires a non-required string/JsonElement dictionary without constructor binding or converters");
                }
                if (ignoreCondition is not (0 or 2 or 3 or 4 or 5)) throw new NotSupportedException(type.Name + "." + member.Name + " unsupported ignore condition");
                if (member is IPropertySymbol property)
                {
                    if (included && (property.GetMethod is { DeclaredAccessibility: not Accessibility.Public } ||
                            property.SetMethod is { DeclaredAccessibility: not Accessibility.Public }))
                        throw new NotSupportedException(type.Name + "." + member.Name + " non-public JsonInclude accessor needs an explicit accessibility mapping");
                    if (input && property.SetMethod?.DeclaredAccessibility != Accessibility.Public && !constructorBound) continue;
                    if (!input && property.GetMethod?.DeclaredAccessibility != Accessibility.Public) continue;
                }
                else if (input && member is IFieldSymbol { IsReadOnly: true } && !constructorBound && !extension) continue;
                if (extension)
                {
                    extensionData = true;
                    continue;
                }
                if (memberConverter is not null && !stringEnumMember)
                    throw new NotSupportedException(type.Name + "." + member.Name + " custom JSON member handling is not yet mapped");
                var wireName = Attribute(member, "JsonPropertyNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string
                    ?? JsonWireNames.ConvertName(member.Name, _namingPolicy);
                if (members.ContainsKey(wireName))
                {
                    if (selectedMembers[wireName].Name == member.Name) continue;
                    throw new NotSupportedException(type.Name + " has colliding JSON property " + wireName);
                }
                var required = member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true };
                var optional = input ? !required && Attribute(member, "JsonRequiredAttribute") is null && !RequiresConstructorParameter(constructor, member)
                    : ignore is null && _defaultIgnoreCondition == 4 || ignoreCondition == 2 || ignoreCondition == 3 && (memberType.IsReferenceType || memberType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
                var memberHandling = NumberHandling(member);
                if (memberHandling is > 0 && !NumberHandlingApplies(memberType))
                    throw new NotSupportedException(type.Name + "." + member.Name + " applies number handling to a non-numeric property");
                members.Add(wireName, "  " + Quote(wireName) + (optional ? "?: " : ": ") + (stringEnumMember ? StringEnum(enumType!) + (nullableEnum ? " | null" : "") :
                    Type(memberType, input, numberHandling: memberHandling ?? NumberHandling(type))) + ";");
                selectedMembers.Add(wireName, member);
            }
        }
        _declarations.Add(name, "export interface " + name + " {\n" + string.Join("\n", members.Values) +
            (extensionData ? "\n  [key: string]: unknown;" : "") + "\n}");
        return name;
    }

    private static int? NumberHandling(ISymbol symbol)
    {
        var attribute = Attribute(symbol, "JsonNumberHandlingAttribute");
        return attribute is null ? null : ReadNumberHandling(attribute.ConstructorArguments.FirstOrDefault().Value);
    }

    private static int ReadNumberHandling(object? value)
    {
        var flags = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (flags < 0 || flags > 7) throw new NotSupportedException("Invalid JSON number handling flags");
        return flags;
    }

    private static bool NumberHandlingApplies(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array && array.ElementType.SpecialType != SpecialType.System_Byte) type = array.ElementType;
        else if (type is INamedTypeSymbol collection && collection.IsGenericType)
        {
            var definition = collection.OriginalDefinition.ToDisplayString();
            if (definition is "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>" or "System.Collections.Generic.IReadOnlyList<T>" or
                "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.ICollection<T>" or "System.Collections.Generic.IReadOnlyCollection<T>" or
                "System.Collections.Generic.HashSet<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>" or
                "System.Collections.Generic.IDictionary<TKey, TValue>" or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
                type = collection.TypeArguments.Last();
        }
        if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            type = nullable.TypeArguments[0];
        return type.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
    }

    private static string NumberType(ITypeSymbol type, bool input, int handling)
    {
        if (input && (handling & 1) != 0) return "number | string";
        if (!input && (handling & 2) != 0) return "string";
        return (handling & 4) != 0 && type.SpecialType is SpecialType.System_Single or SpecialType.System_Double
            ? "number | \"NaN\" | \"Infinity\" | \"-Infinity\"" : "number";
    }

    private static IMethodSymbol? DeserializationConstructor(INamedTypeSymbol type)
    {
        var marked = type.InstanceConstructors.Where(constructor => Attribute(constructor, "JsonConstructorAttribute") is not null).ToArray();
        if (marked.Length > 1) throw new NotSupportedException(type.Name + " has multiple JSON constructors");
        if (marked.Length == 1) return marked[0];
        // STJ uses the default struct constructor unless explicitly annotated.
        if (type.IsValueType) return null;
        var constructors = type.InstanceConstructors.Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public).ToArray();
        return constructors.FirstOrDefault(constructor => constructor.Parameters.Length == 0)
            ?? (constructors.Length == 1 ? constructors[0] : null);
    }

    private static ITypeSymbol? JsonMemberType(ISymbol member) => member switch
    {
        IPropertySymbol property => property.Type,
        IFieldSymbol field => field.Type,
        _ => null
    };

    private static IParameterSymbol? ConstructorParameter(IMethodSymbol? constructor, ISymbol member) =>
        constructor?.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, member.Name, StringComparison.OrdinalIgnoreCase) &&
            SymbolEqualityComparer.Default.Equals(candidate.Type, JsonMemberType(member)));

    private static bool RequiresConstructorParameter(IMethodSymbol? constructor, ISymbol member)
    {
        var parameter = ConstructorParameter(constructor, member);
        if (parameter is null || parameter.HasExplicitDefaultValue) return false;
        // Mirrors CommandArgumentJson's AssociatedParameter rule; wire-name
        // overrides do not change the CLR property/constructor association.
        return parameter.Type.IsReferenceType ? parameter.NullableAnnotation == NullableAnnotation.NotAnnotated
            : parameter.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }

    private static AttributeData? Attribute(ISymbol? symbol, string name) => symbol?.GetAttributes().FirstOrDefault(attribute =>
        attribute.AttributeClass?.Name == name && attribute.AttributeClass.ContainingNamespace.ToDisplayString() == "System.Text.Json.Serialization");

    private static bool IsStringEnumConverter(AttributeData converter, ITypeSymbol type)
    {
        var converterType = converter.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
        return converterType?.OriginalDefinition.ToDisplayString() == "System.Text.Json.Serialization.JsonStringEnumConverter<TEnum>" &&
            SymbolEqualityComparer.Default.Equals(converterType.TypeArguments[0], type);
    }

    private static string StringEnum(ITypeSymbol type)
    {
        if (type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.FlagsAttribute")) return "string | number";
        var names = type.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue).Select(field =>
            Quote(Attribute(field, "JsonStringEnumMemberNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string ?? field.Name));
        return string.Join(" | ", names.Distinct(StringComparer.Ordinal).Concat(new[] { "number" }));
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        foreach (var character in value)
        {
            if (character is '"' or '\\') result.Append('\\').Append(character);
            else if (character < 32 || char.IsSurrogate(character) || character is '\u2028' or '\u2029')
                result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else result.Append(character);
        }
        return result.Append('"').ToString();
    }
}
