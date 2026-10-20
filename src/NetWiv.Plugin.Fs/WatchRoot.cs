using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetWiv.Plugin.Fs;

// A watcher can follow a moved directory while reporting filenames under its old
// configured path. Keep a handle to that directory so path replacement cannot
// make events from the moved object look authorized again.
internal sealed partial class WatchRoot : IDisposable
{
    private readonly object _gate = new();
    private readonly SafeHandle _handle;
    private readonly string _initialPath;
    private readonly (int Device, ulong Inode)? _macIdentity;
    private readonly List<SafeHandle> _ancestors = [];
    private SafeHandle? _movement;
    private bool _invalidated;

    private WatchRoot(SafeHandle handle, string directory)
    {
        _handle = handle;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                _initialPath = MacRealPath(directory);
                _macIdentity = MacIdentity(handle) ?? throw new IOException("Cannot verify the file watch directory.");
            }
            else _initialPath = CurrentPath() ?? throw new IOException("Cannot verify the file watch directory.");
            OpenMovementGuard();
            if (!IsCurrent()) throw new IOException("The file watch directory changed during registration.");
        }
        catch { Dispose(); throw; }
    }

    public static WatchRoot Open(string directory)
    {
        SafeHandle handle;
        if (OperatingSystem.IsWindows())
            handle = CreateFile(directory, 1, 3, 0, 3, 0x02000000, 0); // FILE_LIST_DIRECTORY; share read/write, blocking moves/deletion until close.
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var flags = OperatingSystem.IsLinux() ? 0x10000 | 0x80000 : 0x100000 | 0x1000000; // O_DIRECTORY | O_CLOEXEC.
            handle = new UnixDirectoryHandle(OpenDirectory(directory, flags));
        }
        else throw new PlatformNotSupportedException("File watch directory verification is unavailable.");
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new IOException("Cannot open the file watch directory for verification.");
        }
        return new WatchRoot(handle, directory);
    }

    public bool IsCurrent()
    {
        lock (_gate)
        {
            if (_invalidated || _handle.IsClosed) return false;
            try
            {
                var current = MovementUnchanged();
                if (current && OperatingSystem.IsMacOS())
                {
                    using var directory = new UnixDirectoryHandle(OpenDirectory(_initialPath, 0x100000 | 0x1000000));
                    current = !directory.IsInvalid && _macIdentity == MacIdentity(directory);
                }
                else if (current)
                    current = Directory.Exists(_initialPath) && string.Equals(_initialPath, CurrentPath(),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                if (!current) _invalidated = true;
                return current;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _invalidated = true;
                return false;
            }
        }
    }

    private unsafe void OpenMovementGuard()
    {
        for (var path = Path.GetDirectoryName(_initialPath); path is not null; path = Path.GetDirectoryName(path))
        {
            SafeHandle ancestor = OperatingSystem.IsWindows()
                ? CreateFile(path, 1, 3, 0, 3, 0x02000000, 0)
                : new UnixDirectoryHandle(OpenDirectory(path, OperatingSystem.IsLinux() ? 0x10000 | 0x80000 : 0x100000 | 0x1000000));
            if (ancestor.IsInvalid) { ancestor.Dispose(); throw new IOException("Cannot verify the file watch ancestors."); }
            _ancestors.Add(ancestor);
        }
        if (OperatingSystem.IsWindows()) return;
        _movement = new UnixDirectoryHandle(OperatingSystem.IsLinux() ? InotifyInit(0x800 | 0x80000) : CreateQueue());
        if (_movement.IsInvalid) throw new IOException("Cannot monitor file watch directory movement.");
        foreach (var directory in _ancestors.Prepend(_handle))
        {
            if (OperatingSystem.IsLinux())
            {
                // Subscribe to the held object, not a pathname that could have been replaced.
                var path = "/proc/self/fd/" + directory.DangerousGetHandle().ToInt64();
                if (InotifyAdd(_movement, path, 0x800 | 0x400 | 0x2000 | 0x01000000) < 0)
                    throw new IOException("Cannot monitor file watch directory movement.");
            }
            else
            {
                var change = new VnodeEvent { Ident = directory.DangerousGetHandle(), Filter = -4, Flags = 1 | 4 | 0x20, Notes = 1 | 0x20 | 0x40 };
                var timeout = new NativeTimespec();
                if (QueueEvents(_movement, &change, 1, null, 0, &timeout) < 0)
                    throw new IOException("Cannot monitor file watch directory movement.");
            }
        }
    }

    private unsafe bool MovementUnchanged()
    {
        if (_movement is null) return true;
        // inotify reads whole records; this also fits a maximum-length named event.
        var buffer = stackalloc byte[4096];
        while (true)
        {
            if (OperatingSystem.IsLinux())
            {
                var count = ReadMovement(_movement, buffer, 4096);
                if (count >= 0) return false; // Any queued self-event, overflow or unexpected EOF invalidates permanently.
                var error = Marshal.GetLastPInvokeError();
                if (error == 4) continue; // EINTR.
                return error == 11; // EAGAIN means the nonblocking queue is empty.
            }
            var value = new VnodeEvent();
            var timeout = new NativeTimespec();
            var events = QueueEvents(_movement, null, 0, &value, 1, &timeout);
            if (events >= 0) return events == 0; // Any vnode event or EV_ERROR invalidates permanently.
            if (Marshal.GetLastPInvokeError() != 4) return false;
        }
    }

    private static string MacRealPath(string path)
    {
        var buffer = Marshal.AllocHGlobal(1024); // Darwin PATH_MAX.
        try { return RealPath(path, buffer) != 0 ? Marshal.PtrToStringUTF8(buffer)! : throw new IOException("Cannot resolve the file watch directory."); }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static (int Device, ulong Inode)? MacIdentity(SafeHandle handle)
    {
        var result = RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? GetMacIntelIdentity(handle, out var stat) : GetMacIdentity(handle, out stat);
        return result == 0 ? (stat.Device, stat.Inode) : null;
    }

    private string? CurrentPath()
    {
        if (OperatingSystem.IsLinux())
            return new FileInfo("/proc/self/fd/" + _handle.DangerousGetHandle().ToInt64()).LinkTarget;
        const int capacity = 32768;
        var buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
        try
        {
            var length = GetWindowsPath(_handle, buffer, (uint)capacity, 0);
            return length > 0 && length < capacity ? Marshal.PtrToStringUni(buffer, (int)length) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _invalidated = true;
            _movement?.Dispose();
            foreach (var ancestor in _ancestors) ancestor.Dispose();
            _ancestors.Clear();
            _handle.Dispose();
        }
    }

    private sealed class UnixDirectoryHandle : SafeHandleMinusOneIsInvalid
    {
        public UnixDirectoryHandle(int descriptor) : base(true) => SetHandle(descriptor);
        protected override bool ReleaseHandle() => CloseDirectory(handle) == 0;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenDirectory(string path, int flags);
    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int CloseDirectory(nint descriptor);
    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RealPath(string path, nint buffer);
    [LibraryImport("libc", EntryPoint = "inotify_init1")]
    private static partial int InotifyInit(int flags);
    [LibraryImport("libc", EntryPoint = "inotify_add_watch", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int InotifyAdd(SafeHandle descriptor, string path, uint mask);
    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial nint ReadMovement(SafeHandle descriptor, byte* buffer, nuint count);
    [StructLayout(LayoutKind.Sequential)]
    private struct VnodeEvent
    {
        public nint Ident;
        public short Filter;
        public ushort Flags;
        public uint Notes;
        public nint Data;
        public nint UserData;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTimespec { public nint Seconds; public nint Nanoseconds; }
    [LibraryImport("libc", EntryPoint = "kqueue")]
    private static partial int CreateQueue();
    [LibraryImport("libc", EntryPoint = "kevent", SetLastError = true)]
    private static unsafe partial int QueueEvents(SafeHandle descriptor, VnodeEvent* changes, int changeCount, VnodeEvent* events, int eventCount, NativeTimespec* timeout);
    // Darwin's 64-bit struct stat is 144 bytes: dev_t at 0 and ino64_t at 8.
    // https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/stat.h
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct DarwinStat
    {
        [FieldOffset(0)] public int Device;
        [FieldOffset(8)] public ulong Inode;
    }

    [LibraryImport("libc", EntryPoint = "fstat")]
    private static partial int GetMacIdentity(SafeHandle descriptor, out DarwinStat stat);
    [LibraryImport("libc", EntryPoint = "fstat$INODE64")]
    private static partial int GetMacIntelIdentity(SafeHandle descriptor, out DarwinStat stat);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW")]
    private static partial uint GetWindowsPath(SafeHandle handle, nint buffer, uint length, uint flags);
}
