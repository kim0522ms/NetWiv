using System.Text.Json;

namespace NetWiv.Plugin.Fs;

public sealed partial class FsPlugin
{
    private const int MaximumDirectories = 128;
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
        var uniqueRoots = roots.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        if (uniqueRoots.Length > MaximumDirectories)
            throw new InvokeException("A file watch is limited to 128 directory registrations. Use narrower roots or nonrecursive watching.");
        var scope = context.IsTrusted ? null : Scope(context);
        var watch = new WatchResource(Channel.FromJson(context, Property(args, "onEvent")),
            path => scope is null || IsAllowed(context, scope, path), delay,
            resource => { lock (_watchGate) _watches.Remove(resource); });
        uint? rid = null;
        try
        {
            foreach (var root in uniqueRoots)
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
        private readonly List<Action> _starts = [];
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
            var anchor = WatchRoot.Open(watchDirectory);
            _roots.Add(anchor);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var entries = new Dictionary<string, DirectoryWatch>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var epoch = 0;
            bool Matches(string path) => directory || string.Equals(path, root,
                comparison);
            bool RootUnchanged() => anchor.IsCurrent() && string.Equals(ResolveLinks(root), physicalRoot, comparison);
            bool Active(DirectoryWatch entry) => entries.TryGetValue(entry.Path, out var current) &&
                ReferenceEquals(current, entry) && RootUnchanged() && entry.Identity.IsCurrent();
            DirectoryWatch Create(string path, WatchRoot identity)
            {
                // Recursive Linux watchers can silently stop after a directory is
                // moved out on .NET 9/10. Nonrecursive registrations also let each
                // directory's guard reject events from a relocated subtree.
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes
                };
                var entry = new DirectoryWatch(path, watcher, identity);
                watcher.Created += (_, e) => Changed(entry, "create", [e.FullPath]);
                watcher.Deleted += (_, e) => Changed(entry, "remove", [e.FullPath]);
                watcher.Changed += (_, e) => Changed(entry, "modify", [e.FullPath]);
                watcher.Renamed += (_, e) => Changed(entry, "rename", [e.OldFullPath, e.FullPath]);
                watcher.Error += (_, e) =>
                {
                    if (e.GetException() is InternalBufferOverflowException) Restart(entry);
                    else Failed(false, () => Active(entry));
                };
                return entry;
            }
            bool RegisterTree(string path, int revision, bool initial = false)
            {
                int remaining;
                lock (_gate)
                {
                    if (_disposed || epoch != revision || !RootUnchanged() || entries.ContainsKey(path)) return false;
                    remaining = MaximumDirectories - _watchers.Count;
                }
                var required = 0;
                void Count(string candidate, bool first = false)
                {
                    if (!first && File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint)) return;
                    bool registered;
                    lock (_gate) registered = entries.ContainsKey(candidate);
                    if (!registered && ++required > remaining)
                        throw new InvokeException("A file watch is limited to 128 directory registrations. Use narrower roots or nonrecursive watching.");
                    if (!directory || !recursive) return;
                    foreach (var child in Directory.EnumerateDirectories(candidate))
                    {
                        try { Count(child); }
                        catch (Exception error) when (error is DirectoryNotFoundException or FileNotFoundException) { }
                    }
                }
                // Reject an oversized discovery before allocating native queues,
                // threads and ancestor handles. Registration still checks the
                // limit atomically because the tree can change after this scan.
                Count(path, initial);
                return RegisterDirectory(path, revision, initial);
            }
            bool RegisterDirectory(string path, int revision, bool initial = false)
            {
                if (!initial && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return false;
                lock (_gate)
                {
                    if (_disposed || epoch != revision || !RootUnchanged() || entries.ContainsKey(path)) return false;
                    if (_watchers.Count >= MaximumDirectories)
                        throw new InvokeException("A file watch is limited to 128 directory registrations. Use narrower roots or nonrecursive watching.");
                }
                var identity = WatchRoot.Open(path);
                DirectoryWatch entry;
                try { entry = Create(path, identity); }
                catch { identity.Dispose(); throw; }
                var installed = false;
                try
                {
                    lock (_gate)
                    {
                        if (_disposed || epoch != revision || !RootUnchanged() || entries.ContainsKey(path)) return false;
                        if (_watchers.Count >= MaximumDirectories)
                            throw new InvokeException("A file watch is limited to 128 directory registrations. Use narrower roots or nonrecursive watching.");
                        entries.Add(path, entry);
                        _watchers.Add(entry.Watcher); _roots.Add(identity);
                        installed = true;
                    }
                    entry.Watcher.EnableRaisingEvents = true;
                    if (directory && recursive)
                    {
                        foreach (var child in Directory.EnumerateDirectories(path))
                        {
                            try { RegisterDirectory(child, revision); }
                            catch (Exception error) when (error is DirectoryNotFoundException or FileNotFoundException) { }
                        }
                    }
                    return true;
                }
                finally
                {
                    if (!installed) { entry.Watcher.Dispose(); identity.Dispose(); }
                }
            }
            DirectoryWatch[] Retire(string? path = null)
            {
                // Called under _gate; native cleanup happens after releasing it.
                var removedEntries = entries.Values.Where(entry => path is null || string.Equals(entry.Path, path, comparison) ||
                    entry.Path.StartsWith(Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar, comparison)).ToArray();
                foreach (var entry in removedEntries)
                {
                    entries.Remove(entry.Path);
                    _watchers.Remove(entry.Watcher); _roots.Remove(entry.Identity);
                }
                return removedEntries;
            }
            void Cleanup(DirectoryWatch[] retired)
            {
                foreach (var entry in retired) { entry.Watcher.Dispose(); entry.Identity.Dispose(); }
            }
            void Changed(DirectoryWatch source, string kind, string[] paths)
            {
                var revision = -1;
                try
                {
                    DirectoryWatch[] retired = [];
                    lock (_gate)
                    {
                        if (_disposed || !Active(source)) return;
                        revision = epoch;
                        if (directory && recursive && kind is "remove" or "rename") retired = Retire(paths[0]);
                    }
                    Cleanup(retired);
                    var added = false;
                    if (directory && recursive && kind is "create" or "rename")
                    {
                        var target = paths[^1];
                        if (Directory.Exists(target)) added = RegisterTree(target, revision);
                    }
                    bool Current() => epoch == revision && Active(source);
                    if (retired.Length > 0 || added) Failed(true, Current);
                    Receive(kind, paths, Matches, Current);
                }
                catch (Exception error) when (error is DirectoryNotFoundException or FileNotFoundException)
                {
                    // A newly reported directory may already have disappeared.
                    Failed(true, () => Active(source));
                }
                catch (Exception) { Failed(false, () => revision < 0 ? Active(source) : epoch == revision); }
            }
            void Restart(DirectoryWatch source)
            {
                var revision = -1;
                try
                {
                    DirectoryWatch[] retired;
                    lock (_gate)
                    {
                        if (_disposed || !Active(source)) return;
                        revision = ++epoch;
                        retired = Retire();
                    }
                    Cleanup(retired);
                    if (!RegisterTree(watchDirectory, revision, initial: true))
                        throw new IOException("File watch root changed during recovery.");
                    Failed(true, () => epoch == revision && RootUnchanged());
                }
                catch (Exception) { Failed(false, () => revision < 0 ? Active(source) : epoch == revision); }
            }
            _starts.Add(() =>
            {
                // Enable each parent before enumerating descendants so a new
                // child cannot fall permanently between enumeration and startup.
                if (!RegisterTree(watchDirectory, epoch, initial: true))
                    throw new InvokeException("File watch root changed before registration completed.");
            });
        }

        public void Start()
        {
            Action[] starts;
            lock (_gate)
            {
                if (_disposed) throw new InvokeException("The file watch was closed before registration completed.");
                starts = [.. _starts]; _starts.Clear();
            }
            foreach (var start in starts) start();
            lock (_gate)
                if (_disposed) throw new InvokeException("The file watch was closed before registration completed.");
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
                _starts.Clear();
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
        private sealed record DirectoryWatch(string Path, FileSystemWatcher Watcher, WatchRoot Identity);
    }
}
