using System.Runtime.InteropServices;

namespace HvViewer.Core.Reading.Native;

/// <summary>An event log API handle, closed with EvtClose.</summary>
internal sealed class EvtHandle : SafeHandle
{
    public EvtHandle(IntPtr value) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(value);

    public override bool IsInvalid => handle == IntPtr.Zero;

    public IntPtr Value => handle;

    protected override bool ReleaseHandle() => Wevtapi.EvtClose(handle);
}
