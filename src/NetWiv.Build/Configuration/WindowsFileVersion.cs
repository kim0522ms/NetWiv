// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
// Mirrors tauri-build 2.6.3 to_winres_version; this differs from MSI version rules.
using System.Globalization;
using System.Text.RegularExpressions;

namespace NetWiv.Build.Configuration;

public static partial class WindowsFileVersion
{
    public static ulong Encode(string version)
    {
        var match = SemanticVersion().Match(version);
        if (!match.Success || !Number(match.Groups["major"].Value, out var major)
            || !Number(match.Groups["minor"].Value, out var minor) || !Number(match.Groups["patch"].Value, out var patch)
            || match.Groups["pre"].Value.Split('.').Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit)))
            throw new ArgumentException("Invalid semantic application version: " + version);
        var build = ushort.TryParse(match.Groups["build"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0UL;
        return (major << 48) | (minor << 32) | (patch << 16) | build;
    }

    public static string Format(string version)
    {
        var encoded = Encode(version);
        return $"{(encoded >> 48) & 0xffff}.{(encoded >> 32) & 0xffff}.{(encoded >> 16) & 0xffff}.{encoded & 0xffff}";
    }

    private static bool Number(string value, out ulong result) => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    [GeneratedRegex(@"\A(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();
}
