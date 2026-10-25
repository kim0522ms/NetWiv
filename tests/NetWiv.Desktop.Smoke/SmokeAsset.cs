internal sealed class SmokeAsset : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netwiv-desktop-asset-" + Guid.NewGuid().ToString("N") + ".txt");
    internal SmokeAsset() => File.WriteAllText(Path, "NetWiv asset" + new string('x', 64 * 1024));
    public void Dispose() => File.Delete(Path);
}
