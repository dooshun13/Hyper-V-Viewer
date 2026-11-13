using System.Runtime.InteropServices;

namespace HvViewer.Core.Reading.Native;

/// <summary>Read-only subset of the Windows event log API (wevtapi.dll).</summary>
internal static class Wevtapi
{
    private const string Dll = "wevtapi.dll";

    // EvtQuery flags
    public const uint EvtQueryChannelPath = 0x1;
    public const uint EvtQueryFilePath = 0x2;
    public const uint EvtQueryReverseDirection = 0x200;
    public const uint EvtQueryTolerateQueryErrors = 0x1000;

    // EvtRender flags
    public const uint EvtRenderEventValues = 0;
    public const uint EvtRenderEventXml = 1;
    public const uint EvtRenderContextValues = 0;
    public const uint EvtRenderContextSystem = 1;

    public const uint EvtFormatMessageEvent = 1;

    // EVT_SYSTEM_PROPERTY_ID
    public const int SysProviderName = 0;
    public const int SysEventId = 2;
    public const int SysLevel = 4;
    public const int SysTask = 5;
    public const int SysKeywords = 7;
    public const int SysTimeCreated = 8;
    public const int SysEventRecordId = 9;
    public const int SysChannel = 14;
    public const int SysComputer = 15;

    // EVT_CHANNEL_CONFIG_PROPERTY_ID
    public const int ChannelConfigEnabled = 0;
    public const int ChannelConfigType = 2;

    // Win32 errors
    public const int ErrorFileNotFound = 2;
    public const int ErrorPathNotFound = 3;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInsufficientBuffer = 122;
    public const int ErrorNoMoreItems = 259;
    public const int ErrorEvtChannelNotFound = 15007;

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr EvtQuery(IntPtr session, string path, string query, uint flags);

    [DllImport(Dll, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtNext(IntPtr resultSet, uint eventsSize, [Out] IntPtr[] events, uint timeout, uint flags, out uint returned);

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr EvtCreateRenderContext(uint valuePathsCount, string[]? valuePaths, uint flags);

    [DllImport(Dll, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtRender(IntPtr context, IntPtr fragment, uint flags, uint bufferSize, IntPtr buffer, out uint bufferUsed, out uint propertyCount);

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr EvtOpenPublisherMetadata(IntPtr session, string publisherId, string? logFilePath, uint locale, uint flags);

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtFormatMessage(IntPtr publisherMetadata, IntPtr eventHandle, uint messageId, uint valueCount, IntPtr values, uint flags, uint bufferSize, IntPtr buffer, out uint bufferUsed);

    [DllImport(Dll, SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr EvtOpenChannelEnum(IntPtr session, uint flags);

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtNextChannelPath(IntPtr channelEnum, uint bufferSize, IntPtr buffer, out uint bufferUsed);

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr EvtOpenChannelConfig(IntPtr session, string channelPath, uint flags);

    [DllImport(Dll, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtGetChannelConfigProperty(IntPtr config, int propertyId, uint flags, uint bufferSize, IntPtr buffer, out uint bufferUsed);

    [DllImport(Dll, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtClose(IntPtr handle);
}
