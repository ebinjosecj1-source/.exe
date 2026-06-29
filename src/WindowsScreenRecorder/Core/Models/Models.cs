using System.IO;
using System.Windows;
using WindowsScreenRecorder.Core.Enums;

namespace WindowsScreenRecorder.Core.Models;

/// <summary>Represents a physical display monitor.</summary>
public sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required string FriendlyName { get; init; }
    public required Rect Bounds { get; init; }
    public required Rect WorkArea { get; init; }
    public bool IsPrimary { get; init; }
    public int Index { get; init; }
    public double ScaleFactor { get; init; } = 1.0;

    public override string ToString() =>
        IsPrimary
            ? $"{FriendlyName} (Primary) â {(int)Bounds.Width}Ã{(int)Bounds.Height}"
            : $"{FriendlyName} â {(int)Bounds.Width}Ã{(int)Bounds.Height}";
}

/// <summary>Represents a capturable application window.</summary>
public sealed class WindowInfo
{
    public required IntPtr Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }
    public required int ProcessId { get; init; }
    public Rect Bounds { get; init; }
    public bool IsMinimized { get; init; }

    public override string ToString() => $"{Title} ({ProcessName})";
}

/// <summary>Represents a WASAPI audio device.</summary>
public sealed class AudioDeviceInfo
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public bool IsDefault { get; init; }
    public bool IsCapture { get; init; }  // true = microphone, false = render/loopback

    public override string ToString() => FriendlyName;
}

/// <summary>Represents a connected webcam.</summary>
public sealed class WebcamInfo
{
    public required string MonikerString { get; init; }
    public required string FriendlyName { get; init; }
    public int Index { get; init; }

    public override string ToString() => FriendlyName;
}

/// <summary>Detected hardware encoder capability.</summary>
public sealed class EncoderCapability
{
    public required HardwareAccelerator Accelerator { get; init; }
    public required string DeviceName { get; init; }
    public bool SupportsH264 { get; init; }
    public bool SupportsH265 { get; init; }
    public bool SupportsAV1 { get; init; }
    public bool IsAvailable { get; init; }
}

/// <summary>Full application settings â serialized to JSON.</summary>
public sealed class AppSettings
{
    // Capture
    public CaptureMode CaptureMode { get; set; } = CaptureMode.FullDesktop;
    public string? SelectedMonitorName { get; set; }
    public int? SelectedWindowHandle { get; set; }
    public Rect CustomRegion { get; set; }

    // Video
    public FrameRate FrameRate { get; set; } = FrameRate.Fps60;
    public ResolutionPreset Resolution { get; set; } = ResolutionPreset.Original;
    public int CustomWidth { get; set; } = 1920;
    public int CustomHeight { get; set; } = 1080;
    public VideoCodec Codec { get; set; } = VideoCodec.H264;
    public ContainerFormat Container { get; set; } = ContainerFormat.Mp4;
    public QualityPreset Quality { get; set; } = QualityPreset.High;
    public int CustomVideoBitrate { get; set; } = 8000; // kbps
    public HardwareAccelerator PreferredAccelerator { get; set; } = HardwareAccelerator.None;
    public bool AutoDetectHardware { get; set; } = true;

    // Audio
    public AudioCaptureMode AudioMode { get; set; } = AudioCaptureMode.Both;
    public string? SystemAudioDeviceId { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public float SystemVolume { get; set; } = 1.0f;
    public float MicrophoneVolume { get; set; } = 1.0f;
    public bool NoiseSuppressionEnabled { get; set; } = false;
    public bool PushToTalkEnabled { get; set; } = false;
    public string PushToTalkKey { get; set; } = "F12";

    // Mouse
    public bool RecordCursor { get; set; } = true;
    public bool HighlightCursor { get; set; } = false;
    public bool ShowClickEffects { get; set; } = false;
    public ClickEffect LeftClickEffect { get; set; } = ClickEffect.Circle;
    public ClickEffect RightClickEffect { get; set; } = ClickEffect.Circle;
    public bool EnableCursorZoom { get; set; } = false;

    // Webcam
    public bool WebcamEnabled { get; set; } = false;
    public string? WebcamMonikerString { get; set; }
    public double WebcamX { get; set; } = 20;
    public double WebcamY { get; set; } = 20;
    public double WebcamWidth { get; set; } = 240;
    public double WebcamHeight { get; set; } = 135;
    public double WebcamOpacity { get; set; } = 1.0;
    public WebcamBorderStyle WebcamBorder { get; set; } = WebcamBorderStyle.Round;

    // Output
    public string OutputDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Screen Recordings");
    public string FilenameFormat { get; set; } = "yyyy-MM-dd_HH-mm-ss";

    // Hotkeys
    public string HotkeyStart { get; set; } = "F8";
    public string HotkeyPause { get; set; } = "F9";
    public string HotkeyStop { get; set; } = "F10";
    public string HotkeyScreenshot { get; set; } = "F11";

    // Countdown / auto-stop
    public int CountdownSeconds { get; set; } = 3;
    public bool AutoStopEnabled { get; set; } = false;
    public int AutoStopMinutes { get; set; } = 60;
    public bool AutoDeleteOldRecordings { get; set; } = false;
    public int AutoDeleteAfterDays { get; set; } = 30;

    // UI
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool ShowPreviewWindow { get; set; } = true;
    public bool MinimizeToTrayOnRecord { get; set; } = false;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
}

/// <summary>Live telemetry snapshot during recording.</summary>
public sealed class RecordingStats
{
    public TimeSpan Duration { get; set; }
    public long FileSizeBytes { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsageMB { get; set; }
    public double EncoderFps { get; set; }
    public double DroppedFrames { get; set; }
    public long AvailableDiskBytes { get; set; }

    public string FileSizeFormatted => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{FileSizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{FileSizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string DiskSpaceFormatted => AvailableDiskBytes switch
    {
        < 1024 * 1024 * 1024 => $"{AvailableDiskBytes / (1024.0 * 1024):F0} MB free",
        _ => $"{AvailableDiskBytes / (1024.0 * 1024 * 1024):F1} GB free"
    };
}

/// <summary>An entry in the recording history list.</summary>
public sealed class RecordingHistoryEntry
{
    public required string FilePath { get; init; }
    public required DateTime CreatedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public long FileSizeBytes { get; init; }
    public required string ThumbnailPath { get; init; }

    public string FileName => Path.GetFileName(FilePath);
    public string FileSizeFormatted => FileSizeBytes switch
    {
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{FileSizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{FileSizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

/// <summary>Arguments for recording-state-change events.</summary>
public sealed class RecordingStateChangedEventArgs : EventArgs
{
    public required RecordingState NewState { get; init; }
    public required RecordingState PreviousState { get; init; }
    public string? OutputFilePath { get; init; }
    public string? ErrorMessage { get; init; }
}
