using System.Runtime.InteropServices;

namespace PrettyDesk.Presentation.Services;

/// <summary>
/// Velopack channel names (docs/adr/0006). One channel per architecture and stability, so the x64 and arm64 packages of a release
/// never collide in the same GitHub release and each machine only ever sees packages built for it.
/// </summary>
public static class UpdateChannels
{
    public static string For(bool beta, Architecture architecture) =>
        $"win-{(architecture == Architecture.Arm64 ? "arm64" : "x64")}-{(beta ? "beta" : "stable")}";

    public static string ForThisMachine(bool beta) => For(beta, RuntimeInformation.ProcessArchitecture);
}
