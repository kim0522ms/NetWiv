using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NetWiv.Platform.Windows.WebView2;

[GeneratedComClass]
internal sealed partial class ReadOnlyComStream : IComStream
{
    private const int STG_E_ACCESSDENIED = unchecked((int)0x80030005);
    private const int STG_E_INVALIDFUNCTION = unchecked((int)0x80030001);
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const int S_OK = 0;
    private const int S_FALSE = 1;

    private readonly ReadOnlyMemory<byte> _contents;
    private long _position;

    internal ReadOnlyComStream(ReadOnlyMemory<byte> contents)
    {
        _contents = contents;
    }

    public unsafe int Read(nint buffer, uint count, nint bytesRead)
    {
        if (buffer == 0 && count != 0)
        {
            return E_INVALIDARG;
        }

        int available = (int)Math.Min(count, (ulong)Math.Max(0, _contents.Length - _position));
        if (available != 0)
        {
            _contents.Span.Slice((int)_position, available).CopyTo(new Span<byte>((void*)buffer, available));
            _position += available;
        }

        if (bytesRead != 0)
        {
            *(uint*)bytesRead = (uint)available;
        }

        return available == count ? S_OK : S_FALSE;
    }

    public int Write(nint buffer, uint count, nint bytesWritten) => STG_E_ACCESSDENIED;

    public unsafe int Seek(long move, uint origin, nint newPosition)
    {
        long originPosition = origin switch
        {
            0 => 0,
            1 => _position,
            2 => _contents.Length,
            _ => -1,
        };
        if (originPosition < 0)
        {
            return E_INVALIDARG;
        }

        long position;
        try
        {
            position = checked(originPosition + move);
        }
        catch (OverflowException)
        {
            return E_INVALIDARG;
        }

        if (position < 0)
        {
            return E_INVALIDARG;
        }

        _position = position;
        if (newPosition != 0)
        {
            *(ulong*)newPosition = (ulong)position;
        }

        return S_OK;
    }

    public int SetSize(ulong newSize) => STG_E_ACCESSDENIED;

    public int CopyTo(IComStream stream, ulong count, nint bytesRead, nint bytesWritten) => STG_E_INVALIDFUNCTION;

    public int Commit(uint flags) => S_OK;

    public int Revert() => STG_E_INVALIDFUNCTION;

    public int LockRegion(ulong offset, ulong count, uint type) => STG_E_INVALIDFUNCTION;

    public int UnlockRegion(ulong offset, ulong count, uint type) => STG_E_INVALIDFUNCTION;

    public unsafe int Stat(nint stat, uint flags)
    {
        if (stat == 0)
        {
            return E_INVALIDARG;
        }

        // STATSTG: leave all fields zero except cbSize, whose offset is 16 on x64 and 8 on x86.
        int size = IntPtr.Size == 8 ? 80 : 72;
        new Span<byte>((void*)stat, size).Clear();
        *(ulong*)((byte*)stat + (IntPtr.Size == 8 ? 16 : 8)) = (ulong)_contents.Length;
        return S_OK;
    }

    public int Clone(out IComStream? stream)
    {
        var clone = new ReadOnlyComStream(_contents) { _position = _position };
        stream = clone;
        return S_OK;
    }
}
