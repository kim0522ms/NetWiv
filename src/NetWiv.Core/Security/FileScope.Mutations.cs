// Behavioral port of tauri 2.11.5 src/scope/fs.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: MIT OR Apache-2.0
using System.Text;

namespace NetWiv;

public enum FileScopeEventKind { PathAllowed, PathForbidden }
public sealed record FileScopeEvent(FileScopeEventKind Kind, string Path);

public sealed partial class FileScope
{
    private readonly object _eventGate = new();
    private readonly Dictionary<uint, Action<FileScopeEvent>> _listeners = [];
    private readonly Queue<Action> _pending = new();
    private bool _emitting;
    private int _nextListenerId = -1;

    public void AllowFile(string path) => Extend(path, false, false, false);
    public void AllowDirectory(string path, bool recursive) => Extend(path, true, recursive, false);
    public void ForbidFile(string path) => Extend(path, false, false, true);
    public void ForbidDirectory(string path, bool recursive) => Extend(path, true, recursive, true);

    private void Extend(string path, bool directory, bool recursive, bool forbidden)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = NormalizeSeparators(path);
        var root = Path.GetPathRoot(normalized) ?? string.Empty;
        normalized = Combine(root, SplitComponents(normalized[root.Length..]).Where(component => component != ".").ToArray());
        var variants = new HashSet<string>(StringComparer.Ordinal) { normalized };
        if (OperatingSystem.IsWindows() && TryRemoveVerbatimDiskPrefix(normalized, out var simplified)) variants.Add(simplified);
        var canonical = CanonicalizeParent(normalized);
        if (canonical is not null) variants.Add(canonical);
        var additions = new Dictionary<string, LabelPattern>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            var escaped = Escape(variant);
            additions.TryAdd(escaped, new LabelPattern(escaped, _options, "file scope"));
            if (!directory) continue;
            var children = escaped + (variant.EndsWith(Path.DirectorySeparatorChar) ? "" : Path.DirectorySeparatorChar) + (recursive ? "**" : "*");
            additions.TryAdd(children, new LabelPattern(children, _options, "file scope"));
        }
        lock (_scopeGate)
        {
            var destination = forbidden ? _denied : _allowed;
            foreach (var addition in additions) destination.TryAdd(addition.Key, addition.Value);
        }
        Emit(new(forbidden ? FileScopeEventKind.PathForbidden : FileScopeEventKind.PathAllowed, path));
    }

    private static string Escape(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '*' or '?' or '[' or ']') result.Append('[').Append(character).Append(']');
            else result.Append(character);
        }
        return result.ToString();
    }

    public uint Listen(Action<FileScopeEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = unchecked((uint)Interlocked.Increment(ref _nextListenerId));
        ChangeListeners(() => _listeners.Add(id, handler));
        return id;
    }

    public uint Once(Action<FileScopeEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = unchecked((uint)Interlocked.Increment(ref _nextListenerId));
        var fired = 0;
        ChangeListeners(() => _listeners.Add(id, value =>
        {
            if (Interlocked.Exchange(ref fired, 1) != 0) return;
            Unlisten(id);
            handler(value);
        }));
        return id;
    }

    public void Unlisten(uint id) => ChangeListeners(() => _listeners.Remove(id));

    private void ChangeListeners(Action action)
    {
        lock (_eventGate)
        {
            if (_emitting) _pending.Enqueue(() => ChangeListeners(action));
            else action();
        }
    }

    private void Emit(FileScopeEvent value)
    {
        Action<FileScopeEvent>[] handlers;
        lock (_eventGate)
        {
            if (_emitting) { _pending.Enqueue(() => Emit(value)); return; }
            _emitting = true;
            handlers = _listeners.Values.ToArray();
        }
        try { foreach (var handler in handlers) handler(value); }
        finally
        {
            Action[] pending;
            lock (_eventGate)
            {
                _emitting = false;
                pending = _pending.ToArray();
                _pending.Clear();
            }
            foreach (var action in pending) action();
        }
    }
}
