// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
// Version conversion/validation follows tauri-bundler 2.11.4 windows/msi.
using System.Globalization;
using System.Text.RegularExpressions;
using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

internal static partial class MsiVersion
{
    internal static string Resolve(string version, string? wixOverride)
    {
        var result = wixOverride ?? Convert(version);
        Validate(result);
        return result;
    }

    internal static string Convert(string version)
    {
        var match = SemanticVersion().Match(version);
        if (!match.Success || new[] { "major", "minor", "patch" }.Any(name => !TryNumber(match.Groups[name].Value, out _)))
            throw new NetWivConfigException("invalid app version");
        var pre = match.Groups["pre"].Value;
        // SemVer numeric prerelease identifiers cannot have leading zeroes.
        if (pre.Split('.').Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit)))
            throw new NetWivConfigException("invalid app version");
        var build = match.Groups["build"].Value;
        var fourth = build.Length > 0 ? build : pre;
        if (fourth.Length == 0) return version;
        if (!TryNumber(fourth, out var number) || number > 65535)
            throw new NetWivConfigException(build.Length > 0
                ? "optional build metadata in app version must be numeric-only and cannot be greater than 65535 for msi target"
                : "optional pre-release identifier in app version must be numeric-only and cannot be greater than 65535 for msi target");
        return $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}.{match.Groups["patch"].Value}.{fourth}";
    }

    internal static void Validate(string version)
    {
        // Preserve upstream's filter_map parsing, including its treatment of
        // extra/non-numeric components. WiX itself still validates its input.
        var parts = version.Split('.').Where(part => TryNumber(part, out _))
            .Select(part => ulong.Parse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)).ToArray();
        if (parts.Length < 3)
            throw new NetWivConfigException("app wix version should be in the format major.minor.patch.build (build is optional)");
        if (parts[0] > 255) throw new NetWivConfigException("app version major number cannot be greater than 255");
        if (parts[1] > 255) throw new NetWivConfigException("app version minor number cannot be greater than 255");
        if (parts[2] > 65535) throw new NetWivConfigException("app version patch number cannot be greater than 65535");
        if (parts.Length == 4 && parts[3] > 65535) throw new NetWivConfigException("app version build number cannot be greater than 65535");
    }

    private static bool TryNumber(string value, out ulong number)
    {
        var digits = value.AsSpan();
        if (digits.StartsWith("+")) digits = digits[1..];
        if (digits.IsEmpty || digits.ContainsAnyExceptInRange('0', '9')) { number = 0; return false; }
        return ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    [GeneratedRegex(@"\A(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();
}
