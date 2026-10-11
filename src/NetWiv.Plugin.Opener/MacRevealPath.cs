using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NetWiv.Plugin.Opener;

internal static partial class MacRevealPath
{
    internal static string Resolve(string path)
    {
        if (path.Contains('\0')) throw new InvokeException("Invalid reveal path.");
        // Resolve parent links and '..' together; lexical normalization changes their meaning.
        var resolved = RealPath(path, 0);
        if (resolved == 0)
            throw new InvokeException("Cannot resolve reveal path: " + new Win32Exception(Marshal.GetLastPInvokeError()).Message);
        try { return Marshal.PtrToStringUTF8(resolved)!; }
        finally { Free(resolved); }
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint RealPath(string path, nint buffer);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "free")]
    private static partial void Free(nint memory);
}
