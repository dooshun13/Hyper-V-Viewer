using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using HvViewer.Core.Model;
using HvViewer.Core.Reading.Native;

namespace HvViewer.Core.Reading;

/// <summary>
/// Reads events by calling the Windows event log API directly. Fast: it renders only the basic
/// system fields and builds messages only when asked.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeEventSource : IEventSource, IDisposable
{
    private const int BatchSize = 256;
    private const int VariantSize = 16;      // sizeof(EVT_VARIANT) on x64
    private const int VariantTypeMask = 0x7F;

    // EVT_VARIANT_TYPE values we read
    private const uint VarString = 1, VarByte = 4, VarUInt16 = 6, VarUInt32 = 8, VarUInt64 = 10, VarFileTime = 17, VarHexInt64 = 21;

    private readonly ConcurrentDictionary<string, Lazy<EvtHandle?>> _publishers = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskNameCache _taskNames = new();

    // ---------- Query ----------

    public IEnumerable<EventRecordLite> Query(LogTarget log, string xpath, CancellationToken ct)
    {
        foreach (var (record, _) in Read(log, xpath, textTarget: null, ct)) yield return record;
    }

    public IEnumerable<(EventRecordLite Event, string? Text)> QueryWithText(LogTarget log, string xpath, TextTarget target, CancellationToken ct) =>
        Read(log, xpath, target, ct);

    private IEnumerable<(EventRecordLite, string?)> Read(LogTarget log, string xpath, TextTarget? textTarget, CancellationToken ct)
    {
        using var query = OpenQuery(log, xpath);
        using var systemContext = new EvtHandle(Wevtapi.EvtCreateRenderContext(0, null, Wevtapi.EvtRenderContextSystem));
        using var sourceContext = new EvtHandle(Wevtapi.EvtCreateRenderContext(1, ["Event/System/Provider/@EventSourceName"], Wevtapi.EvtRenderContextValues));
        using var buffer = new RenderBuffer();
        var handles = new IntPtr[BatchSize];

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (!Wevtapi.EvtNext(query.Value, BatchSize, handles, uint.MaxValue, 0, out var returned))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == Wevtapi.ErrorNoMoreItems) yield break;
                throw Map(log.Name, error);
            }

            // Take ownership of the whole batch first, so every handle is closed even if reading is cancelled part-way.
            var batch = new EvtHandle[returned];
            for (var i = 0; i < returned; i++) batch[i] = new EvtHandle(handles[i]);
            try
            {
                // Basic fields are cheap, so they are read one after another.
                var records = new EventRecordLite[returned];
                for (var i = 0; i < returned; i++) records[i] = ReadRecord(batch[i].Value, systemContext, sourceContext, buffer, log);

                // Building message text is the expensive part (about 100 us per event), so a batch is done in parallel.
                var texts = textTarget is { } target ? BuildTexts(batch, records, target, ct) : null;

                for (var i = 0; i < returned; i++) yield return (records[i], texts?[i]);
            }
            finally
            {
                foreach (var handle in batch) handle.Dispose();
            }
        }
    }

    private string?[] BuildTexts(EvtHandle[] batch, EventRecordLite[] records, TextTarget target, CancellationToken ct)
    {
        var texts = new string?[batch.Length];
        try
        {
            Parallel.For(0, batch.Length, new ParallelOptions { MaxDegreeOfParallelism = TextParallelism, CancellationToken = ct },
                i => texts[i] = BuildText(batch[i].Value, records[i], target, _threadBuffer.Value!));
        }
        catch (AggregateException ex)
        {
            // Callers expect the original error (for example "access denied"), not a wrapper around it.
            ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
        }
        return texts;
    }

    /// <summary>Half the processors (at least 2, at most 8) format messages, leaving the rest for the window.</summary>
    private static readonly int TextParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    private readonly ThreadLocal<RenderBuffer> _threadBuffer = new(() => new RenderBuffer(), trackAllValues: true);

    private static EvtHandle OpenQuery(LogTarget log, string xpath)
    {
        var flags = (log.IsFile ? Wevtapi.EvtQueryFilePath : Wevtapi.EvtQueryChannelPath)
                    | Wevtapi.EvtQueryReverseDirection;
        var handle = Wevtapi.EvtQuery(IntPtr.Zero, log.Name, xpath, flags);
        if (handle == IntPtr.Zero) throw Map(log.Name, Marshal.GetLastWin32Error());
        return new EvtHandle(handle);
    }

    private static EventRecordLite ReadRecord(IntPtr ev, EvtHandle systemContext, EvtHandle sourceContext, RenderBuffer buffer, LogTarget log)
    {
        Render(systemContext.Value, ev, Wevtapi.EvtRenderEventValues, buffer, log.Name);

        var provider = ReadString(buffer, Wevtapi.SysProviderName);
        var channel = ReadString(buffer, Wevtapi.SysChannel);
        var record = new EventRecordLite(
            LogName: string.IsNullOrEmpty(channel) ? log.Name : channel,
            RecordId: (long)ReadNumber(buffer, Wevtapi.SysEventRecordId),
            TimeCreatedUtc: ReadTime(buffer, Wevtapi.SysTimeCreated),
            EventId: (int)ReadNumber(buffer, Wevtapi.SysEventId),
            Level: (EventLevel)(byte)ReadNumber(buffer, Wevtapi.SysLevel),
            ProviderName: provider,
            EventSourceName: null,
            Computer: ReadString(buffer, Wevtapi.SysComputer),
            Task: (int)ReadNumber(buffer, Wevtapi.SysTask),
            Keywords: (long)ReadNumber(buffer, Wevtapi.SysKeywords));

        // The classic source name (for example "BugCheck") sits in an attribute the system context does not return.
        Render(sourceContext.Value, ev, Wevtapi.EvtRenderEventValues, buffer, log.Name);
        var source = ReadString(buffer, 0);
        return string.IsNullOrEmpty(source) || string.Equals(source, provider, StringComparison.OrdinalIgnoreCase)
            ? record
            : record with { EventSourceName = source };
    }

    private string? BuildText(IntPtr ev, EventRecordLite record, TextTarget target, RenderBuffer buffer)
    {
        switch (target)
        {
            case TextTarget.Message:
                // No message (publisher not installed, or none defined): search the event's data instead.
                return FormatMessageCore(ev, record.ProviderName, buffer) ?? DataText(ev, buffer, record.LogName);
            case TextTarget.Xml:
                return RenderXml(ev, buffer, record.LogName);
            default:
                var message = FormatMessageCore(ev, record.ProviderName, buffer);
                var xml = RenderXml(ev, buffer, record.LogName);
                var values = xml is null ? [] : DataPairs(xml).Select(p => p.Value);
                return string.Join(" ", new[] { message, record.ProviderName }.Concat(values).Where(s => !string.IsNullOrEmpty(s)));
        }
    }

    /// <summary>The event data values joined by spaces, or null when the event has none.</summary>
    private static string? DataText(IntPtr ev, RenderBuffer buffer, string log)
    {
        var xml = RenderXml(ev, buffer, log);
        if (xml is null) return null;
        var joined = string.Join(" ", DataPairs(xml).Select(p => p.Value));
        return joined.Length == 0 ? null : joined;
    }

    // ---------- Single events ----------

    public string? FormatMessage(LogTarget log, long recordId) =>
        WithRecord(log, recordId, (ev, buffer) =>
        {
            using var system = new EvtHandle(Wevtapi.EvtCreateRenderContext(0, null, Wevtapi.EvtRenderContextSystem));
            Render(system.Value, ev, Wevtapi.EvtRenderEventValues, buffer, log.Name);
            return FormatMessageCore(ev, ReadString(buffer, Wevtapi.SysProviderName), buffer);
        });

    public string? GetXml(LogTarget log, long recordId) =>
        WithRecord(log, recordId, (ev, buffer) => RenderXml(ev, buffer, log.Name));

    public IReadOnlyList<KeyValuePair<string, string>> GetEventData(LogTarget log, long recordId)
    {
        var xml = GetXml(log, recordId);
        return xml is null ? [] : DataPairs(xml);
    }

    private T? WithRecord<T>(LogTarget log, long recordId, Func<IntPtr, RenderBuffer, T?> action)
    {
        using var query = OpenQuery(log, $"*[System[EventRecordID={recordId}]]");
        var handles = new IntPtr[1];
        if (!Wevtapi.EvtNext(query.Value, 1, handles, uint.MaxValue, 0, out var returned) || returned == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 0 or Wevtapi.ErrorNoMoreItems) return default;
            throw Map(log.Name, error);
        }
        using var ev = new EvtHandle(handles[0]);
        using var buffer = new RenderBuffer();
        return action(ev.Value, buffer);
    }

    // ---------- Messages ----------

    /// <summary>
    /// Formats the message with one call to Windows into a reusable buffer. Windows does the formatting work on every call,
    /// so asking first "how big?" and then again "now fill it" would double the cost; only a too-small buffer costs a retry.
    /// </summary>
    private string? FormatMessageCore(IntPtr ev, string provider, RenderBuffer buffer)
    {
        var publisher = GetPublisher(provider);
        if (publisher is null) return null;

        if (!Wevtapi.EvtFormatMessage(publisher.Value, ev, 0, 0, IntPtr.Zero, Wevtapi.EvtFormatMessageEvent, (uint)(buffer.Size / 2), buffer.Ptr, out var chars))
        {
            if (Marshal.GetLastWin32Error() != Wevtapi.ErrorInsufficientBuffer || chars == 0) return null;
            buffer.Ensure(checked((int)chars * 2));
            if (!Wevtapi.EvtFormatMessage(publisher.Value, ev, 0, 0, IntPtr.Zero, Wevtapi.EvtFormatMessageEvent, (uint)(buffer.Size / 2), buffer.Ptr, out _))
                return null;
        }
        return Marshal.PtrToStringUni(buffer.Ptr)?.TrimEnd();
    }

    private EvtHandle? GetPublisher(string provider)
    {
        if (string.IsNullOrEmpty(provider)) return null;
        return _publishers.GetOrAdd(provider, name => new Lazy<EvtHandle?>(() =>
        {
            var handle = Wevtapi.EvtOpenPublisherMetadata(IntPtr.Zero, name, null, 0, 0);
            return handle == IntPtr.Zero ? null : new EvtHandle(handle);
        })).Value;
    }

    public string? GetTaskName(string provider, int task) => _taskNames.Get(provider, task);

    // ---------- Channels ----------

    public IReadOnlyList<ChannelInfo> GetChannels()
    {
        var channels = new List<ChannelInfo>();
        var enumHandle = Wevtapi.EvtOpenChannelEnum(IntPtr.Zero, 0);
        if (enumHandle == IntPtr.Zero) throw Map("channels", Marshal.GetLastWin32Error());
        using var enumerator = new EvtHandle(enumHandle);

        using var buffer = new RenderBuffer();
        while (true)
        {
            if (!Wevtapi.EvtNextChannelPath(enumerator.Value, (uint)(buffer.Size / 2), buffer.Ptr, out var used))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == Wevtapi.ErrorInsufficientBuffer) { buffer.Ensure((int)(used * 2)); continue; }
                if (error == Wevtapi.ErrorNoMoreItems) break;
                throw Map("channels", error);
            }

            var name = Marshal.PtrToStringUni(buffer.Ptr)!;
            channels.Add(ReadChannel(name));
        }
        return channels;
    }

    private static ChannelInfo ReadChannel(string name)
    {
        var config = Wevtapi.EvtOpenChannelConfig(IntPtr.Zero, name, 0);
        if (config == IntPtr.Zero) return new ChannelInfo(name, ChannelType.Operational, false);
        using var handle = new EvtHandle(config);
        using var buffer = new RenderBuffer(64);

        var enabled = ReadConfigNumber(handle, Wevtapi.ChannelConfigEnabled, buffer) != 0;
        var type = (ChannelType)Math.Clamp((int)ReadConfigNumber(handle, Wevtapi.ChannelConfigType, buffer), 0, 3);
        return new ChannelInfo(name, type, enabled);
    }

    private static uint ReadConfigNumber(EvtHandle config, int property, RenderBuffer buffer)
    {
        if (!Wevtapi.EvtGetChannelConfigProperty(config.Value, property, 0, (uint)buffer.Size, buffer.Ptr, out var used))
        {
            if (Marshal.GetLastWin32Error() != Wevtapi.ErrorInsufficientBuffer) return 0;
            buffer.Ensure((int)used);
            if (!Wevtapi.EvtGetChannelConfigProperty(config.Value, property, 0, (uint)buffer.Size, buffer.Ptr, out _)) return 0;
        }
        return (uint)Marshal.ReadInt32(buffer.Ptr);
    }

    // ---------- Rendering helpers ----------

    private static void Render(IntPtr context, IntPtr ev, uint flags, RenderBuffer buffer, string log)
    {
        if (Wevtapi.EvtRender(context, ev, flags, (uint)buffer.Size, buffer.Ptr, out var used, out _)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != Wevtapi.ErrorInsufficientBuffer) throw Map(log, error);

        buffer.Ensure((int)used);
        if (!Wevtapi.EvtRender(context, ev, flags, (uint)buffer.Size, buffer.Ptr, out _, out _))
            throw Map(log, Marshal.GetLastWin32Error());
    }

    private static string? RenderXml(IntPtr ev, RenderBuffer buffer, string log)
    {
        Render(IntPtr.Zero, ev, Wevtapi.EvtRenderEventXml, buffer, log);
        return Marshal.PtrToStringUni(buffer.Ptr);
    }

    private static string ReadString(RenderBuffer buffer, int index)
    {
        var type = (uint)Marshal.ReadInt32(buffer.Ptr, index * VariantSize + 12) & VariantTypeMask;
        if (type != VarString) return string.Empty;
        var pointer = Marshal.ReadIntPtr(buffer.Ptr, index * VariantSize);
        return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer) ?? string.Empty;
    }

    private static ulong ReadNumber(RenderBuffer buffer, int index)
    {
        var offset = index * VariantSize;
        var type = (uint)Marshal.ReadInt32(buffer.Ptr, offset + 12) & VariantTypeMask;
        return type switch
        {
            VarByte => Marshal.ReadByte(buffer.Ptr, offset),
            VarUInt16 => (ushort)Marshal.ReadInt16(buffer.Ptr, offset),
            VarUInt32 => (uint)Marshal.ReadInt32(buffer.Ptr, offset),
            VarUInt64 or VarHexInt64 => (ulong)Marshal.ReadInt64(buffer.Ptr, offset),
            _ => 0,
        };
    }

    private static DateTime ReadTime(RenderBuffer buffer, int index)
    {
        var offset = index * VariantSize;
        var type = (uint)Marshal.ReadInt32(buffer.Ptr, offset + 12) & VariantTypeMask;
        return type == VarFileTime
            ? DateTime.FromFileTimeUtc(Marshal.ReadInt64(buffer.Ptr, offset))
            : DateTime.MinValue;
    }

    /// <summary>Event data as name/value pairs, from EventData or UserData.</summary>
    internal static IReadOnlyList<KeyValuePair<string, string>> DataPairs(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return [];
            var pairs = new List<KeyValuePair<string, string>>();

            var eventData = root.Elements().FirstOrDefault(e => e.Name.LocalName == "EventData");
            if (eventData is not null)
            {
                var n = 0;
                foreach (var data in eventData.Elements().Where(e => e.Name.LocalName == "Data"))
                {
                    n++;
                    var name = (string?)data.Attribute("Name");
                    pairs.Add(new(string.IsNullOrEmpty(name) ? $"Data{n}" : name, data.Value));
                }
            }

            var userData = root.Elements().FirstOrDefault(e => e.Name.LocalName == "UserData");
            var payload = userData?.Elements().FirstOrDefault();
            if (payload is not null)
                pairs.AddRange(payload.Elements().Select(e => new KeyValuePair<string, string>(e.Name.LocalName, e.Value)));

            return pairs;
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    private static EventSourceException Map(string log, int error) => error switch
    {
        Wevtapi.ErrorAccessDenied => new LogAccessDeniedException(log, new Win32Exception(error)),
        Wevtapi.ErrorEvtChannelNotFound or Wevtapi.ErrorFileNotFound or Wevtapi.ErrorPathNotFound
            => new LogNotFoundException(log, new Win32Exception(error)),
        _ => new EventSourceException(log, new Win32Exception(error).Message, new Win32Exception(error)),
    };

    public void Dispose()
    {
        foreach (var publisher in _publishers.Values)
            if (publisher.IsValueCreated) publisher.Value?.Dispose();
        _publishers.Clear();

        foreach (var buffer in _threadBuffer.Values) buffer.Dispose();
        _threadBuffer.Dispose();
    }

    /// <summary>Native memory used for EvtRender output. Grows when Windows asks for more.</summary>
    private sealed class RenderBuffer(int initialSize = 8192) : IDisposable
    {
        public IntPtr Ptr { get; private set; } = Marshal.AllocHGlobal(initialSize);

        public int Size { get; private set; } = initialSize;

        public void Ensure(int size)
        {
            if (size <= Size) return;
            Ptr = Marshal.ReAllocHGlobal(Ptr, size);
            Size = size;
        }

        public void Dispose()
        {
            if (Ptr != IntPtr.Zero) Marshal.FreeHGlobal(Ptr);
            Ptr = IntPtr.Zero;
        }
    }
}
