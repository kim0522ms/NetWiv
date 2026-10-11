using NetWiv;
using NetWiv.Plugin.Log;

internal static class LogPluginTests
{
    internal static async Task Rotation()
    {
        using var app = new Builder().UseRuntime(new FakeRuntime()).Metadata(new("LogProbe", "1.0", "dev.netwiv.log." + Guid.NewGuid().ToString("N")))
            .Plugin(new LogPlugin()).Build();
        var directory = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppLog, "");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "LogProbe.log");
        try
        {
            using (var file = File.Create(path)) file.SetLength(5 * 1024 * 1024);
            await app.Handle.InvokeAsync("plugin:log|log", JsonValue.Parse("""{"level":3,"message":"rotation marker"}"""));
            Assert.True(File.Exists(path + ".1"), "A full log was not rotated.");
            Assert.True(new FileInfo(path).Length < 1024, "The active log kept growing.");
            Assert.Contains("rotation marker", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static async Task BoundedUnicodeRecords()
    {
        using var app = new Builder().UseRuntime(new FakeRuntime()).Metadata(new("LogProbe", "1.0", "dev.netwiv.log." + Guid.NewGuid().ToString("N")))
            .Plugin(new LogPlugin(1, 256, 2)).Build();
        var directory = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppLog, "");
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
                await app.Handle.InvokeAsync("plugin:log|log", JsonValue.Parse("{\"level\":3,\"message\":" + JsonValue.From(index + string.Concat(Enumerable.Repeat("한글😀", 100))).GetRawText() + "}")))));
            var files = Directory.GetFiles(directory, "LogProbe.log*");
            Assert.Equal(3, files.Length);
            foreach (var path in files)
            {
                Assert.True(new FileInfo(path).Length <= 256, "A rotated or active file exceeded the byte limit.");
                var text = new System.Text.UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
                Assert.Contains("[truncated]", text);
                Assert.True(text.EndsWith(Environment.NewLine), "A record was split across files.");
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static async Task Records()
    {
        using var app = new Builder().UseRuntime(new FakeRuntime()).Plugin(new LogPlugin()).Build();
        var directory = await app.Handle.ResolveDirectoryAsync((int)BaseDirectory.AppLog, "");
        var file = Path.Combine(directory, app.Handle.Metadata.Name + ".log");
        var marker = "NetWiv log test " + Guid.NewGuid().ToString("N");
        await app.Handle.InvokeAsync("plugin:log|log", JsonValue.Parse("{\"level\":3,\"message\":" + JsonValue.From(marker).GetRawText() + "}"));
        Assert.Contains(marker, File.ReadAllText(file));
        try
        {
            await app.Handle.InvokeAsync("plugin:log|log", JsonValue.Parse("""{"level":7,"message":"bad"}"""));
            throw new Exception("Invalid log level was accepted.");
        }
        catch (InvokeException) { }
    }
}
