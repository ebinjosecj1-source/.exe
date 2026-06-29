namespace WindowsScreenRecorder.Core.Enums;

/// <summary>Recording state machine states.</summary>
public enum RecordingState
{
    Idle,
    Countdown,
    Recording,
    Paused,
    Stopping,
    Saving
}

/// <summary>Which screen region to capture.</summary>
public enum CaptureMode
{
    FullDesktop,
    SingleMonitor,
    ApplicationWindow,
    CustomRegion
}

/// <summary>Frames per second options.</summary>
public enum FrameRate
{
    Fps30 = 30,
    Fps60 = 60,
    Fps120 = 120
}

/// <summary>Output resolution presets.</summary>
public enum ResolutionPreset
{
    Original,
    Res1080p,
    Res720p,
    Custom
}

/// <summary>Video codec selection.</summary>
public enum VideoCodec
{
    H264,
    H265,
    AV1
}

/// <summary>Output container format.</summary>
public enum ContainerFormat
{
    Mp4,
    Mkv,
    Avi
}

/// <summary>Encoding quality preset.</summary>
public enum QualityPreset
{
    Low,
    Medium,
    High,
    Lossless
}

/// <summary>Hardware acceleration provider.</summary>
public enum HardwareAccelerator
{
    None,       // Software (libx264/libx265)
    NVENC,      // NVIDIA
    AMF,        // AMD
    QuickSync   // Intel
}

/// <summary>Which audio streams to record.</summary>
public enum AudioCaptureMode
{
    None,
    SystemOnly,
    MicrophoneOnly,
    Both
}

/// <summary>Application theme.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>Webcam border style.</summary>
public enum WebcamBorderStyle
{
    None,
    Round,
    Square
}

/// <summary>Mouse click effect style.</summary>
public enum ClickEffect
{
    None,
    Circle,
    Ripple,
    Highlight
}
