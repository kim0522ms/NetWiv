// HTTP response behavior follows tauri 2.11.5 src/protocol/asset.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: MIT OR Apache-2.0
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWiv.Runtime;

namespace NetWiv;

internal sealed class FileAssetProtocol(FileScope scope)
{
    internal FileScope Scope => scope;
    private const ulong MaximumRangeLength = 1000 * 1024;

    internal static FileAssetProtocol? Create(JsonElement config, string identifier)
    {
        if (config.ValueKind == JsonValueKind.Undefined) return null;
        if (config.ValueKind != JsonValueKind.Object || config.EnumerateObject().Any(static property => property.Name is not ("enable" or "scope")))
            throw new ArgumentException("Asset protocol configuration must be an object containing only enable and scope.");
        var enabled = config.TryGetProperty("enable", out var enable) && enable.GetBoolean();
        var scope = new FileScope(config.TryGetProperty("scope", out var value) ? value : default, identifier);
        return enabled ? new(scope) : null;
    }

    internal static bool IsAssetUri(Uri uri) => uri.GetLeftPart(UriPartial.Authority) is
        "asset://localhost" or "http://asset.localhost" or "https://asset.localhost";

    internal AssetResponse Resolve(AssetRequest request)
    {
        // Only the native initial document origin supplies this header. Never reflect
        // an Origin header supplied by the request into access-control policy.
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Access-Control-Allow-Origin"] = request.WindowOrigin ?? "null"
        };
        try
        {
            var path = DecodePath(request.Uri);
            if (path.Split(Path.DirectorySeparatorChar == '\\' ? ['/', '\\'] : ['/']).Contains("..", StringComparer.Ordinal) ||
                !scope.IsAllowed(path)) return new(403, "", default, headers);
            if (path.Contains('\0'))
                return new(500, "text/plain", OperatingSystem.IsWindows()
                    ? "strings passed to WinAPI cannot contain NULs"u8.ToArray()
                    : "file name contained an unexpected NUL byte"u8.ToArray(), headers);
            // Rust opens Unix directories and fails on read with EISDIR; .NET
            // rejects them on open as UnauthorizedAccessException instead.
            if (OperatingSystem.IsLinux() && Directory.Exists(path))
                return new(500, "text/plain", "Is a directory (os error 21)"u8.ToArray(), headers);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            ulong length = (ulong)file.Length;
            var prefix = Read(file, 0, Math.Min(length, 8192));
            var contentType = FileAssetMimeType.Parse(prefix, path);
            var range = request.Headers.FirstOrDefault(static pair => string.Equals(pair.Key, "range", StringComparison.OrdinalIgnoreCase));
            if (range.Value is not null && range.Value.All(static c => c is >= ' ' and <= '~' or '\t'))
            {
                headers["Accept-Ranges"] = "bytes";
                headers["Access-Control-Expose-Headers"] = "content-range";
                var ranges = ParseRanges(range.Value, length);
                if (ranges is null) return Unsatisfiable(length);
                if (ranges.Count == 1)
                {
                    var (start, count) = ranges[0];
                    if (start >= length || count == 0) return Unsatisfiable(length);
                    count = Math.Min(count, MaximumRangeLength);
                    headers["Content-Range"] = FormattableString.Invariant($"bytes {start}-{start + count - 1}/{length}");
                    headers["Content-Length"] = count.ToString(CultureInfo.InvariantCulture);
                    return new(206, contentType, Read(file, start, count), headers);
                }

                var boundary = string.Concat(RandomNumberGenerator.GetBytes(30).Select(static value => value.ToString("x", CultureInfo.InvariantCulture)));
                var separator = Encoding.ASCII.GetBytes($"\r\n--{boundary}\r\n");
                using var body = new MemoryStream();
                foreach (var (start, rangeLength) in ranges)
                {
                    if (start >= length || rangeLength == 0) continue;
                    var count = Math.Min(rangeLength, MaximumRangeLength);
                    body.Write(separator);
                    body.Write(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                        $"content-type: {contentType}\r\ncontent-range: bytes {start}-{start + count - 1}/{length}\r\n\r\n")));
                    body.Write(Read(file, start, count));
                }
                // Pinned upstream returns status 200 and repeats the separator at
                // the end (without the MIME closing '--'). Preserve that behavior.
                body.Write(separator);
                headers["Content-Type"] = $"multipart/byteranges; boundary={boundary}";
                return new(200, contentType, body.ToArray(), headers);
            }

            headers["Content-Length"] = length.ToString(CultureInfo.InvariantCulture);
            if (request.Method == "HEAD") return new(200, contentType, default, headers);
            if (length < 8192) return new(200, contentType, prefix, headers);
            file.Position = 0;
            using var content = new MemoryStream();
            file.CopyTo(content);
            return new(200, contentType, content.ToArray(), headers);
        }
        catch (FileNotFoundException) { return new(404, "", default, headers); }
        catch (DirectoryNotFoundException) { return new(404, "", default, headers); }
        catch (UnauthorizedAccessException) { return new(403, "", default, headers); }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        {
            return new(500, "text/plain", Encoding.UTF8.GetBytes(error.Message), headers);
        }
    }

    private static byte[] Read(FileStream file, ulong start, ulong count)
    {
        file.Position = checked((long)start);
        var bytes = new byte[checked((int)count)];
        int read = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        return read == bytes.Length ? bytes : bytes[..read];
    }

    private static AssetResponse Unsatisfiable(ulong length) => new(416, "", default,
        new Dictionary<string, string> { ["Content-Range"] = "bytes */" + length.ToString(CultureInfo.InvariantCulture) });

    private static string DecodePath(Uri uri)
    {
        // Uri.AbsolutePath canonicalizes escaped dot segments. Inspect the original
        // wire spelling so the SafePathBuf parent-directory check cannot be bypassed.
        var raw = uri.OriginalString;
        int start = raw.IndexOf('/', raw.IndexOf("://", StringComparison.Ordinal) + 3);
        if (start < 0) return "";
        int end = raw.IndexOfAny(['?', '#'], start);
        var bytes = Encoding.UTF8.GetBytes(raw[(start + 1)..(end < 0 ? raw.Length : end)]);
        int written = 0;
        for (int index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == '%' && index + 2 < bytes.Length &&
                Hex(bytes[index + 1]) is var high && high >= 0 && Hex(bytes[index + 2]) is var low && low >= 0)
            {
                bytes[written++] = (byte)(high * 16 + low);
                index += 2;
            }
            else bytes[written++] = bytes[index];
        }
        return Encoding.UTF8.GetString(bytes, 0, written);
    }

    private static int Hex(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => -1
    };

    // Parsing semantics follow pinned http-range 0.1.5 (MIT).
    private static List<(ulong Start, ulong Length)>? ParseRanges(string header, ulong size)
    {
        if (header.Length == 0) return [];
        if (!header.StartsWith("bytes=", StringComparison.Ordinal)) return null;
        var result = new List<(ulong, ulong)>();
        bool noOverlap = false;
        foreach (var item in header[6..].Split(','))
        {
            var value = item.Trim(' ', '\t');
            if (value.Length == 0) continue;
            int dash = value.IndexOf('-');
            if (dash < 0) return null;
            var first = value[..dash].Trim(' ', '\t');
            var last = value[(dash + 1)..].Trim(' ', '\t');
            if (first.Length == 0)
            {
                if (!Number(last, out var count)) return null;
                if (count == 0) { noOverlap = true; continue; }
                count = Math.Min(count, size);
                result.Add((size - count, count));
            }
            else
            {
                if (!Number(first, out var start)) return null;
                if (start >= size) { noOverlap = true; continue; }
                var end = size - 1;
                if (last.Length != 0 && (!Number(last, out end) || start > end)) return null;
                result.Add((start, Math.Min(end, size - 1) - start + 1));
            }
        }
        return noOverlap && result.Count == 0 ? null : result;
    }

    private static bool Number(string value, out ulong number) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
}
