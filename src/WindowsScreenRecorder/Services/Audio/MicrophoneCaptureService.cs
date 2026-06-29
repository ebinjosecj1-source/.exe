using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WindowsScreenRecorder.Core.Interfaces;

namespace WindowsScreenRecorder.Services.Audio;

/// <summary>
/// Captures microphone input via WASAPI exclusive or shared mode.
/// Optionally applies a basic noise gate (noise suppression) by zeroing
/// samples below an RMS threshold. Full WebRTC NS is available via the
/// Microsoft.CognitiveServices.Speech SDK if desired in a future upgrade.
/// </summary>
public sealed class MicrophoneCaptureService : IMicrophoneCaptureService
{
    private readonly ILogger<MicrophoneCaptureService> _logger;
    private WasapiCapture? _capture;
    private CancellationTokenSource? _cts;
    private volatile float _currentLevel;
    private bool _disposed;

    // Noise gate threshold — RMS level below which audio is considered noise
    private const float NoiseGateThreshold = 0.02f;

    public bool IsCapturing { get; private set; }
    public float CurrentLevel => _currentLevel;

    public MicrophoneCaptureService(ILogger<MicrophoneCaptureService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(
        string? deviceId,
        float volume,
        bool noiseSuppression,
        Func<AudioFrame, Task> dataCallback,
        CancellationToken cancellationToken)
    {
        if (IsCapturing)
            throw new InvalidOperationException("Microphone capture is already running.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        MMDevice device = GetMicrophoneDevice(deviceId);
        _logger.LogInformation(
            "Starting microphone capture on: {DeviceName} | NoiseSuppression={NS}",
            device.FriendlyName, noiseSuppression);

        // Use WASAPI shared mode to avoid conflicts with other apps
        _capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 20)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1) // Mono mic
        };

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _capture.DataAvailable += async (_, args) =>
        {
            if (args.BytesRecorded == 0) return;

            try
            {
                float rms = ComputeRms(args.Buffer, args.BytesRecorded);
                _currentLevel = Math.Min(1.0f, rms * 3.0f);

                byte[] pcm = args.Buffer[..args.BytesRecorded];

                // Apply noise gate
                if (noiseSuppression && rms < NoiseGateThreshold)
                    pcm = new byte[pcm.Length]; // silence the frame

                // Apply volume
                pcm = ApplyVolume(pcm, volume);

                // Upmix mono to stereo for the encoder (duplicate channels)
                pcm = MonoToStereo(pcm);

                var frame = new AudioFrame
                {
                    Data = pcm,
                    SampleRate = 48000,
                    Channels = 2,
                    BitsPerSample = 32,  // IEEE float
                    TimestampTicks = DateTime.UtcNow.Ticks
                };

                await dataCallback(frame).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing microphone frame");
            }
        };

        _capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
            {
                _logger.LogError(args.Exception, "Microphone capture stopped with error");
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

    private static MMDevice GetMicrophoneDevice(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (deviceId is not null)
        {
            try { return enumerator.GetDevice(deviceId); }
            catch { /* fall through */ }
        }
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }

    private static float ComputeRms(byte[] buffer, int count)
    {
        int samples = count / 4;
        double sumSq = 0;
        for (int i = 0; i < samples; i++)
        {
            float s = BitConverter.ToSingle(buffer, i * 4);
            sumSq += s * s;
        }
        return samples > 0 ? (float)Math.Sqrt(sumSq / samples) : 0f;
    }

    private static byte[] ApplyVolume(byte[] buffer, float volume)
    {
        if (Math.Abs(volume - 1.0f) < 0.001f) return buffer;
        var result = new byte[buffer.Length];
        int samples = buffer.Length / 4;
        for (int i = 0; i < samples; i++)
        {
            float s = BitConverter.ToSingle(buffer, i * 4) * volume;
            s = Math.Clamp(s, -1.0f, 1.0f);
            Buffer.BlockCopy(BitConverter.GetBytes(s), 0, result, i * 4, 4);
        }
        return result;
    }

    /// <summary>Duplicate each mono sample into left and right channels.</summary>
    private static byte[] MonoToStereo(byte[] mono)
    {
        int monoSamples = mono.Length / 4;
        var stereo = new byte[mono.Length * 2];
        for (int i = 0; i < monoSamples; i++)
        {
            // Left channel
            Buffer.BlockCopy(mono, i * 4, stereo, i * 8, 4);
            // Right channel (identical for center-panned mic)
            Buffer.BlockCopy(mono, i * 4, stereo, i * 8 + 4, 4);
        }
        return stereo;
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
