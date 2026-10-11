using System.Buffers;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetWiv.Plugin.SingleInstance;

/// <summary>Forwards a second desktop process's arguments to the first app instance.</summary>
public sealed class SingleInstancePlugin(
    Action<AppHandle, IReadOnlyList<string>, string>? onSecondInstance = null,
    IReadOnlyList<string>? arguments = null) : IPlugin
{
    private readonly Action<AppHandle, IReadOnlyList<string>, string>? _callback = onSecondInstance;
    private readonly IReadOnlyList<string>? _arguments = arguments;
    private readonly CancellationTokenSource _stop = new();
    private FileStream? _presence;
    private Task? _listener;
    private string? _pipeName;

    public string Name => "single-instance";
    public void Initialize(PluginContext context) { }

    public void Setup(AppHandle app)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.Metadata.Identifier)))[..24];
        _pipeName = "NetWiv-Instance-" + hash;
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
                ? runtime : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var directory = Path.Combine(root, "NetWiv", "single-instance");
        Directory.CreateDirectory(directory);
        var first = false;
        try
        {
            _presence = new FileStream(Path.Combine(directory, hash + ".lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            first = true;
        }
        catch (IOException) { }
        if (first)
        {
            _listener = Task.Run(() => ListenAsync(app, _pipeName, _stop.Token));
            return;
        }
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(5000);
            var args = _arguments ?? Environment.GetCommandLineArgs();
            var payload = Encode(args, Environment.CurrentDirectory);
            client.Write(payload);
            client.Flush();
            app.Exit(0);
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            throw new InvokeException("Could not forward this launch to the existing app instance: " + error.Message);
        }
    }

    private async Task ListenAsync(AppHandle app, string pipeName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);
                using var buffer = new MemoryStream();
                await server.CopyToAsync(buffer, token);
                if (buffer.Length > 1024 * 1024) continue;
                using var document = JsonDocument.Parse(buffer.ToArray());
                var root = document.RootElement;
                var args = root.GetProperty("args").EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
                var cwd = root.GetProperty("cwd").GetString() ?? string.Empty;
                var payload = root.Clone();
                app.Dispatch(() =>
                {
                    app.Emit("single-instance://new-instance", payload);
                    _callback?.Invoke(app, args, cwd);
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
            catch (InvalidOperationException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException)
            {
                Console.Error.WriteLine($"Single instance message failed: {error.Message}");
            }
        }
    }

    private static byte[] Encode(IReadOnlyList<string> args, string cwd)
    {
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject(); writer.WriteString("cwd", cwd);
            writer.WritePropertyName("args"); writer.WriteStartArray();
            foreach (var argument in args) writer.WriteStringValue(argument);
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return output.WrittenSpan.ToArray();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener?.GetAwaiter().GetResult(); }
        finally
        {
            _presence?.Dispose();
            _stop.Dispose();
        }
    }
}
