using Cortex.Core.Flows;
using Cortex.Core.Memory;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;
using Xunit;

namespace Cortex.Core.Tests;

public sealed class ObservedLiveRunCompatibilityTests
{
    [Fact]
    public void Existing_live_host_constructor_remains_binary_compatible()
    {
        Type[] signature = [typeof(PinnedShadowProject), typeof(ShadowSessionBinding), typeof(string),
            typeof(OutboundBindings), typeof(IFlowCompletion), typeof(IMemoryReader), typeof(string), typeof(long),
            typeof(LiveRouteBinding), typeof(IBoundOutboundTransport), typeof(TransportSafetyCapabilities),
            typeof(Func<CancellationToken, Task>), typeof(int), typeof(bool), typeof(bool), typeof(MemberDmBoundTextBinding)];
        Assert.NotNull(typeof(ObservedLiveRun).GetConstructor(signature));
    }
}
