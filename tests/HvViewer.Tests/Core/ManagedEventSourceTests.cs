using System.Runtime.Versioning;
using HvViewer.Core.Reading;

namespace HvViewer.Tests.Core;

[SupportedOSPlatform("windows")]
public class ManagedEventSourceTests : EventSourceContractTests
{
    protected override IEventSource Create() => new ManagedEventSource();
}
