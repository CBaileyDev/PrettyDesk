using Shouldly;
using Xunit;

namespace PrettyDesk.App.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void Disposing_twice_is_harmless()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows-only: uses a named mutex");

        // Quit disposes the instance explicitly and Program's `using` disposes it again after app.Run() returns.
        using var instance = SingleInstance.TryAcquire();
        Assert.SkipWhen(instance is null, "A real PrettyDesk is running on this machine and owns the mutex");

        instance!.Listen(() => { });
        instance.Dispose();

        Should.NotThrow(instance.Dispose);
    }
}
