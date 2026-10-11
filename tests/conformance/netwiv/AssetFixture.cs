namespace NetWiv.Conformance;

internal sealed class AssetFixture : IDisposable
{
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "netwiv-conformance-assets", "netwiv-" + Guid.NewGuid().ToString("N"));
    internal AssetFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "folder"));
        File.WriteAllBytes(Path.Combine(DirectoryPath, "sample # 한글.txt"), "0123456789"u8.ToArray());
        File.WriteAllText(Path.Combine(DirectoryPath, "secret.txt"), "secret");
        foreach (var name in new[] { "rgba.png", "gray16.png", "palette.png", "multi.ico" })
        {
            using var source = typeof(AssetFixture).Assembly.GetManifestResourceStream("netwiv.image." + name)!;
            using var target = File.Create(Path.Combine(DirectoryPath, name));
            source.CopyTo(target);
        }
        File.WriteAllBytes(Path.Combine(DirectoryPath, "bytes.bin"), [.. Enumerable.Range(0, 256).Select(static value => (byte)value)]);
    }
    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
