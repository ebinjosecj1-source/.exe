using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WindowsScreenRecorder.Core.Interfaces;

namespace WindowsScreenRecorder.Services.Audio;

/// <summary>
/// Captures Windows system audio using WASAPI loopback mode.
/// WASAPI loopback records whatever is playing through the selected output device,
/// avoiding the need for third-party virtual audio drivers (like VB-Cable).
/// The captured PCM data is resampled to 48 kHz stereo for encoder compatibility.
/// </summary>
public sealed class SystemAudioCaptureService : ISystemAudioCaptureService
{
    private readonly ILogger<SystemAudioCaptureService> _logger;
    private WasapiLoopbackCapture? _capture;
    private WaveFloatTo16Provider? _resampler;
    private CancellationTokenSource? _cts;
    private volatile float _currentLevel;
    private bool _disposed;

    public bool IsCapturing { get; private set; }
    public float CurrentLevel => _currentLevel;

    public SystemAudioCaptureService(ILogger<SystemAudioCaptureService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(
        string? deviceId,
        float volume,
        Func<AudioFrame, Task> dataCallback,
        CancellationToken cancellationToken)
    {
        if (IsCapturing)
            throw new InvalidOperationException("System audio capture is already running.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        MMDevice device = GetDevice(deviceId);
        _logger.LogInformation(
            "Starting system audio capture on device: {DeviceName}", device.FriendlyName);

        _capture = new WasapiLoopbackCapture(device)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
        };

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _capture.DataAvailable += async (_, args) =>
        {
            if (args.BytesRecorded == 0) return;

            try
            {
                // Compute VU level from PCM float samples
                _currentLevel = ComputeLevel(args.Buffer, args.BytesRecorded);

                // Apply volume scaling
                byte[] scaled = ApplyVolume(args.Buffer, args.BytesRecorded, volume);

                var frame = new AudioFrame
                {
                    Data = scaled,
                    SampleRate = _capture.WaveFormat.SampleRate,
                    Channels = _capture.WaveFormat.Channels,
                    BitsPerSample = _capture.WaveFormat.BitsPerSample,
                    TimestampTicks = DateTime.UtcNow.Ticks
                };

                await dataCallback(frame).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing system audio frame");
            }
        };

        _capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
            {
                _logger.LogError(args.Exception, "System audio capture stopped with error");
                tcs.TrySetException(args.Exception);
            }
            else
            {
                tcs.TrySetResult(true);
            }
            IsCapturing = false;
        };

        IsCapturing = true;
        _capture.StartRecording();

        // Register cancellation to stop capture
        await using var reg = ct.Register(() => _capture?.StopRecording());

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            IsCapturing = false;
            _currentLevel = 0;
        }
    }

    public Task StopAsync()
    {
        _capture?.StopRecording();
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private static MMDevice GetDevice(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (deviceId is not null)
        {
            try
            {
                return enumerator.GetDevice(deviceId);
            }
            catch
            {
                // Fall through to default
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    /// <summary>Compute RMS level (0.0–1.0) from IEEE float PCM buffer.</summary>
    private static float ComputeLevel(byte[] buffer, int count)
    {
        int samples = count / 4;  // 4 bytes per IEEE float
        double sumSquares = 0;

        for (int i = 0; i < samples; i++)
        {
            float sample = BitConverter.ToSingle(buffer, i * 4);
            sumSquares += sample * sample;
        }

        return samples > 0
            ? Math.Min(1.0f, (float)Math.Sqrt(sumSquares / samples) * 3.0f)
            : 0;
    }

    /// <summary>Scale PCM float samples by the specified volume factor.</summary>
    private static byte[] ApplyVolume(byte[] buffer, int count, float volume)
    {
        if (Math.Abs(volume - 1.0f) < 0.001f)
            return buffer[..count];  // No-op if volume is 1.0

        var result = new byte[count];
        int samples = count / 4;
        for (int i = 0; i < samples; i++)
        {
            float sample = BitConverter.ToSingle(buffer, i * 4) * volume;
            sample = Math.Clamp(sample, -1.0f, 1.0f);
            Buffer.BlockCopy(BitConverter.GetBytes(sample), 0, result, i * 4, 4);
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _capture?.StopRecording();
        _capture?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
