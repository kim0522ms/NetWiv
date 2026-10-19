using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetWiv.Platform.Desktop;

internal static unsafe partial class NativeMethods
{
    private const string Library = "netwiv_native";
    [StructLayout(LayoutKind.Sequential)]
    internal struct Buffer { internal byte* Data; internal nuint Length; }

    [LibraryImport(Library, EntryPoint = "netwiv_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersion();
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint Create(delegate* unmanaged[Cdecl]<nint, uint, ulong, byte*, nuint, byte*, nuint, int> callback, nint user);
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_run")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Run(nint runtime, byte* config, nuint length);
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_submit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Submit(nint runtime, ulong requestId, byte* json, nuint length, byte* binary, nuint binaryLength);
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_cancel")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Cancel(nint runtime, ulong requestId);
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_dispatch")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Dispatch(nint runtime, ulong requestId);
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_eval")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Eval(nint runtime, byte* label, nuint labelLength, ulong documentId, byte* script, nuint scriptLength);
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_respond_asset")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int RespondAsset(nint runtime, ulong requestId, byte* json, nuint jsonLength, byte* body, nuint bodyLength);
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_exit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Exit(nint runtime, int exitCode);
    [LibraryImport(Library, EntryPoint = "netwiv_runtime_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Destroy(nint runtime);
    [LibraryImport(Library, EntryPoint = "netwiv_last_error")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial Buffer LastError();
    [LibraryImport(Library, EntryPoint = "netwiv_buffer_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Free(Buffer buffer);
}
