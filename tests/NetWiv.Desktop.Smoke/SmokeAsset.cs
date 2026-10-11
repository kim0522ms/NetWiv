internal sealed class SmokeAsset : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netwiv-desktop-asset-" + Guid.NewGuid().ToString("N") + ".txt");
    internal SmokeAsset() => File.WriteAllBytes(Path, "NetWiv asset"u8.ToArray());
    public void Dispose() => File.Delete(Path);
}
