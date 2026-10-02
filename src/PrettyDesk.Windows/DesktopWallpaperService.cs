using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PrettyDesk.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PrettyDesk.Windows;

/// <summary>
/// <c>IDesktopWallpaper</c> access (FR-APPLY-1, FR-MON-1). Every COM call runs on one dedicated STA thread through a queue,
/// with a 5 s per-call timeout (a hung call abandons the thread and starts a fresh one) and retries that re-create the COM
/// object, which also recovers from an Explorer restart (FR-APPLY-6). Only when COM keeps failing does it fall back to
/// <c>SystemParametersInfo</c>, which sets one wallpaper for all monitors.
/// </summary>
public sealed partial class DesktopWallpaperService : IWallpaperSetter, IMonitorProvider, IDisposable
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    public const int MaxAttempts = 3;

    private readonly TimeProvider _time;
    private readonly ILogger<DesktopWallpaperService> _logger;
    private readonly object _workerLock = new();
    private Worker? _worker;

    public DesktopWallpaperService(TimeProvider time, ILogger<DesktopWallpaperService> logger)
    {
        _time = time;
        _logger = logger;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event Action? Changed;

    // ---- IWallpaperSetter -----------------------------------------------------------------------------------------

    public Task SetAsync(string monitorId, string path, CancellationToken cancellationToken = default) =>
        SetAsync(monitorId, path, DesktopPosition.Fill, cancellationToken);

    public async Task SetAsync(string monitorId, string path, DesktopPosition position, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunAsync(session =>
                {
                    session.SetPositionIfDifferent(position);
                    session.SetWallpaper(monitorId, path);
                    return true;
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogComFailed(attempt, ex);
                if (attempt >= MaxAttempts)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1))), _time, cancellationToken).ConfigureAwait(false);
            }
        }

        // COM is unusable: SPI_SETDESKWALLPAPER sets one wallpaper on every monitor, which is better than nothing (FR-APPLY-1).
        if (!SetViaSystemParametersInfo(path))
        {
            throw new InvalidOperationException("Windows refused to change the wallpaper.");
        }
    }

    public async Task<string?> GetAsync(string monitorId, CancellationToken cancellationToken = default)
    {
        var path = await RunAsync(session => session.GetWallpaper(monitorId), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrEmpty(path) ? null : path;
    }

    public Task<DesktopPosition> GetPositionAsync(CancellationToken cancellationToken = default) =>
        RunAsync(session => session.GetPosition(), cancellationToken);

    public Task SetPositionAsync(DesktopPosition position, CancellationToken cancellationToken = default) =>
        RunAsync(session =>
        {
            session.SetPositionIfDifferent(position);
            return true;
        }, cancellationToken);

    public Task<uint> GetBackgroundColorAsync(CancellationToken cancellationToken = default) =>
        RunAsync(session => session.GetBackgroundColor(), cancellationToken);

    public Task<SlideshowSnapshot> GetSlideshowAsync(CancellationToken cancellationToken = default) =>
        RunAsync(session => session.GetSlideshow(), cancellationToken);

    public Task SetSlideshowAsync(SlideshowSnapshot snapshot, CancellationToken cancellationToken = default) =>
        RunAsync(session =>
        {
            session.SetSlideshow(snapshot);
            return true;
        }, cancellationToken);

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        RunAsync(session =>
        {
            session.SetEnabled(enabled);
            return true;
        }, cancellationToken);

    public Task SetBackgroundColorAsync(uint colorRef, CancellationToken cancellationToken = default) =>
        RunAsync(session =>
        {
            session.SetBackgroundColor(colorRef);
            return true;
        }, cancellationToken);

    // ---- IMonitorProvider -----------------------------------------------------------------------------------------

    /// <summary>Monitors with their physical pixel rectangles; the process must be Per-Monitor-V2 DPI aware (FR-MON-1).</summary>
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        try
        {
            return RunAsync(session => session.GetMonitors(), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            LogMonitorQueryFailed(ex);
            return [];
        }
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        lock (_workerLock)
        {
            _worker?.Dispose();
            _worker = null;
        }
    }

    // ---- plumbing -------------------------------------------------------------------------------------------------

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Changed?.Invoke();

    private async Task<T> RunAsync<T>(Func<ComSession, T> work, CancellationToken cancellationToken)
    {
        var worker = CurrentWorker();
        try
        {
            // GetMonitors is also called synchronously by WPF view-model construction. Returning to the
            // caller's dispatcher here would deadlock that thread while it waits for this task.
            return await worker.Enqueue(work).WaitAsync(CallTimeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The STA thread is stuck inside a COM call. Abandon it; the next call gets a fresh thread and COM object.
            lock (_workerLock)
            {
                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                }
            }

            worker.Abandon();
            throw;
        }
    }

    private Worker CurrentWorker()
    {
        lock (_workerLock)
        {
            return _worker ??= new Worker();
        }
    }

    private static unsafe bool SetViaSystemParametersInfo(string path)
    {
        fixed (char* p = path)
        {
            return PInvoke.SystemParametersInfo(
                SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER,
                0,
                p,
                SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE | SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "IDesktopWallpaper call failed (attempt {Attempt}); recreating the COM object")]
    private partial void LogComFailed(int attempt, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not enumerate monitors")]
    private partial void LogMonitorQueryFailed(Exception ex);

    /// <summary>One STA thread plus its queue and COM session.</summary>
    private sealed class Worker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        private volatile bool _abandoned;

        public Worker()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "PrettyDesk.Wallpaper.STA" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public Task<T> Enqueue<T>(Func<ComSession, T> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() => Execute(work, completion));
            return completion.Task;
        }

        public void Abandon() => _abandoned = true;

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(2));
            _queue.Dispose();
        }

        private ComSession? _session;

        private void Execute<T>(Func<ComSession, T> work, TaskCompletionSource<T> completion)
        {
            try
            {
                _session ??= new ComSession();
                completion.TrySetResult(work(_session));
            }
#pragma warning disable CA1031 // Marshalled to the awaiting task; the session is discarded so the next call re-creates the COM object.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _session?.Dispose();
                _session = null;
                completion.TrySetException(ex);
            }
        }

        private unsafe void Loop()
        {
            PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED);
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable())
                {
                    if (_abandoned)
                    {
                        break;
                    }

                    item();
                }
            }
            finally
            {
                _session?.Dispose();
                PInvoke.CoUninitialize();
            }
        }
    }
}

/// <summary>Mirrors <c>DESKTOP_WALLPAPER_POSITION</c> (values verified against Microsoft Learn).</summary>
public enum DesktopPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

/// <summary>A live <c>IDesktopWallpaper</c>. Thread-affine: create, use and dispose on the STA thread only.</summary>
public sealed record SlideshowSnapshot(IReadOnlyList<string> Items, uint Options, uint Interval);

internal sealed unsafe class ComSession : IDisposable
{
    private IDesktopWallpaper* _wallpaper;
    private DesktopPosition? _position;

    public ComSession()
    {
        var clsid = typeof(DesktopWallpaper).GUID;
        var iid = IDesktopWallpaper.IID_Guid;
        IDesktopWallpaper* created;
        PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_LOCAL_SERVER, &iid, (void**)&created).ThrowOnFailure();
        _wallpaper = created;
    }

    public void SetWallpaper(string monitorId, string path) => _wallpaper->SetWallpaper(monitorId, path);

    public string? GetWallpaper(string monitorId)
    {
        _wallpaper->GetWallpaper(monitorId, out var result);
        return TakeString(result);
    }

    public DesktopPosition GetPosition()
    {
        _wallpaper->GetPosition(out var position);
        return (DesktopPosition)(int)position;
    }

    public void SetPositionIfDifferent(DesktopPosition position)
    {
        if (_position == position)
        {
            return;
        }

        if (GetPosition() != position)
        {
            _wallpaper->SetPosition((DESKTOP_WALLPAPER_POSITION)(int)position);
        }

        _position = position;
    }

    public uint GetBackgroundColor()
    {
        _wallpaper->GetBackgroundColor(out var color);
        return color.Value;
    }

    public void SetBackgroundColor(uint colorRef) => _wallpaper->SetBackgroundColor(new COLORREF(colorRef));

    public void SetEnabled(bool enabled) => _wallpaper->Enable(enabled);

    public SlideshowSnapshot GetSlideshow()
    {
        IShellItemArray* array = null;
        try
        {
            _wallpaper->GetSlideshow(&array);
            uint count;
            array->GetCount(&count);
            var items = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                IShellItem* item = null;
                try
                {
                    array->GetItemAt(i, &item);
                    PWSTR name;
                    item->GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, &name);
                    if (TakeString(name) is { } path)
                    {
                        items.Add(path);
                    }
                }
                finally
                {
                    if (item is not null)
                    {
                        item->Release();
                    }
                }
            }

            DESKTOP_SLIDESHOW_OPTIONS options;
            uint interval;
            _wallpaper->GetSlideshowOptions(&options, &interval);
            return new SlideshowSnapshot(items, (uint)options, interval);
        }
        finally
        {
            if (array is not null)
            {
                array->Release();
            }
        }
    }

    public void SetSlideshow(SlideshowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Items.Count == 0)
        {
            throw new ArgumentException("A slideshow needs its original source items.", nameof(snapshot));
        }

        var pidls = new nint[snapshot.Items.Count];
        IShellItemArray* array = null;
        try
        {
            for (var i = 0; i < pidls.Length; i++)
            {
                PInvoke.SHParseDisplayName(snapshot.Items[i], null, out var pidl, 0).ThrowOnFailure();
                pidls[i] = (nint)pidl;
            }

            fixed (nint* pointers = pidls)
            {
                PInvoke.SHCreateShellItemArrayFromIDLists((uint)pidls.Length, (ITEMIDLIST**)pointers, &array).ThrowOnFailure();
            }

            _wallpaper->SetSlideshow(array);
            _wallpaper->SetSlideshowOptions((DESKTOP_SLIDESHOW_OPTIONS)snapshot.Options, snapshot.Interval);
            _wallpaper->Enable(true);
        }
        finally
        {
            if (array is not null)
            {
                array->Release();
            }

            foreach (var pidl in pidls)
            {
                PInvoke.CoTaskMemFree((void*)pidl);
            }
        }
    }

    public List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        _wallpaper->GetMonitorDevicePathCount(out var count);
        for (uint i = 0; i < count; i++)
        {
            _wallpaper->GetMonitorDevicePathAt(i, out var pathPointer);
            var id = TakeString(pathPointer);
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            try
            {
                _wallpaper->GetMonitorRECT(id, out var rect);
                var width = rect.right - rect.left;
                var height = rect.bottom - rect.top;
                if (width > 0 && height > 0)
                {
                    // The primary monitor's top-left corner is the origin of the virtual screen by definition.
                    monitors.Add(new MonitorInfo(id, rect.left, rect.top, width, height, IsPrimary: rect.left == 0 && rect.top == 0));
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // A device path that is not currently attached has no rectangle; skip it.
            }
        }

        return monitors;
    }

    public void Dispose()
    {
        if (_wallpaper is not null)
        {
            _wallpaper->Release();
            _wallpaper = null;
        }
    }

    private static string? TakeString(PWSTR pointer)
    {
        if (pointer.Value is null)
        {
            return null;
        }

        var text = pointer.ToString();
        PInvoke.CoTaskMemFree(pointer.Value);
        return text;
    }
}
