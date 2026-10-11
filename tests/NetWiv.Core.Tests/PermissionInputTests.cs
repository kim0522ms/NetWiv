// Assertion ports from tauri-utils/src/acl/{identifier,capability}.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text.Json;
using NetWiv;

internal static class PermissionInputTests
{
    public static Task Format()
    {
        foreach (var value in new[] { "prefix:base", "prefix3:base", "preFix:base", "pre--fix:base--sep", "prefix:base--sep", "pre--fix:base" })
            Equal(value, PermissionIdentifier.Parse(value).Value);
        foreach (var value in new[] { "tauri-plugin-prefix:base", "-prefix-:-base-", "-prefix:base", "prefix-:base", "prefix:-base", "prefix:base-", "prefix::base", ":base", "prefix:", ":prefix:base:", "base:", "", "💩", new string('a', PermissionIdentifier.MaxLength + 1) })
            Reject(() => PermissionIdentifier.Parse(value));
        Equal(new string('a', PermissionIdentifier.MaxLength), PermissionIdentifier.Parse(new string('a', PermissionIdentifier.MaxLength)).Base);
        // Core identifiers use the last colon, including consecutive colons.
        Equal("core:window", PermissionIdentifier.Parse("core:window:default").Prefix);
        Equal("core:", PermissionIdentifier.Parse("core::base").Prefix);
        Reject(() => PermissionIdentifier.Parse("plugin:bad_name"));
        return Task.CompletedTask;
    }

    public static Task Base()
    {
        Equal("base", PermissionIdentifier.Parse("prefix:base").Base);
        Equal("base", PermissionIdentifier.Parse("base").Base);
        return Task.CompletedTask;
    }

    public static Task Prefix()
    {
        Equal("prefix", PermissionIdentifier.Parse("prefix:base").Prefix);
        Equal<string?>(null, PermissionIdentifier.Parse("base").Prefix);
        return Task.CompletedTask;
    }

    public static Task PermissionEntryDe()
    {
        var simple = EmbeddedConfiguration.ReadCapability(JsonValue.Parse("""{"identifier":"test","permissions":["plugin:perm"]}"""));
        Equal("plugin:perm", simple.Permissions.Single());
        Equal(0, simple.PermissionScopes!.Count);
        var extended = EmbeddedConfiguration.ReadCapability(JsonValue.Parse("""{"identifier":"test","permissions":[{"identifier":"plugin:perm","allow":[],"deny":null}]}"""));
        Equal("plugin:perm", extended.Permissions.Single());
        var scope = extended.PermissionScopes!.Single();
        Equal("plugin:perm", scope.Identifier);
        Equal(0, scope.Allow!.Count);
        Equal<IReadOnlyList<JsonElement>?>(null, scope.Deny);
        return Task.CompletedTask;
    }

    public static Task InvalidInputs()
    {
        foreach (var input in new[] {
            """{"identifier":"test"}""", """{"identifier":"test","permissions":null}""",
            """{"identifier":"test","permissions":["plugin:bad_name"]}""",
            """{"identifier":"test","permissions":[{"identifier":"plugin:perm","allow":{}}]}""",
            """{"identifier":"test","permissions":[{"identifier":"plugin:perm","deny":1}]}""",
            """{"identifier":"test","permissions":[],"windows":null}""",
            """{"identifier":"test","permissions":[],"webviews":[null]}""",
            """{"identifier":"test","permissions":[],"remote":{}}""",
            """{"identifier":"test","permissions":[],"remote":{"urls":[1]}}""",
            """{"identifier":"test","permissions":[],"local":null}""",
            """{"identifier":"test","permissions":[],"platforms":["unknown"]}""",
            """{"identifier":"test","permissions":[null]}""" })
            Reject(() => EmbeddedConfiguration.ReadCapability(JsonValue.Parse(input)));
        var valid = EmbeddedConfiguration.ReadCapability(JsonValue.Parse("""{"identifier":"test","permissions":[],"remote":null,"platforms":null,"unknown":"ignored upstream"}"""));
        Equal(true, valid.Local);
        Reject(() => new Builder().Permission(new PermissionDefinition("plugin:bad_name", "ping"))
            .Capability(new Capability("main", ["plugin:bad_name"], Windows: ["main"]))
            .UseRuntime(new FakeRuntime()).Build());
        return Task.CompletedTask;
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (FormatException) { return; }
        throw new Exception("Expected malformed permission input to be rejected.");
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
}
