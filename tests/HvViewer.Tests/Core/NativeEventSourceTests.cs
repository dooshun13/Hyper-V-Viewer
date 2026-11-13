using System.Runtime.Versioning;
using HvViewer.Core.Reading;

namespace HvViewer.Tests.Core;

[SupportedOSPlatform("windows")]
public class NativeEventSourceTests : EventSourceContractTests
{
    protected override IEventSource Create() => new NativeEventSource();
}
