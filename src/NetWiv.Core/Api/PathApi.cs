using System.Text.Json;

namespace NetWiv;

public enum BaseDirectory
{
    Audio = 1,
    Cache = 2,
    Config = 3,
    Data = 4,
    LocalData = 5,
    Document = 6,
    Download = 7,
    Picture = 8,
    Public = 9,
    Video = 10,
    Resource = 11,
    Temp = 12,
    AppConfig = 13,
    AppData = 14,
    AppLocalData = 15,
    AppCache = 16,
    AppLog = 17,
    Desktop = 18,
    Executable = 19,
    Font = 20,
    Home = 21,
    Runtime = 22,
    Template = 23,
}

public static class PathApi
{
    public static string Separator => Path.DirectorySeparatorChar.ToString();
    public static string Delimiter => Path.PathSeparator.ToString();

    public static Task<string> AppConfigDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.AppConfig, cancellationToken: cancellationToken);

    public static Task<string> AppDataDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.AppData, cancellationToken: cancellationToken);

    public static Task<string> AppLocalDataDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.AppLocalData, cancellationToken: cancellationToken);

    public static Task<string> AppCacheDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.AppCache, cancellationToken: cancellationToken);

    public static Task<string> AppLogDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.AppLog, cancellationToken: cancellationToken);

    public static Task<string> AudioDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Audio, cancellationToken: cancellationToken);

    public static Task<string> CacheDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Cache, cancellationToken: cancellationToken);

    public static Task<string> ConfigDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Config, cancellationToken: cancellationToken);

    public static Task<string> DataDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Data, cancellationToken: cancellationToken);

    public static Task<string> DesktopDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Desktop, cancellationToken: cancellationToken);

    public static Task<string> DocumentDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Document, cancellationToken: cancellationToken);

    public static Task<string> DownloadDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Download, cancellationToken: cancellationToken);

    public static Task<string> ExecutableDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Executable, cancellationToken: cancellationToken);

    public static Task<string> FontDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Font, cancellationToken: cancellationToken);

    public static Task<string> HomeDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Home, cancellationToken: cancellationToken);

    public static Task<string> LocalDataDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.LocalData, cancellationToken: cancellationToken);

    public static Task<string> PictureDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Picture, cancellationToken: cancellationToken);

    public static Task<string> PublicDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Public, cancellationToken: cancellationToken);

    public static Task<string> ResourceDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Resource, cancellationToken: cancellationToken);

    public static Task<string> ResolveResourceAsync(
        AppHandle app,
        string resourcePath,
        CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Resource, resourcePath, cancellationToken);

    public static Task<string> RuntimeDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Runtime, cancellationToken: cancellationToken);

    public static Task<string> TemplateDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Template, cancellationToken: cancellationToken);

    public static Task<string> VideoDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Video, cancellationToken: cancellationToken);

    public static Task<string> TemporaryDirectoryAsync(AppHandle app, CancellationToken cancellationToken = default) =>
        ResolveDirectoryAsync(app, BaseDirectory.Temp, cancellationToken: cancellationToken);

    public static Task<string> ResolveDirectoryAsync(
        AppHandle app,
        BaseDirectory directory,
        string? path = null,
        CancellationToken cancellationToken = default) =>
        ReadStringAsync(app, "resolve_directory", ApiJson.Arguments(null, writer =>
        {
            writer.WriteNumber("directory", (int)directory);
            if (path is not null) writer.WriteString("path", path);
        }), cancellationToken);

    public static Task<string> ResolveAsync(
        AppHandle app,
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default) =>
        ReadPathsCommandAsync(app, "resolve", paths, cancellationToken);

    public static Task<string> JoinAsync(
        AppHandle app,
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default) =>
        ReadPathsCommandAsync(app, "join", paths, cancellationToken);

    public static Task<string> NormalizeAsync(
        AppHandle app,
        string path,
        CancellationToken cancellationToken = default) =>
        ReadPathCommandAsync(app, "normalize", path, cancellationToken);

    public static Task<string> DirectoryNameAsync(
        AppHandle app,
        string path,
        CancellationToken cancellationToken = default) =>
        ReadPathCommandAsync(app, "dirname", path, cancellationToken);

    public static Task<string> ExtensionNameAsync(
        AppHandle app,
        string path,
        CancellationToken cancellationToken = default) =>
        ReadPathCommandAsync(app, "extname", path, cancellationToken);

    public static Task<string> BaseNameAsync(
        AppHandle app,
        string path,
        string? extension = null,
        CancellationToken cancellationToken = default) =>
        ReadStringAsync(app, "basename", ApiJson.Arguments(null, writer =>
        {
            writer.WriteString("path", path);
            writer.WriteString("ext", extension);
        }), cancellationToken);

    public static async Task<bool> IsAbsoluteAsync(
        AppHandle app,
        string path,
        CancellationToken cancellationToken = default) =>
        (await app.InvokeAsync("plugin:path|is_absolute", ApiJson.Arguments(null, writer =>
            writer.WriteString("path", path)), cancellationToken)).GetBoolean();

    private static Task<string> ReadPathsCommandAsync(
        AppHandle app,
        string command,
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return ReadStringAsync(app, command, ApiJson.Arguments(null, writer =>
        {
            writer.WriteStartArray("paths");
            foreach (var path in paths) writer.WriteStringValue(path);
            writer.WriteEndArray();
        }), cancellationToken);
    }

    private static Task<string> ReadPathCommandAsync(
        AppHandle app,
        string command,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ReadStringAsync(app, command, ApiJson.Arguments(null, writer =>
            writer.WriteString("path", path)), cancellationToken);
    }

    private static async Task<string> ReadStringAsync(
        AppHandle app,
        string command,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        var result = await app.InvokeAsync($"plugin:path|{command}", arguments, cancellationToken);
        return result.GetString() ?? throw new InvokeException($"Path command '{command}' did not return a string.");
    }
}
