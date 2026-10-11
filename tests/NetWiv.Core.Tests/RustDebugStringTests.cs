// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Security.Cryptography;
using System.Text;
using NetWiv;

internal static class RustDebugStringTests
{
    internal static Task UnicodeOracle()
    {
        // SHA-256 of the complete scalar-string corpus emitted by the pinned
        // Rust 1.95.0 generator's --oracle mode (UTF-8, one Debug string + LF).
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int value = 0; value <= 0x10ffff; value++)
        {
            if (!Rune.IsValid(value)) continue;
            hash.AppendData(Encoding.UTF8.GetBytes(RustDebugString.Format(new Rune(value).ToString()) + "\n"));
        }
        Assert.Equal("1c62555c93d63747f1d39bbc077bd81afbdf1fdafcba54d4a0b2c26d86b83b3d",
            Convert.ToHexStringLower(hash.GetHashAndReset()));
        Assert.Equal("\"a\\u{301}👩\\u{200d}💻\\u{10ffff}\"", RustDebugString.Format("a\u0301👩\u200d💻\U0010ffff"));
        Assert.Equal("string \"\\0\\u{8}\\u{c}\\t\\n\\r\\u{1b}\"",
            CommandArgumentJson.Unexpected(JsonValue.From("\0\b\f\t\n\r\u001b")));
        return Task.CompletedTask;
    }

    // Ports Tauri event::tests::test_illegal_event_name verbatim input/message.
    internal static Task IllegalEventName()
    {
        try
        {
            EventName.Validate("some\r illegal event name");
            throw new Exception("Invalid event name was accepted.");
        }
        catch (ArgumentException error)
        {
            Assert.Equal("only alphanumeric, '-', '/', ':', '_' permitted for event names: \"some\\r illegal event name\"", error.Message);
        }
        return Task.CompletedTask;
    }
}
