namespace NetWiv.Cli;

internal static class StarterIcons
{
    internal static void CopyTo(string backend)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "templates", "react-app", "src-netwiv", "icons");
        var destination = Path.Combine(backend, "icons");
        Directory.CreateDirectory(destination);
        foreach (var name in new[] { "icon.png", "icon.ico", "icon.icns" })
        {
            var path = Path.Combine(destination, name);
            // In particular, init --force must preserve an application's artwork.
            if (!File.Exists(path)) File.Copy(Path.Combine(source, name), path);
        }
    }
}
