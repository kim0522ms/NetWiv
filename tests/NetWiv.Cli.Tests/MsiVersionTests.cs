// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using NetWiv.Build.Configuration;
using NetWiv.Cli;

internal static class MsiVersionTests
{
    internal static Task ValidatesWixVersion()
    {
        foreach (var version in new[] { "1.1.1", "1.1.1.1", "255.1.1.1", "1.255.1.1", "1.1.65535.1", "1.1.1.65535" })
            MsiVersion.Validate(version);
        foreach (var version in new[] { "256.1.1.1", "1.256.1.1", "1.1.65536.1", "1.1.1.65536" })
            Reject(() => MsiVersion.Validate(version));
        return Task.CompletedTask;
    }

    internal static Task ConvertsVersionToWix()
    {
        foreach (var (input, output) in new[] { ("1.1.2", "1.1.2"), ("1.1.2-4", "1.1.2.4"),
            ("1.1.2-65535", "1.1.2.65535"), ("1.1.2+2", "1.1.2.2") }) Equal(output, MsiVersion.Convert(input));
        foreach (var input in new[] { "1.1.2-alpha", "1.1.2-alpha.4", "1.1.2+asd.3" }) Reject(() => MsiVersion.Convert(input));
        return Task.CompletedTask;
    }

    internal static Task Boundaries()
    {
        Equal("1.2.3.0004", MsiVersion.Resolve("1.2.3-5+0004", null));
        Equal("1.2.3.4", MsiVersion.Resolve("1.2.3-beta+4", null));
        Equal("2.3.4.5", MsiVersion.Resolve("1.2.3-beta", "2.3.4.5"));
        foreach (var value in new[] { "01.2.3", "1.2.3-04", "1.2.3+65536", "1.2.3-65536", "1.2", "1.2.3\n",
            "1.2.3+", "1.2.3-", "18446744073709551616.2.3", "1.2.3+18446744073709551616", "256.1.2" })
            Reject(() => MsiVersion.Resolve(value, null));
        Reject(() => MsiVersion.Resolve("1.2.3", "1.2.65536"));
        Reject(() => MsiVersion.Resolve("1.2.3", ""));
        return Task.CompletedTask;
    }

    private static void Equal(string expected, string actual)
    {
        if (expected != actual) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (NetWivConfigException) { return; }
        throw new InvalidOperationException("Expected invalid MSI version to be rejected.");
    }
}
