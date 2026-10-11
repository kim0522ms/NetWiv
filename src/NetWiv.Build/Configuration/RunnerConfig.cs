// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text.Json.Nodes;

namespace NetWiv.Build.Configuration;

/// <summary>A build runner in Tauri's string or command/cwd/args format.</summary>
public sealed record RunnerConfig
{
    public string Command { get; }
    public string? Cwd { get; }
    public IReadOnlyList<string>? Args { get; }
    private readonly bool _objectFormat;

    public RunnerConfig(string command) => Command = command;
    public RunnerConfig(string command, string? cwd, IEnumerable<string>? args)
    {
        Command = command;
        Cwd = cwd;
        Args = args is null ? null : Array.AsReadOnly(args.ToArray());
        _objectFormat = true;
    }

    /// <summary>The .NET equivalent of Tauri's default cargo runner.</summary>
    public static RunnerConfig Default => new("dotnet");
    public static RunnerConfig Parse(string command) => new(command);

    public static RunnerConfig FromJson(JsonNode? value)
    {
        if (value is JsonValue text && text.TryGetValue<string>(out var command)) return new(command);
        if (value is not JsonObject obj || obj["cmd"] is not JsonValue cmd || !cmd.TryGetValue<string>(out command))
            throw new NetWivConfigException("build.runner must be a string or an object with a string cmd.");
        string? cwd = null;
        if (obj["cwd"] is { } cwdNode && (cwdNode is not JsonValue cwdValue || !cwdValue.TryGetValue(out cwd)))
            throw new NetWivConfigException("build.runner.cwd must be a string or null.");
        string[]? args = null;
        if (obj["args"] is { } argsNode)
        {
            if (argsNode is not JsonArray array || array.Any(item => item is not JsonValue argument || !argument.TryGetValue<string>(out _)))
                throw new NetWivConfigException("build.runner.args must be a string array or null.");
            args = array.Select(item => item!.GetValue<string>()).ToArray();
        }
        return new(command, cwd, args);
    }

    public JsonNode ToJson()
    {
        if (!_objectFormat) return JsonValue.Create(Command)!;
        var value = new JsonObject { ["cmd"] = Command };
        if (Cwd is not null) value["cwd"] = Cwd;
        if (Args is not null) value["args"] = new JsonArray(Args.Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray());
        return value;
    }

    public bool Equals(RunnerConfig? other) => other is not null && _objectFormat == other._objectFormat &&
        Command == other.Command && Cwd == other.Cwd &&
        (Args is null ? other.Args is null : other.Args is not null && Args.SequenceEqual(other.Args));
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_objectFormat); hash.Add(Command); hash.Add(Cwd); hash.Add(Args is null);
        if (Args is not null) foreach (var arg in Args) hash.Add(arg);
        return hash.ToHashCode();
    }
}
