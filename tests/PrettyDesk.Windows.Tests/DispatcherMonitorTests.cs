using Microsoft.Extensions.Logging.Abstractions;
using PrettyDesk.Core.Abstractions;
using Shouldly;
using Xunit;

namespace PrettyDesk.Windows.Tests;

public sealed class DispatcherMonitorTests
{
    [Fact]
    public void Synchronous_monitor_query_completes_on_a_non_pumping_UI_context()
    {
        WindowsOnly.Require();
        IReadOnlyList<MonitorInfo>? monitors = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingContext());
            try
            {
                using var service = new DesktopWallpaperService(TimeProvider.System, NullLogger<DesktopWallpaperService>.Instance);
                monitors = service.GetMonitors();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15)).ShouldBeTrue("A synchronous UI monitor query must not await the blocked UI context.");
        failure.ShouldBeNull(failure?.ToString());
        monitors.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Models the WPF dispatcher while its constructor is blocked in GetMonitors.
        }
    }
}
