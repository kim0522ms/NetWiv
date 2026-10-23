using System.Runtime.InteropServices;
using NetWiv;
using NetWiv.Platform.Desktop;

internal static class DefaultIconSmoke
{
    internal static int Run()
    {
        var result = 1;
        using var stream = typeof(DefaultIconSmoke).Assembly.GetManifestResourceStream("netwiv.default-icon")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var encoded = buffer.ToArray();
        using var app = new Builder().UseRuntime(new DesktopRuntime()).UseEmbeddedConfig().UseEmbeddedAssets()
            .Window("main", "NetWiv default icon", "quiet.html")
            .Setup(app =>
            {
                try { if (OperatingSystem.IsMacOS()) VerifyDockIcon(encoded); }
                catch (Exception error) { Console.WriteLine("FAIL " + error.Message); app.Handle.Exit(1); return; }
                _ = VerifyAsync(app.Handle);
            }).Build();
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
        app.Run();
        return result;

        async Task VerifyAsync(AppHandle handle)
        {
            try
            {
                var first = await AppApi.DefaultWindowIconAsync(handle) ?? throw new InvalidOperationException("Default icon is missing.");
                var second = await AppApi.DefaultWindowIconAsync(handle) ?? throw new InvalidOperationException("Second icon is missing.");
                if (first.ResourceId == second.ResourceId) throw new InvalidOperationException("Icon calls share ownership.");
                await first.CloseAsync();
                var expected = await Image.FromBytesAsync(handle, encoded);
                if (await second.SizeAsync() != await expected.SizeAsync() || !(await second.RgbaAsync()).SequenceEqual(await expected.RgbaAsync()))
                    throw new InvalidOperationException("Default icon differs from the embedded artwork.");
                await expected.CloseAsync();
                await second.CloseAsync();
                Console.WriteLine("PASS embedded default icon, native pixels, independent resources" + (OperatingSystem.IsMacOS() ? " and AppKit Dock image" : ""));
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error.Message); }
            finally { handle.Exit(result); }
        }
    }

    private static unsafe void VerifyDockIcon(byte[] encoded)
    {
        fixed (byte* bytes = encoded)
        {
            var data = Data(GetClass("NSData"), Selector("dataWithBytes:length:"), (nint)bytes, (nuint)encoded.Length);
            var expected = SendArgument(Send(GetClass("NSImage"), Selector("alloc")), Selector("initWithData:"), data);
            if (expected == 0) throw new InvalidOperationException("AppKit could not read the fixture.");
            try
            {
                var app = Send(GetClass("NSApplication"), Selector("sharedApplication"));
                var actual = Send(app, Selector("applicationIconImage"));
                if (actual == 0) throw new InvalidOperationException("AppKit application icon is missing.");
                Send(actual, Selector("retain"));
                try
                {
                    var actualPixels = ReadPixels(actual);
                    // AppKit normalizes Dock images (including their size). Apply the
                    // same normalization to the original PNG before comparing pixels.
                    SetIcon(app, Selector("setApplicationIconImage:"), expected);
                    if (!actualPixels.SequenceEqual(ReadPixels(Send(app, Selector("applicationIconImage")))))
                        throw new InvalidOperationException("AppKit application icon differs from the configured artwork.");
                }
                finally
                {
                    SetIcon(app, Selector("setApplicationIconImage:"), actual);
                    Send(actual, Selector("release"));
                }
            }
            finally { Send(expected, Selector("release")); }
        }
    }

    private static byte[] ReadPixels(nint image)
    {
        var data = Send(image, Selector("TIFFRepresentation"));
        if (data == 0) throw new InvalidOperationException("AppKit icon has no bitmap representation.");
        var bitmap = SendArgument(GetClass("NSBitmapImageRep"), Selector("imageRepWithData:"), data);
        if (bitmap == 0) throw new InvalidOperationException("AppKit icon has no bitmap.");
        var width = Send(bitmap, Selector("pixelsWide"));
        var height = Send(bitmap, Selector("pixelsHigh"));
        if (width <= 0 || height <= 0) throw new InvalidOperationException("AppKit icon is empty.");
        var space = Send(GetClass("NSColorSpace"), Selector("sRGBColorSpace"));
        var bytes = new byte[checked((int)(width * height) * 4 + 8)];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), checked((int)width));
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), checked((int)height));
        for (nint y = 0; y < height; y++)
        for (nint x = 0; x < width; x++)
        {
            var color = Color(bitmap, Selector("colorAtX:y:"), x, y);
            color = SendArgument(color, Selector("colorUsingColorSpace:"), space);
            Components(color, Selector("getRed:green:blue:alpha:"), out var r, out var g, out var b, out var a);
            var index = checked((int)(y * width + x) * 4 + 8);
            bytes[index] = (byte)Math.Round(r * 255);
            bytes[index + 1] = (byte)Math.Round(g * 255);
            bytes[index + 2] = (byte)Math.Round(b * 255);
            bytes[index + 3] = (byte)Math.Round(a * 255);
        }
        return bytes;
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
    private static extern nint GetClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
    private static extern nint Selector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint SendArgument(nint receiver, nint selector, nint argument);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetIcon(nint receiver, nint selector, nint image);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint Data(nint receiver, nint selector, nint bytes, nuint length);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint Color(nint receiver, nint selector, nint x, nint y);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void Components(nint receiver, nint selector, out double r, out double g, out double b, out double a);
}
