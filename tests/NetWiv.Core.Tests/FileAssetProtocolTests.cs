using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NetWiv;
using NetWiv.Runtime;

internal static class FileAssetProtocolTests
{
    internal static Task Run()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .UseEmbeddedConfig(assembly: typeof(FileAssetProtocolTests).Assembly, resourceName: "asset-config.json")
            .ResolveAssets(_ => new(200, "text/plain", "delegate must not bypass scope"u8.ToArray()))
            .Build();
        app.Run();
        if (!runtime.Context!.AssetProtocolEnabled) throw new Exception("Enabled asset config did not reach runtime.");
        var deniedByConfig = runtime.Context.ResolveAsset(new(new Uri("asset://localhost/secret.txt"), "GET",
            new Dictionary<string, string> { ["Origin"] = "https://spoofed.example" }, WindowOrigin: "tauri://localhost"))!;
        if (deniedByConfig.StatusCode != 403 || deniedByConfig.Headers!["Access-Control-Allow-Origin"] != "tauri://localhost" ||
            deniedByConfig.Headers.ContainsKey("X-Embedded-Asset")) throw new Exception("Asset scope/config/origin policy mismatch.");
        var root = Path.Combine(Path.GetTempPath(), "netwiv-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "한글 # sample.txt");
            File.WriteAllBytes(path, "0123456789"u8.ToArray());
            File.WriteAllText(Path.Combine(root, "secret.txt"), "secret");
            Directory.CreateDirectory(Path.Combine(root, "folder"));
            var scope = JsonValue.Write(writer =>
            {
                writer.WriteStartObject();
                writer.WriteStartArray("allow"); writer.WriteStringValue(root.Replace('\\', '/') + "/**"); writer.WriteEndArray();
                writer.WriteStartArray("deny"); writer.WriteStringValue(root.Replace('\\', '/') + "/secret.txt"); writer.WriteEndArray();
                writer.WriteEndObject();
            });
            var resolver = new FileAssetProtocol(new FileScope(scope, "dev.netwiv.test"));
            AssetResponse Get(string method = "GET", string? range = null, string? file = null, string? raw = null)
            {
                var url = raw ?? "asset://localhost/" + Uri.EscapeDataString(file ?? path);
                return resolver.Resolve(new(new Uri(url), method, range is null ? new Dictionary<string, string>() :
                    new Dictionary<string, string> { ["Range"] = range }, WindowOrigin: "https://initial.example"));
            }
            void Check(AssetResponse response, int status, string body)
            {
                if (response.StatusCode != status || Encoding.UTF8.GetString(response.Body.Span) != body)
                    throw new Exception($"Expected {status} '{body}', got {response.StatusCode} '{Encoding.UTF8.GetString(response.Body.Span)}'.");
            }
            var full = Get();
            Check(full, 200, "0123456789");
            using (var concurrentWriter = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                Check(Get(), 200, "0123456789");
            if (full.ContentType != "text/plain" || full.Headers!["Access-Control-Allow-Origin"] != "https://initial.example") throw new Exception("MIME/origin mismatch.");
            Check(Get(method: "POST"), 200, "0123456789");
            var head = Get(method: "HEAD");
            Check(head, 200, "");
            if (head.Headers!["Content-Length"] != "10") throw new Exception("HEAD lost entity length.");
            Check(Get(range: "bytes=2-4"), 206, "234");
            Check(Get(range: "bytes=-3"), 206, "789");
            Check(Get(range: "bytes=7-"), 206, "789");
            Check(Get(range: "bytes=8-100"), 206, "89");
            Check(Get(range: "bytes=99-nonsense,2-4"), 206, "234");
            Check(Get(method: "HEAD", range: "bytes=2-4"), 206, "234");
            foreach (var invalid in new[] { "bytes=-0", "bytes=20-", "bytes=3-2", "items=0-2", "bytes=+1-2", "bytes=0-18446744073709551616" })
            {
                var response = Get(range: invalid);
                Check(response, 416, "");
                if (response.ContentType != "" || response.Headers!.Count != 1 || response.Headers["Content-Range"] != "bytes */10")
                    throw new Exception("416 header behavior differs from pinned upstream.");
            }
            var multipart = Get(range: "bytes=0-1,8-9");
            var boundary = multipart.Headers!["Content-Type"].Split("boundary=")[1];
            Check(multipart, 200, $"\r\n--{boundary}\r\ncontent-type: text/plain\r\ncontent-range: bytes 0-1/10\r\n\r\n01\r\n--{boundary}\r\ncontent-type: text/plain\r\ncontent-range: bytes 8-9/10\r\n\r\n89\r\n--{boundary}\r\n");
            if (multipart.Headers!.ContainsKey("Content-Length")) throw new Exception("Multipart must omit content length.");
            var empty = Get(range: "bytes=");
            if (empty.StatusCode != 200 || !empty.Headers!["Content-Type"].StartsWith("multipart/byteranges;")) throw new Exception("Empty range list mismatch.");
            Check(Get(file: Path.Combine(root, "secret.txt")), 403, "");
            Check(Get(file: Path.Combine(root, "missing.txt")), 404, "");
            Check(Get(file: path + '\0'), 500, OperatingSystem.IsWindows()
                ? "strings passed to WinAPI cannot contain NULs" : "file name contained an unexpected NUL byte");
            Check(Get(file: Path.Combine(root, "folder")), OperatingSystem.IsLinux() ? 500 : 403,
                OperatingSystem.IsLinux() ? "Is a directory (os error 21)" : "");
            Check(Get(raw: "asset://localhost/" + Uri.EscapeDataString(root + "/child/../secret.txt")), 403, "");
            Check(Get(raw: "asset://localhost/" + Uri.EscapeDataString(root) + "/%2e%2e/secret.txt"), 403, "");
            File.WriteAllBytes(path, new byte[1_100_000]);
            var capped = Get(range: "bytes=0-");
            if (capped.Body.Length != 1_024_000 || capped.Headers!["Content-Range"] != "bytes 0-1023999/1100000") throw new Exception("Range cap mismatch.");
            foreach (var size in new[] { 0, 8191, 8192, 8193 })
            {
                var expected = Enumerable.Range(0, size).Select(static index => (byte)(index % 251)).ToArray();
                File.WriteAllBytes(path, expected);
                var response = Get();
                if (response.StatusCode != 200 || !response.Body.Span.SequenceEqual(expected) ||
                    response.Headers!["Content-Length"] != size.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    throw new Exception("Full response differs at the MIME prefix boundary.");
            }
            const int allocationSize = 16 * 1024 * 1024;
            using (var writer = File.OpenWrite(path)) writer.SetLength(allocationSize);
            _ = Get(); // Warm up parsing and file I/O before measuring the synchronous resolver.
            long before = GC.GetAllocatedBytesForCurrentThread();
            var large = Get();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (large.StatusCode != 200 || large.Body.Length != allocationSize || allocated > allocationSize + 128 * 1024)
                throw new Exception("Full response allocated more than one body plus fixed request overhead.");
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                writer.SetLength(8192);
                writer.Position = 8192;
                writer.WriteByte(42);
                writer.Flush();
                var captured = FileAssetProtocol.ReadFullBody(reader, 8192);
                if (captured.Length != 8192) throw new Exception("An append expanded the captured entity body.");
                writer.SetLength(8191);
                try { FileAssetProtocol.ReadFullBody(reader, 8192); throw new Exception("Truncation returned a partial success body."); }
                catch (EndOfStreamException) { }
            }
            using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                // NTFS needs an explicit sparse flag; Unix creates holes on extension.
                if (OperatingSystem.IsWindows() && !DeviceIoControl(writer.SafeFileHandle, 0x900c4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create the sparse asset fixture.");
                foreach (long size in new[] { (long)Array.MaxLength + 1, (long)int.MaxValue + 1, 5L * 1024 * 1024 * 1024 })
                {
                    writer.SetLength(size);
                    writer.Position = size - 1;
                    writer.WriteByte(42);
                    writer.Flush();
                    before = GC.GetAllocatedBytesForCurrentThread();
                    var rejected = Get();
                    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    Check(rejected, 413, "Asset file is too large for a buffered response. Request byte ranges instead.");
                    if (allocated > 128 * 1024 || rejected.Headers!.ContainsKey("Content-Length") ||
                        rejected.Headers["Access-Control-Allow-Origin"] != "https://initial.example")
                        throw new Exception("Oversized response allocated a body or lost error/origin metadata.");
                    var hugeHead = Get(method: "HEAD");
                    if (hugeHead.StatusCode != 200 || !hugeHead.Body.IsEmpty ||
                        hugeHead.Headers!["Content-Length"] != size.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        throw new Exception("Oversized HEAD lost entity metadata.");
                    var tail = Get(range: "bytes=" + (size - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "-");
                    if (tail.StatusCode != 206 || !tail.Body.Span.SequenceEqual(new byte[] { 42 }) ||
                        tail.Headers!["Content-Range"] != FormattableString.Invariant($"bytes {size - 1}-{size - 1}/{size}"))
                        throw new Exception("Oversized source tail range was not served.");
                    var hugeMultipart = Get(range: FormattableString.Invariant($"bytes=0-0,{size - 1}-{size - 1}"));
                    if (hugeMultipart.StatusCode != 200 || hugeMultipart.Body.Length > 1024 ||
                        !hugeMultipart.Headers!["Content-Type"].StartsWith("multipart/byteranges;", StringComparison.Ordinal))
                        throw new Exception("Oversized source multipart response was not bounded.");
                }
            }
            if (FileAssetProtocol.Create(default, "test") is not null) throw new Exception("Asset scheme enabled by default.");
            return Task.CompletedTask;
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint controlCode, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
