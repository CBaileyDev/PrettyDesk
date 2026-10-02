using NAudio.CoreAudioApi;
using NAudio.Wave;
using PrettyDesk.Core.Imaging;

namespace PrettyDesk.Windows;

/// <summary>Default-output WASAPI loopback. Samples are analyzed immediately, never saved or transmitted.</summary>
public sealed class PlaybackVisualizerSource(TimeProvider time) : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private double[] _levels = new double[AudioSpectrum.BandCount];
    private long _lastFrame;
    private string? _deviceId;
    private int _failed;
    public bool HasError { get; private set; }

    public double[] Levels => time.GetElapsedTime(Interlocked.Read(ref _lastFrame)) > TimeSpan.FromSeconds(1)
        ? new double[AudioSpectrum.BandCount] : Volatile.Read(ref _levels);

    public void StartOrRefreshDevice()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_capture is not null && _deviceId == device.ID && Volatile.Read(ref _failed) == 0)
            {
                return;
            }

            Stop();
            _deviceId = device.ID;
            _capture = new WasapiLoopbackCapture(device) { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) };
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null)
                {
                    HasError = true;
                    Volatile.Write(ref _failed, 1);
                }
            };
            _capture.StartRecording();
            HasError = false;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            Stop();
            HasError = true;
        }
    }

    public void Stop()
    {
        if (_capture is { } capture)
        {
            _capture = null;
            capture.DataAvailable -= OnData;
            capture.StopRecording();
            capture.Dispose();
        }

        _deviceId = null;
        Volatile.Write(ref _failed, 0);
        Volatile.Write(ref _levels, new double[AudioSpectrum.BandCount]);
    }

    public void Dispose() => Stop();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (!ReferenceEquals(sender, _capture))
        {
            return;
        }

        var timestamp = time.GetTimestamp();
        if (time.GetElapsedTime(Interlocked.Read(ref _lastFrame), timestamp) < TimeSpan.FromMilliseconds(67))
        {
            return;
        }

        const int frameBytes = sizeof(float) * 2;
        var count = Math.Min(1024, e.BytesRecorded / frameBytes);
        var samples = new float[count];
        var start = e.BytesRecorded - (count * frameBytes);
        for (var i = 0; i < count; i++)
        {
            samples[i] = (BitConverter.ToSingle(e.Buffer, start + (i * frameBytes)) + BitConverter.ToSingle(e.Buffer, start + (i * frameBytes) + sizeof(float))) / 2;
        }

        Volatile.Write(ref _levels, AudioSpectrum.Analyze(samples, 48000));
        Interlocked.Exchange(ref _lastFrame, timestamp);
    }
}
