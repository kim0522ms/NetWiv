using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace NetWiv.Build.Frontend;

/// <summary>Reads generated bindings from PE metadata without loading or executing application code.</summary>
public sealed record FrontendBindings(string JavaScript, string TypeScript)
{
    /// <summary>The typed C# client for a .NET frontend, or null when the assembly predates it.</summary>
    public string? CSharp { get; init; }

    /// <summary>The runtime-import-free command factory, or null for older assemblies.</summary>
    public string? FactoryJavaScript { get; init; }

    /// <summary>Type declarations for the command factory, or null for older assemblies.</summary>
    public string? FactoryTypeScript { get; init; }

    public static FrontendBindings Read(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidOperationException("Read the managed build DLL before NativeAOT publish, not the native executable.");
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) throw new InvalidOperationException("Frontend bindings require an assembly DLL, not a netmodule.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (reader.GetString(type.Namespace) != "System.Reflection" || reader.GetString(type.Name) != "AssemblyMetadataAttribute") continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new InvalidOperationException("Invalid assembly metadata prolog.");
            var key = blob.ReadSerializedString();
            var value = blob.ReadSerializedString();
            if (key is not ("NetWiv.Frontend.JavaScript.v1" or "NetWiv.Frontend.TypeScript.v1" or "NetWiv.Frontend.CSharp.v1" or "NetWiv.Frontend.FactoryJavaScript.v1" or "NetWiv.Frontend.FactoryTypeScript.v1")) continue;
            if (value is null || !values.TryAdd(key, value)) throw new InvalidOperationException("Missing or duplicate frontend binding metadata: " + key);
        }
        var csharp = values.TryGetValue("NetWiv.Frontend.CSharp.v1", out var client) ? client : null;
        values.TryGetValue("NetWiv.Frontend.FactoryJavaScript.v1", out var factory);
        values.TryGetValue("NetWiv.Frontend.FactoryTypeScript.v1", out var factoryTypes);
        if ((factory is null) != (factoryTypes is null)) throw new InvalidOperationException("Incomplete frontend command factory metadata.");
        if (values.TryGetValue("NetWiv.Frontend.JavaScript.v1", out var js) && values.TryGetValue("NetWiv.Frontend.TypeScript.v1", out var types))
            return new(js, types) { CSharp = csharp, FactoryJavaScript = factory, FactoryTypeScript = factoryTypes };
        if (factory is not null) throw new InvalidOperationException("Frontend command factory metadata requires script bindings.");
        // An app that sets only NetWivCSharpBindingsDirectory gets the C# client without script bindings.
        if (csharp is not null) return new("", "") { CSharp = csharp, HasScript = false };
        throw new InvalidOperationException("No generated frontend bindings found. Add [assembly: GenerateFrontendBindings] and reference NetWiv.Generators, then build the managed DLL.");
    }

    /// <summary>False when the app was built for the C# client only, without script bindings.</summary>
    public bool HasScript { get; init; } = true;

    public void WriteTo(string directory)
    {
        if (!HasScript) throw new InvalidOperationException("The app was built with only NetWivCSharpBindingsDirectory set, so it has no JavaScript bindings. Set NetWivFrontendBindingsDirectory to generate commands.js and commands.d.ts.");
        if ((FactoryJavaScript is null) != (FactoryTypeScript is null)) throw new InvalidOperationException("Incomplete frontend command factory metadata.");
        var files = new List<(string Path, string Content)> { (Path.Combine(directory, "commands.js"), JavaScript), (Path.Combine(directory, "commands.d.ts"), TypeScript) };
        var factoryPath = Path.Combine(directory, "commands.factory.js");
        var factoryTypesPath = Path.Combine(directory, "commands.factory.d.ts");
        if (FactoryJavaScript is not null)
        {
            files.Add((factoryPath, FactoryJavaScript));
            files.Add((factoryTypesPath, FactoryTypeScript!));
        }
        // Exporting an older assembly removes a previous factory, but never user-authored files.
        var stale = FactoryJavaScript is null ? new[] { factoryPath, factoryTypesPath }
            .Where(path => File.Exists(path) && File.ReadAllText(path).StartsWith("// Generated by NetWiv. Do not edit.", StringComparison.Ordinal)).ToArray() : [];
        Write(directory, files.ToArray(), stale);
    }

    /// <summary>Writes the typed C# client (NetWivCommands.g.cs) for a .NET frontend.</summary>
    public void WriteCSharpTo(string directory)
    {
        if (CSharp is null) throw new InvalidOperationException("The app was built with a NetWiv.Generators version that does not emit a C# client. Update NetWiv.Generators.");
        Write(directory, [(Path.Combine(directory, "NetWivCommands.g.cs"), CSharp)]);
    }

    private static void Write(string directory, (string Path, string Content)[] files, string[]? stale = null)
    {
        foreach (var path in files.Select(file => file.Path).Concat(stale ?? []))
        {
            if (Directory.Exists(path))
                throw new InvalidOperationException($"Refusing to overwrite a directory with generated bindings: {path}");
            if (File.Exists(path) && !File.ReadAllText(path).StartsWith("// Generated by NetWiv. Do not edit.", StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing to overwrite a file not generated by NetWiv: {path}");
        }
        Directory.CreateDirectory(directory);
        foreach (var (path, content) in files) WriteIfChanged(path, content);
        foreach (var path in stale ?? []) File.Delete(path);
    }

    // The app's own build and a frontend's bindings-only build can export at the same time, so
    // replace the file in one step and accept a concurrent writer that produced the same text.
    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(path) || File.ReadAllText(path) != content) throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
