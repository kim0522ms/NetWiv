// Original manifest/permission traversal vectors: tauri-utils/src/acl/resolved.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
using NetWiv;

internal static class PluginPermissionTests
{
    internal static Task OtherPlugins()
    {
        var permissions = new List<PermissionDefinition>();
        foreach (var name in new[] { "fs", "http", "dialog" })
        {
            var context = new PluginContext(name, new CommandRegistry(), permissions, JsonValue.Null);
            new PermissionFixturePlugin(name).Initialize(context);
            context.Complete();
        }
        var authority = new AclAuthorizer(permissions, [], Origins(), false);
        Check("fs:default", "fs:read", "fs:exist");
        Check("fs:rm", "fs:rm");
        Check("http:fetch-with-cancel", "http:fetch", "http:fetch-cancel");
        Check("dialog:extra", "dialog:save", "fs:read", "fs:exist", "fs:write", "http:fetch", "http:fetch-cancel");
        Check("http:default");
        return Task.CompletedTask;

        void Check(string name, params string[] expected)
        {
            var actual = authority.Resolve(name).Select(permission => permission.Identifier).ToArray();
            Assert.Equal(expected.Length, actual.Length);
            for (var index = 0; index < expected.Length; index++) Assert.Equal(expected[index], actual[index]);
        }
    }

    internal static Task PrecedenceAndFailures()
    {
        foreach (var setFirst in new[] { false, true })
        {
            var definitions = new List<PermissionDefinition>();
            var context = new PluginContext("test", new CommandRegistry(), definitions, JsonValue.Null);
            context.Permission(new("other", "other"));
            if (setFirst) context.PermissionSet("same", "other");
            context.Permission(new("same", "leaf"));
            if (!setFirst) context.PermissionSet("same", "other");
            context.PermissionSet("nested", "same");
            context.Permission(new("default", "ignored"));
            context.PermissionSet("default", "same");
            context.PermissionSet("with-default", "default");
            context.Complete();
            var authority = new AclAuthorizer(definitions, [], Origins(), false);
            Assert.Equal("test:other", authority.Resolve("test:same").Single().Identifier);
            Assert.Equal("plugin:test|leaf", authority.Resolve("test:nested").Single().Allow.Single());
            Assert.Equal("plugin:test|leaf", authority.Resolve("test:with-default").Single().Allow.Single());
            Assert.Throws<ArgumentException>(() => context.PermissionSet("foreign:set", "other"));
            context.PermissionSet("cycle", "cycle");
            Assert.Throws<InvalidOperationException>(() => new AclAuthorizer(definitions,
                [new("main", ["test:cycle"], Windows: ["main"])], Origins(), false));
            context.PermissionSet("unknown", "missing:permission");
            Assert.Throws<InvalidOperationException>(() => new AclAuthorizer(definitions,
                [new("main", ["test:unknown"], Windows: ["main"])], Origins(), false));
        }
        return Task.CompletedTask;
    }

    private static HashSet<string> Origins() => new(StringComparer.Ordinal) { "tauri://localhost" };
}

internal sealed class PermissionFixturePlugin(string name) : IPlugin
{
    public string Name => name;
    public void Initialize(PluginContext context)
    {
        var commands = name switch
        {
            "fs" => new[] { "read", "write", "rm", "exist" },
            "http" => ["fetch", "fetch-cancel"],
            "dialog" => ["open", "save"],
            _ => throw new ArgumentException(name)
        };
        foreach (var command in commands)
        {
            context.RegisterCommand(command, (_, _, _) => ValueTask.FromResult(JsonValue.From(command)));
            context.Permission(new(command, command));
        }
        if (name == "fs") context.PermissionSet("default", "read", "exist");
        if (name == "http") context.PermissionSet("fetch-with-cancel", "fetch", "fetch-cancel");
        if (name == "dialog") context.PermissionSet("extra", "save", "fs:default", "fs:write", "http:default", "http:fetch-with-cancel");
    }
}
