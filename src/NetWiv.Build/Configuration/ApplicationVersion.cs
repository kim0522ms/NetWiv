// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text.Json;

namespace NetWiv.Build.Configuration;

public static class ApplicationVersion
{
    public static string Resolve(string value, string configurationDirectory, out string? inputFile)
    {
        inputFile = null;
        var path = value.Length == 0 ? "" : Path.GetFullPath(value, configurationDirectory);
        if (File.Exists(path) || Directory.Exists(path))
        {
            inputFile = path;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("`tauri.conf.json > version` value is not a path to a JSON object");
            if (!document.RootElement.TryGetProperty("version", out var version))
                throw new ArgumentException("JSON must contain a `version` field");
            if (version.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"`{value} > version` must be a string");
            value = version.GetString()!;
        }
        try { _ = WindowsFileVersion.Encode(value); }
        catch (ArgumentException) { throw new ArgumentException("`tauri.conf.json > version` must be a semver string"); }
        return value;
    }
}
