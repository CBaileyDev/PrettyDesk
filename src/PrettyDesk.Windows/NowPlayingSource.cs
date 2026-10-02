using System.Runtime.InteropServices;
using global::Windows.Media.Control;
using PrettyDesk.Core.Abstractions;

namespace PrettyDesk.Windows;

public enum MediaSessionState
{
    Empty,
    Available,
    Unavailable,
}

public sealed record MediaSessionInfo(MediaSessionState State, string? Title = null, string? Artist = null);

/// <summary>Transient Windows media-session metadata and explicit user playback controls.</summary>
public sealed class NowPlayingSource : IPlaybackControls
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    private async Task<GlobalSystemMediaTransportControlsSession?> SessionAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return _manager.GetCurrentSession();
    }

    public async Task<MediaSessionInfo> ReadAsync()
    {
        try
        {
            var session = await SessionAsync();
            if (session is null)
            {
                return new MediaSessionInfo(MediaSessionState.Empty);
            }

            var media = await session.TryGetMediaPropertiesAsync();
            return string.IsNullOrWhiteSpace(media.Title) ? new MediaSessionInfo(MediaSessionState.Empty) : new MediaSessionInfo(MediaSessionState.Available, media.Title, media.Artist);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            _manager = null;
            return new MediaSessionInfo(MediaSessionState.Unavailable);
        }
    }

    public async Task<bool> TogglePlayPauseAsync() => await SessionAsync() is { } session && await session.TryTogglePlayPauseAsync();

    public async Task<bool> NextAsync() => await SessionAsync() is { } session && await session.TrySkipNextAsync();
}
