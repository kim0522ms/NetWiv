using System.Text.Json;
using NetWiv;

internal static class FileScopeTests
{
    public static Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "netwiv-file-scope-" + Guid.NewGuid().ToString("N"));
        var allowed = Path.Combine(root, "Allowed");
        var denied = Path.Combine(allowed, "private");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(denied);
        Directory.CreateDirectory(outside);
        var visible = Path.Combine(allowed, "visible.txt");
        var nested = Path.Combine(denied, "secret.txt");
        var hidden = Path.Combine(allowed, ".hidden.txt");
        var outsideFile = Path.Combine(outside, "outside.txt");
        File.WriteAllText(visible, "visible");
        File.WriteAllText(nested, "secret");
        File.WriteAllText(hidden, "hidden");
        File.WriteAllText(outsideFile, "outside");

        try
        {
            var denyWins = new FileScope(ObjectScope(
                allow: [Path.Combine(allowed, "**")],
                deny: [Path.Combine(denied, "**")]), "com.netwiv.scope-test");
            Check(denyWins.IsAllowed(visible), "allowed file was rejected");
            Check(!denyWins.IsAllowed(nested), "deny did not override allow");
            Check(!denyWins.IsAllowed(outsideFile), "path outside allow scope was accepted");

            var separator = new FileScope(ArrayScope(Path.Combine(allowed, "*.txt")), "com.netwiv.scope-test");
            Check(separator.IsAllowed(visible), "single-component wildcard did not match");
            Check(!separator.IsAllowed(nested), "wildcard crossed a literal separator");

            var caseSensitive = new FileScope(ArrayScope(Path.Combine(allowed, "MISSING.txt")), "com.netwiv.scope-test");
            Check(!caseSensitive.IsAllowed(Path.Combine(allowed, "missing.txt")), "file scope matching was not case-sensitive");

            var platformDotDefault = new FileScope(ArrayScope(Path.Combine(allowed, "**")), "com.netwiv.scope-test");
            Check(platformDotDefault.IsAllowed(hidden) == OperatingSystem.IsWindows(), "platform leading-dot default differed from pinned scope");

            var wildcardDot = new FileScope(ObjectScope(
                allow: [Path.Combine(allowed, "*.txt")],
                writeLeadingDot: true,
                leadingDot: false), "com.netwiv.scope-test");
            Check(wildcardDot.IsAllowed(hidden), "explicit false did not allow wildcard leading-dot matching");

            var literalDot = new FileScope(ObjectScope(
                allow: [Path.Combine(allowed, "*.txt")],
                writeLeadingDot: true,
                leadingDot: true), "com.netwiv.scope-test");
            Check(!literalDot.IsAllowed(hidden), "wildcard matched a leading dot with literal-dot enabled");

            var explicitDot = new FileScope(ObjectScope(
                allow: [Path.Combine(allowed, ".*.txt")],
                writeLeadingDot: true,
                leadingDot: true), "com.netwiv.scope-test");
            Check(explicitDot.IsAllowed(hidden), "literal dot pattern did not match a hidden file");

            var kebabDot = new FileScope(ObjectScope(
                allow: [Path.Combine(allowed, ".*.txt")],
                writeLeadingDot: true,
                leadingDot: true,
                kebabCase: true), "com.netwiv.scope-test");
            Check(kebabDot.IsAllowed(hidden), "kebab-case literal-dot option was not accepted");

            var empty = new FileScope(default, "com.netwiv.scope-test");
            Check(!empty.IsAllowed(visible), "undefined scope did not default to empty allow");
            var nullOption = new FileScope(ObjectScope(writeLeadingDot: true), "com.netwiv.scope-test");
            Check(!nullOption.IsAllowed(visible), "null leading-dot option did not retain the default");

            var relativeToTemp = Path.GetRelativePath(Path.GetTempPath(), visible);
            var expanded = new FileScope(ArrayScope("$TEMP/" + relativeToTemp.Replace('\\', '/')), "com.netwiv.scope-test");
            Check(expanded.IsAllowed(visible), "$TEMP scope variable did not resolve");

            ExpectArgument(() => new FileScope(ArrayScope(Path.Combine(allowed, "***")), "com.netwiv.scope-test"), "invalid glob was accepted");
            ExpectArgument(() => new FileScope(JsonValue.Parse("{\"allow\":\"not-an-array\"}"), "com.netwiv.scope-test"), "invalid allow value was accepted");
            ExpectArgument(() => new FileScope(JsonValue.Null, "com.netwiv.scope-test"), "null file scope was accepted");

            VerifySymlinkScope(root, allowed, outside, outsideFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static void VerifySymlinkScope(string root, string allowed, string outside, string outsideFile)
    {
        var outsideAlias = Path.Combine(root, "outside-alias");
        var link = Path.Combine(allowed, "escape");
        var broken = Path.Combine(allowed, "broken");
        var relativeTargetName = "relative-" + Guid.NewGuid().ToString("N") + ".txt";
        var relativeTarget = Path.Combine(allowed, relativeTargetName);
        var relativeLink = Path.Combine(allowed, "relative-link.txt");
        File.WriteAllText(relativeTarget, "relative");
        var created = new List<string>();
        try
        {
            Directory.CreateSymbolicLink(outsideAlias, outside);
            created.Add(outsideAlias);
            Directory.CreateSymbolicLink(link, outsideAlias);
            created.Add(link);
            Directory.CreateSymbolicLink(broken, Path.Combine(root, "missing"));
            created.Add(broken);
            File.CreateSymbolicLink(relativeLink, relativeTargetName);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            DeleteFileLink(relativeLink);
            DeleteDirectoryLinks(created);
            Console.WriteLine("SKIP file scope symlink assertions: Windows symbolic-link creation is not permitted.");
            return;
        }

        try
        {
            var confined = new FileScope(ArrayScope(Path.Combine(allowed, "**")), "com.netwiv.scope-test");
            Check(!confined.IsAllowed(Path.Combine(link, Path.GetFileName(outsideFile))), "nested directory symlinks escaped the allowed scope");
            Check(!confined.IsAllowed(broken), "broken final symlink was matched by its lexical path");
            Check(!confined.IsAllowed(relativeLink), "relative final symlink was resolved against its parent instead of the process directory");

            var rawRelativeTarget = new FileScope(ArrayScope(relativeTargetName), "com.netwiv.scope-test");
            Check(rawRelativeTarget.IsAllowed(relativeLink), "stored relative symlink target did not participate in scope matching");

            var aliased = new FileScope(ArrayScope(Path.Combine(link, "**")), "com.netwiv.scope-test");
            Check(aliased.IsAllowed(outsideFile), "canonicalized configured symlink did not match its target");
        }
        finally
        {
            DeleteFileLink(relativeLink);
            DeleteDirectoryLinks(created);
        }
    }

    private static void DeleteFileLink(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is not null) File.Delete(path);
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static void DeleteDirectoryLinks(IEnumerable<string> paths)
    {
        foreach (var path in paths.Reverse())
        {
            if (new DirectoryInfo(path).LinkTarget is null) continue;
            if (OperatingSystem.IsWindows()) Directory.Delete(path);
            else File.Delete(path);
        }
    }

    private static JsonElement ArrayScope(params string[] patterns) => JsonValue.Write(writer =>
    {
        writer.WriteStartArray();
        foreach (var pattern in patterns) writer.WriteStringValue(pattern);
        writer.WriteEndArray();
    });

    private static JsonElement ObjectScope(
        IReadOnlyList<string>? allow = null,
        IReadOnlyList<string>? deny = null,
        bool writeLeadingDot = false,
        bool? leadingDot = null,
        bool kebabCase = false) => JsonValue.Write(writer =>
    {
        writer.WriteStartObject();
        if (allow is not null)
        {
            writer.WriteStartArray("allow");
            foreach (var pattern in allow) writer.WriteStringValue(pattern);
            writer.WriteEndArray();
        }
        if (deny is not null)
        {
            writer.WriteStartArray("deny");
            foreach (var pattern in deny) writer.WriteStringValue(pattern);
            writer.WriteEndArray();
        }
        if (writeLeadingDot)
        {
            writer.WritePropertyName(kebabCase ? "require-literal-leading-dot" : "requireLiteralLeadingDot");
            if (leadingDot is bool value) writer.WriteBooleanValue(value);
            else writer.WriteNullValue();
        }
        writer.WriteEndObject();
    });

    private static void ExpectArgument(Action action, string message)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
