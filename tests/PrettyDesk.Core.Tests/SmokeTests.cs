using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads() => typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("PrettyDesk.Core");
}
