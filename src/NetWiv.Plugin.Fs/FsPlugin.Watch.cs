using System.Text.Json;

namespace NetWiv.Plugin.Fs;

public sealed partial class FsPlugin
{
    private readonly object _watchGate = new();
    private readonly HashSet<WatchResource> _watches = [];
    private bool _disposed;

    public void Dispose()
    {
        WatchResource[] watches;
        lock (_watchGate) { _disposed = true; watches = [.. _watches]; _watches.Clear(); }
        foreach (var watch in watches) watch.Dispose();
    }

    private async Task<uint> Watch(InvokeContext context, JsonElement args, CancellationToken token)
    {
        var paths = Property(args, "paths");
        if (paths.ValueKind != JsonValueKind.Array || paths.GetArrayLength() == 0)
            throw new InvokeException("File watch requires a nonempty paths array.");
        var options = Options(args);
        if (options.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
            throw new InvokeException("Invalid file watch options.");
        var recursive = false;
        var delay = 0;
        if (options.ValueKind == JsonValueKind.Object)
        {
            if (options.TryGetProperty("recursive", out var value) && value.ValueKind != JsonValueKind.Null)
            {
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvokeException("File watch recursive must be a boolean.");
                recursive = value.GetBoolean();
            }
            if (options.TryGetProperty("delayMs", out value) && value.ValueKind != JsonValueKind.Null &&
                (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out delay) || delay < 0))
                throw new InvokeException("File watch delayMs must be an integer between zero and 2147483647.");
        }
        var roots = new List<string>();
        foreach (var path in paths.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (path.ValueKind != JsonValueKind.String) throw new InvokeException("File watch paths must be strings.");
            roots.Add(await Resolve(context, path.GetString()!, options, token));
        }
        var scope = context.IsTrusted ? null : Scope(context);
        var watch = new WatchResource(Channel.FromJson(context, Property(args, "onEvent")),
            path => scope is null || IsAllowed(context, scope, path), delay,
            resource => { lock (_watchGate) _watches.Remove(resource); });
        uint? rid = null;
        try
        {
            foreach (var root in roots.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                watch.Add(root, recursive);
            token.ThrowIfCancellationRequested();
            lock (_watchGate)
            {
                if (_disposed) throw new InvokeException("The file system plugin has stopped.");
                _watches.Add(watch);
            }
            rid = context.AddResource(watch);
            token.ThrowIfCancellationRequested();
            watch.Start();
            token.ThrowIfCancellationRequested();
            return rid.Value;
        }
        catch
        {
            if (rid.HasValue) context.App.Resources.Close(rid.Value);
            watch.Dispose();
            throw;
        }
    }

    private sealed class WatchResource(Channel channel, Func<string, bool> allowed, int delay, Action<WatchResource> removed) : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<FileSystemWatcher> _watchers = [];
        private readonly List<WatchRoot> _roots = [];
        private readonly Dictionary<string, WatchEvent> _pending = new(StringComparer.Ordinal);
        private Timer? _timer;
        private long _timerEpoch;
        private bool _disposed;

        public void Add(string root, bool recursive)
        {
            var directory = Directory.Exists(root);
            if (!directory && !File.Exists(root)) throw new FileNotFoundException("File watch path does not exist.");
            var physicalRoot = ResolveLinks(root);
            var watchDirectory = directory ? root : Path.GetDirectoryName(root)!;
            var identity = WatchRoot.Open(watchDirectory);
            _roots.Add(identity);
            bool Matches(string path) => directory || string.Equals(path, root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            bool RootUnchanged() => identity.IsCurrent() && string.Equals(ResolveLinks(root), physicalRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            FileSystemWatcher? current = null;
            var epoch = 0;
            var rearmTopology = directory && recursive;
            FileSystemWatcher Create()
            {
                var watcher = new FileSystemWatcher(watchDirectory)
                {
                    IncludeSubdirectories = directory && recursive,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes
                };
                watcher.Created += (_, e) => ReceiveCurrent(watcher, "create", [e.FullPath]);
                watcher.Deleted += (_, e) => ReceiveCurrent(watcher, "remove", [e.FullPath]);
                watcher.Changed += (_, e) => ReceiveCurrent(watcher, "modify", [e.FullPath]);
                watcher.Renamed += (_, e) => ReceiveCurrent(watcher, "rename", [e.OldFullPath, e.FullPath]);
                watcher.Error += (_, e) =>
                {
                    if (rearmTopology && e.GetException() is InternalBufferOverflowException) Rearm(watcher, null, []);
                    else Failed(e.GetException() is InternalBufferOverflowException, () => ReferenceEquals(current, watcher));
                };
                return watcher;
            }
            void ReceiveCurrent(FileSystemWatcher source, string kind, string[] paths)
            {
                if (rearmTopology && kind is "remove" or "rename") Rearm(source, kind, paths);
                else Receive(kind, paths, Matches, () => ReferenceEquals(current, source) && RootUnchanged());
            }
            void Rearm(FileSystemWatcher source, string? kind, string[] paths)
            {
                FileSystemWatcher? replacement = null;
                var revision = -1;
                try
                {
                    lock (_gate)
                    {
                        if (_disposed || !ReferenceEquals(current, source)) return;
                        if (!RootUnchanged()) throw new IOException("The file watch root changed.");
                        // Retire before scope filtering: an excluded directory can
                        // still move native watches outside the subscribed tree.
                        revision = ++epoch;
                        current = null;
                        _watchers.Remove(source);
                        _pending.Clear(); _timerEpoch++; _timer?.Dispose(); _timer = null;
                    }
                    // Native cancellation and setup can acquire their own locks.
                    // Neither may run while a callback is waiting on _gate.
                    source.Dispose();
                    replacement = Create();
                    var installed = false;
                    lock (_gate)
                    {
                        if (!_disposed && epoch == revision)
                        {
                            if (!RootUnchanged()) throw new IOException("The file watch root changed.");
                            current = replacement;
                            _watchers.Add(replacement);
                            installed = true;
                        }
                    }
                    if (!installed) { replacement.Dispose(); return; }
                    replacement.EnableRaisingEvents = true;
                    bool Active() => epoch == revision && ReferenceEquals(current, replacement) && RootUnchanged();
                    // A reset invalidates the stream even if its triggering path
                    // was denied: later authorized events may have been dropped.
                    Failed(true, Active);
                    if (kind is not null) Receive(kind, paths, Matches, Active);
                }
                catch (Exception)
                {
                    if (replacement is not null)
                    {
                        bool installed;
                        lock (_gate) installed = _watchers.Contains(replacement);
                        if (!installed) replacement.Dispose();
                    }
                    Failed(false, () => revision < 0 ? ReferenceEquals(current, source) : epoch == revision);
                }
            }
            current = Create();
            _watchers.Add(current);
        }

        public void Start()
        {
            FileSystemWatcher[] watchers;
            lock (_gate)
            {
                if (_disposed) throw new InvokeException("The file watch was closed before registration completed.");
                watchers = [.. _watchers];
            }
            foreach (var watcher in watchers) watcher.EnableRaisingEvents = true;
        }

        private void Receive(string kind, string[] paths, Func<string, bool> matches, Func<bool> rootUnchanged)
        {
            bool? failure = null;
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    if (!rootUnchanged()) return;
                    var visible = paths.Where(path => matches(path) && allowed(path)).ToArray();
                    if (visible.Length == 0) return;
                    var mode = kind == "rename" ? visible.Length == 2 ? "both" : visible[0] == paths[0] ? "from" : "to" : null;
                    var value = new WatchEvent(kind, visible, mode, rootUnchanged);
                    if (delay == 0) Send(value);
                    else
                    {
                        // A fixed batch window avoids starving consumers during continuous writes.
                        var key = kind + "\0" + string.Join('\0', visible);
                        if (_pending.Count >= 4096 && !_pending.ContainsKey(key)) failure = true;
                        else
                        {
                            _pending[key] = value;
                            if (_timer is null)
                            {
                                var revision = ++_timerEpoch;
                                _timer = new Timer(_ => Flush(revision), null, delay, Timeout.Infinite);
                            }
                        }
                    }
                }
                catch (Exception) { failure = false; }
            }
            if (failure.HasValue) Failed(failure.Value);
        }

        private void Flush(long revision)
        {
            var failed = false;
            lock (_gate)
            {
                if (_disposed || revision != _timerEpoch) return;
                _timerEpoch++; _timer?.Dispose(); _timer = null;
                var pending = _pending.Values.ToArray(); _pending.Clear();
                try { foreach (var value in pending) Send(value); }
                catch (Exception) { failed = true; }
            }
            if (failed) Failed(false);
        }

        private void Send(WatchEvent value)
        {
            // Recheck queued paths: scope and symlink targets may have changed during the delay.
            if (!value.RootUnchanged()) return;
            var paths = value.Paths.Where(allowed).ToArray();
            if (paths.Length == 0) return;
            var mode = value.Mode;
            if (mode == "both" && paths.Length == 1) mode = paths[0] == value.Paths[0] ? "from" : "to";
            channel.Send(Write(writer =>
            {
                writer.WriteStartObject(); writer.WritePropertyName("type"); writer.WriteStartObject();
                writer.WritePropertyName(value.Kind == "rename" ? "modify" : value.Kind); writer.WriteStartObject();
                writer.WriteString("kind", value.Kind == "rename" ? "rename" : "any");
                if (mode is not null) writer.WriteString("mode", mode);
                writer.WriteEndObject(); writer.WriteEndObject();
                writer.WritePropertyName("paths"); writer.WriteStartArray(); foreach (var path in paths) writer.WriteStringValue(path); writer.WriteEndArray();
                writer.WritePropertyName("attrs"); writer.WriteStartObject(); writer.WriteEndObject(); writer.WriteEndObject();
            }));
        }

        private void Failed(bool overflow, Func<bool>? active = null)
        {
            lock (_gate)
            {
                if (_disposed || active?.Invoke() == false) return;
                _pending.Clear(); _timerEpoch++; _timer?.Dispose(); _timer = null;
                try
                {
                    channel.Send(JsonValue.Parse(overflow
                        ? """{"type":"any","paths":[],"attrs":{"rescan":true}}"""
                        : """{"type":"other","paths":[],"attrs":{"error":"watch-failed"}}"""));
                }
                catch (Exception) { }
            }
            if (!overflow) Dispose();
        }

        public void Dispose()
        {
            FileSystemWatcher[] watchers;
            WatchRoot[] roots;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _timerEpoch++; _timer?.Dispose(); _timer = null; _pending.Clear();
                watchers = [.. _watchers];
                _watchers.Clear();
                roots = [.. _roots];
                _roots.Clear();
            }
            foreach (var watcher in watchers) watcher.Dispose();
            foreach (var root in roots) root.Dispose();
            try { channel.Dispose(); }
            catch (Exception) { } // A runtime stopping concurrently cannot receive the channel end.
            finally { removed(this); }
        }

        private sealed record WatchEvent(string Kind, string[] Paths, string? Mode, Func<bool> RootUnchanged);
    }
}
