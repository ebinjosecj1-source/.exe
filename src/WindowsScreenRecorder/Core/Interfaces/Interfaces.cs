using System.Windows;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Core.Interfaces;

// ─────────────────────────────────────────────────────────────
//  Recording orchestration
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Top-level orchestrator. Coordinates capture, encoding, and muxing.
/// </summary>
public interface IRecordingService
{
    RecordingState State { get; }
    RecordingStats CurrentStats { get; }

    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
    event EventHandler<RecordingStats>? StatsUpdated;

    Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task<string> StopAsync();   // Returns the output file path
    Task TakeScreenshotAsync(AppSettings settings);
}

// ─────────────────────────────────────────────────────────────
//  Screen capture
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Captures screen frames using Windows Graphics Capture API
/// with fallback to Desktop Duplication API.
/// </summary>
public interface IScreenCaptureService : IDisposable
{
    bool IsCapturing { get; }

    /// <summary>Start frame capture loop. Calls <paramref name="frameCallback"/> for each raw frame.</summary>
    Task StartAsync(
        CaptureMode mode,
        MonitorInfo? monitor,
        WindowInfo? window,
        Rect customRegion,
        int targetFps,
        Func<CaptureFrame, Task> frameCallback,
        CancellationToken cancellationToken);

    Task StopAsync();
}

/// <summary>One captured video frame (raw BGRA pixels).</summary>
public sealed class CaptureFrame : IDisposable
{
    public required byte[] Data { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required long TimestampTicks { get; init; }  // DateTime.UtcNow.Ticks

    public void Dispose() { /* frame pooling hook */ }
}

// ─────────────────────────────────────────────────────────────
//  Audio capture
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Captures Windows system audio via WASAPI loopback.
/// </summary>
public interface ISystemAudioCaptureService : IDisposable
{
    bool IsCapturing { get; }
    float CurrentLevel { get; }  // 0.0 – 1.0 VU level

    Task StartAsync(
        string? deviceId,
        float volume,
        Func<AudioFrame, Task> dataCallback,
        CancellationToken cancellationToken);

    Task StopAsync();
}

/// <summary>
/// Captures microphone audio via WASAPI capture.
/// </summary>
public interface IMicrophoneCaptureService : IDisposable
{
    bool IsCapturing { get; }
    float CurrentLevel { get; }

    Task StartAsync(
        string? deviceId,
        float volume,
        bool noiseSuppression,
        Func<AudioFrame, Task> dataCallback,
        CancellationToken cancellationToken);

    Task StopAsync();
}

/// <summary>One chunk of PCM audio samples.</summary>
public sealed class AudioFrame
{
    public required byte[] Data { get; init; }
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }
    public required int BitsPerSample { get; init; }
    public required long TimestampTicks { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Encoding
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Encodes raw frames + audio into a final output file via FFmpeg.
/// </summary>
public interface IVideoEncoderService : IDisposable
{
    Task InitializeAsync(EncoderConfig config);
    Task WriteVideoFrameAsync(CaptureFrame frame);
    Task WriteAudioFrameAsync(AudioFrame frame);
    Task FinalizeAsync();
}

/// <summary>Configuration passed to the encoder.</summary>
public sealed class EncoderConfig
{
    public required string OutputPath { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int FrameRate { get; init; }
    public required VideoCodec Codec { get; init; }
    public required ContainerFormat Container { get; init; }
    public required QualityPreset Quality { get; init; }
    public int VideoBitrateKbps { get; init; } = 8000;
    public required HardwareAccelerator Accelerator { get; init; }
    public bool HasAudio { get; init; } = true;
    public int AudioSampleRate { get; init; } = 48000;
    public int AudioChannels { get; init; } = 2;
    public int AudioBitrateKbps { get; init; } = 192;
}

// ─────────────────────────────────────────────────────────────
//  Hardware detection
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Detects available GPU hardware encoders (NVENC, AMF, QuickSync).
/// </summary>
public interface IHardwareDetectionService
{
    Task<IReadOnlyList<EncoderCapability>> DetectEncodersAsync();
    Task<HardwareAccelerator> GetBestAvailableAsync(VideoCodec codec);
    string GetGpuDescription();
}

// ─────────────────────────────────────────────────────────────
//  Device enumeration
// ─────────────────────────────────────────────────────────────

/// <summary>Enumerates monitors, windows, audio devices, and webcams.</summary>
public interface IDeviceEnumerationService
{
    IReadOnlyList<MonitorInfo> GetMonitors();
    IReadOnlyList<WindowInfo> GetCapturableWindows();
    IReadOnlyList<AudioDeviceInfo> GetAudioOutputDevices();
    IReadOnlyList<AudioDeviceInfo> GetAudioInputDevices();
    IReadOnlyList<WebcamInfo> GetWebcams();
    AudioDeviceInfo? GetDefaultOutputDevice();
    AudioDeviceInfo? GetDefaultInputDevice();
}

// ─────────────────────────────────────────────────────────────
//  File management
// ─────────────────────────────────────────────────────────────

public interface IFileManagementService
{
    string GenerateOutputPath(string directory, string format, ContainerFormat container);
    string GenerateScreenshotPath(string directory);
    Task<IReadOnlyList<RecordingHistoryEntry>> GetHistoryAsync(string directory);
    Task DeleteRecordingAsync(string filePath);
    Task PurgeOldRecordingsAsync(string directory, int olderThanDays);
    long GetAvailableDiskSpace(string path);
    Task OpenFolderAsync(string path);
    Task OpenFileAsync(string path);
}

// ─────────────────────────────────────────────────────────────
//  Settings
// ─────────────────────────────────────────────────────────────

public interface ISettingsService
{
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
    Task ResetToDefaultsAsync();
}

// ─────────────────────────────────────────────────────────────
//  Hotkeys
// ─────────────────────────────────────────────────────────────

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? StartHotkeyPressed;
    event EventHandler? PauseHotkeyPressed;
    event EventHandler? StopHotkeyPressed;
    event EventHandler? ScreenshotHotkeyPressed;

    void RegisterAll(AppSettings settings);
    void UnregisterAll();
    bool IsKeyAvailable(string key);
}

// ─────────────────────────────────────────────────────────────
//  Notifications
// ─────────────────────────────────────────────────────────────

public interface INotificationService
{
    void ShowRecordingStarted();
    void ShowRecordingPaused();
    void ShowRecordingResumed();
    void ShowRecordingSaved(string filePath, TimeSpan duration);
    void ShowRecordingFailed(string reason);
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
}

// ─────────────────────────────────────────────────────────────
//  Update checker
// ─────────────────────────────────────────────────────────────

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdateAsync();
}

public sealed class UpdateInfo
{
    public required string Version { get; init; }
    public required string DownloadUrl { get; init; }
    public required string ReleaseNotes { get; init; }
    public bool IsMandatory { get; init; }
}

// ─────────────────────────────────────────────────────────────
//  Webcam
// ─────────────────────────────────────────────────────────────

public interface IWebcamService : IDisposable
{
    bool IsRunning { get; }
    Task StartAsync(string monikerString, Func<System.Windows.Media.Imaging.BitmapSource, Task> frameCallback);
    Task StopAsync();
}
