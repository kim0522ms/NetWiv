using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetWiv.Plugin.Fs;

// A watcher can follow a moved directory while reporting filenames under its old
// configured path. Keep a handle to that directory so path replacement cannot
// make events from the moved object look authorized again.
internal sealed partial class WatchRoot : IDisposable
{
    private readonly SafeHandle _handle;
    private readonly string _initialPath;
    private readonly (int Device, ulong Inode)? _macIdentity;

    private WatchRoot(SafeHandle handle, string directory)
    {
        _handle = handle;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                _initialPath = directory;
                _macIdentity = MacIdentity(handle) ?? throw new IOException("Cannot verify the file watch directory.");
            }
            else _initialPath = CurrentPath() ?? throw new IOException("Cannot verify the file watch directory.");
        }
        catch { handle.Dispose(); throw; }
    }

    public static WatchRoot Open(string directory)
    {
        SafeHandle handle;
        if (OperatingSystem.IsWindows())
            handle = CreateFile(directory, 0, 7, 0, 3, 0x02000000, 0); // Share read/write/delete; backup semantics.
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
        if (_handle.IsClosed) return false;
        if (OperatingSystem.IsMacOS())
        {
            using var current = new UnixDirectoryHandle(OpenDirectory(_initialPath, 0x100000 | 0x1000000));
            return !current.IsInvalid && _macIdentity == MacIdentity(current);
        }
        return Directory.Exists(_initialPath) && string.Equals(_initialPath, CurrentPath(),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
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

    public void Dispose() => _handle.Dispose();

    private sealed class UnixDirectoryHandle : SafeHandleMinusOneIsInvalid
    {
        public UnixDirectoryHandle(int descriptor) : base(true) => SetHandle(descriptor);
        protected override bool ReleaseHandle() => CloseDirectory(handle) == 0;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenDirectory(string path, int flags);
    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int CloseDirectory(nint descriptor);
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
