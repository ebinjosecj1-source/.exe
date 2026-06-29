using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.ViewModels;

/// <summary>
/// Settings page ViewModel. All settings fields are bound directly
/// from a working copy of AppSettings, saved on explicit user action.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly IFileManagementService _fileManager;

    // ─── Video Settings ───────────────────────────────────────────────────────

    [ObservableProperty] private FrameRate _frameRate = FrameRate.Fps60;
    [ObservableProperty] private ResolutionPreset _resolution = ResolutionPreset.Original;
    [ObservableProperty] private int _customWidth = 1920;
    [ObservableProperty] private int _customHeight = 1080;
    [ObservableProperty] private VideoCodec _videoCodec = VideoCodec.H264;
    [ObservableProperty] private ContainerFormat _containerFormat = ContainerFormat.Mp4;
    [ObservableProperty] private QualityPreset _quality = QualityPreset.High;
    [ObservableProperty] private int _videoBitrateKbps = 8000;
    [ObservableProperty] private bool _autoDetectHardware = true;
    [ObservableProperty] private HardwareAccelerator _preferredAccelerator = HardwareAccelerator.None;

    // ─── Audio Settings ───────────────────────────────────────────────────────

    [ObservableProperty] private AudioCaptureMode _audioCaptureMode = AudioCaptureMode.Both;
    [ObservableProperty] private float _systemVolume = 1.0f;
    [ObservableProperty] private float _microphoneVolume = 1.0f;
    [ObservableProperty] private bool _noiseSuppressionEnabled;
    [ObservableProperty] private bool _pushToTalkEnabled;
    [ObservableProperty] private string _pushToTalkKey = "F12";

    // ─── Mouse Settings ───────────────────────────────────────────────────────

    [ObservableProperty] private bool _recordCursor = true;
    [ObservableProperty] private bool _highlightCursor;
    [ObservableProperty] private bool _showClickEffects;
    [ObservableProperty] private ClickEffect _leftClickEffect = ClickEffect.Circle;
    [ObservableProperty] private ClickEffect _rightClickEffect = ClickEffect.Circle;
    [ObservableProperty] private bool _enableCursorZoom;

    // ─── Output Settings ──────────────────────────────────────────────────────

    [ObservableProperty] private string _outputDirectory = string.Empty;
    [ObservableProperty] private string _filenameFormat = "yyyy-MM-dd_HH-mm-ss";

    // ─── Hotkeys ──────────────────────────────────────────────────────────────

    [ObservableProperty] private string _hotkeyStart = "F8";
    [ObservableProperty] private string _hotkeyPause = "F9";
    [ObservableProperty] private string _hotkeyStop = "F10";
    [ObservableProperty] private string _hotkeyScreenshot = "F11";

    // ─── Countdown / Auto-stop ────────────────────────────────────────────────

    [ObservableProperty] private int _countdownSeconds = 3;
    [ObservableProperty] private bool _autoStopEnabled;
    [ObservableProperty] private int _autoStopMinutes = 60;
    [ObservableProperty] private bool _autoDeleteOldRecordings;
    [ObservableProperty] private int _autoDeleteAfterDays = 30;

    // ─── Webcam ───────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _webcamEnabled;
    [ObservableProperty] private double _webcamWidth = 240;
    [ObservableProperty] private double _webcamHeight = 135;
    [ObservableProperty] private double _webcamOpacity = 1.0;
    [ObservableProperty] private WebcamBorderStyle _webcamBorder = WebcamBorderStyle.Round;

    // ─── UI ───────────────────────────────────────────────────────────────────

    [ObservableProperty] private AppTheme _theme = AppTheme.System;
    [ObservableProperty] private bool _minimizeToTrayOnRecord;
    [ObservableProperty] private bool _checkForUpdatesOnStartup = true;

    // ─── Computed Properties ──────────────────────────────────────────────────

    /// <summary>
    /// Controls visibility of the custom width/height fields in SettingsPanel.
    /// </summary>
    public bool IsCustomResolution => Resolution == ResolutionPreset.Custom;

    partial void OnResolutionChanged(ResolutionPreset value)
        => OnPropertyChanged(nameof(IsCustomResolution));

    // ─── Collections for ComboBoxes ───────────────────────────────────────────

    public IReadOnlyList<FrameRate> FrameRates { get; } =
        Enum.GetValues<FrameRate>();
    public IReadOnlyList<VideoCodec> VideoCodecs { get; } =
        Enum.GetValues<VideoCodec>();
    public IReadOnlyList<ContainerFormat> ContainerFormats { get; } =
        Enum.GetValues<ContainerFormat>();
    public IReadOnlyList<QualityPreset> QualityPresets { get; } =
        Enum.GetValues<QualityPreset>();
    public IReadOnlyList<ResolutionPreset> ResolutionPresets { get; } =
        Enum.GetValues<ResolutionPreset>();
    public IReadOnlyList<AudioCaptureMode> AudioCaptureModes { get; } =
        Enum.GetValues<AudioCaptureMode>();
    public IReadOnlyList<HardwareAccelerator> Accelerators { get; } =
        Enum.GetValues<HardwareAccelerator>();
    public IReadOnlyList<AppTheme> Themes { get; } =
        Enum.GetValues<AppTheme>();
    public IReadOnlyList<ClickEffect> ClickEffects { get; } =
        Enum.GetValues<ClickEffect>();
    public IReadOnlyList<WebcamBorderStyle> BorderStyles { get; } =
        Enum.GetValues<WebcamBorderStyle>();

    // ─── Constructor ──────────────────────────────────────────────────────────

    public SettingsViewModel(
        ISettingsService settings,
        IGlobalHotkeyService hotkeys,
        IFileManagementService fileManager)
    {
        _settings = settings;
        _hotkeys = hotkeys;
        _fileManager = fileManager;
    }

    public void LoadFromCurrent()
    {
        var s = _settings.Current;

        FrameRate = s.FrameRate;
        Resolution = s.Resolution;
        CustomWidth = s.CustomWidth;
        CustomHeight = s.CustomHeight;
        VideoCodec = s.Codec;
        ContainerFormat = s.Container;
        Quality = s.Quality;
        VideoBitrateKbps = s.CustomVideoBitrate;
        AutoDetectHardware = s.AutoDetectHardware;
        PreferredAccelerator = s.PreferredAccelerator;

        AudioCaptureMode = s.AudioMode;
        SystemVolume = s.SystemVolume;
        MicrophoneVolume = s.MicrophoneVolume;
        NoiseSuppressionEnabled = s.NoiseSuppressionEnabled;
        PushToTalkEnabled = s.PushToTalkEnabled;
        PushToTalkKey = s.PushToTalkKey;

        RecordCursor = s.RecordCursor;
        HighlightCursor = s.HighlightCursor;
        ShowClickEffects = s.ShowClickEffects;
        LeftClickEffect = s.LeftClickEffect;
        RightClickEffect = s.RightClickEffect;
        EnableCursorZoom = s.EnableCursorZoom;

        OutputDirectory = s.OutputDirectory;
        FilenameFormat = s.FilenameFormat;

        HotkeyStart = s.HotkeyStart;
        HotkeyPause = s.HotkeyPause;
        HotkeyStop = s.HotkeyStop;
        HotkeyScreenshot = s.HotkeyScreenshot;

        CountdownSeconds = s.CountdownSeconds;
        AutoStopEnabled = s.AutoStopEnabled;
        AutoStopMinutes = s.AutoStopMinutes;
        AutoDeleteOldRecordings = s.AutoDeleteOldRecordings;
        AutoDeleteAfterDays = s.AutoDeleteAfterDays;

        WebcamEnabled = s.WebcamEnabled;
        WebcamWidth = s.WebcamWidth;
        WebcamHeight = s.WebcamHeight;
        WebcamOpacity = s.WebcamOpacity;
        WebcamBorder = s.WebcamBorder;

        Theme = s.Theme;
        MinimizeToTrayOnRecord = s.MinimizeToTrayOnRecord;
        CheckForUpdatesOnStartup = s.CheckForUpdatesOnStartup;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _settings.Current;

        s.FrameRate = FrameRate;
        s.Resolution = Resolution;
        s.CustomWidth = CustomWidth;
        s.CustomHeight = CustomHeight;
        s.Codec = VideoCodec;
        s.Container = ContainerFormat;
        s.Quality = Quality;
        s.CustomVideoBitrate = VideoBitrateKbps;
        s.AutoDetectHardware = AutoDetectHardware;
        s.PreferredAccelerator = PreferredAccelerator;

        s.AudioMode = AudioCaptureMode;
        s.SystemVolume = SystemVolume;
        s.MicrophoneVolume = MicrophoneVolume;
        s.NoiseSuppressionEnabled = NoiseSuppressionEnabled;
        s.PushToTalkEnabled = PushToTalkEnabled;
        s.PushToTalkKey = PushToTalkKey;

        s.RecordCursor = RecordCursor;
        s.HighlightCursor = HighlightCursor;
        s.ShowClickEffects = ShowClickEffects;
        s.LeftClickEffect = LeftClickEffect;
        s.RightClickEffect = RightClickEffect;
        s.EnableCursorZoom = EnableCursorZoom;

        s.OutputDirectory = OutputDirectory;
        s.FilenameFormat = FilenameFormat;

        s.HotkeyStart = HotkeyStart;
        s.HotkeyPause = HotkeyPause;
        s.HotkeyStop = HotkeyStop;
        s.HotkeyScreenshot = HotkeyScreenshot;

        s.CountdownSeconds = CountdownSeconds;
        s.AutoStopEnabled = AutoStopEnabled;
        s.AutoStopMinutes = AutoStopMinutes;
        s.AutoDeleteOldRecordings = AutoDeleteOldRecordings;
        s.AutoDeleteAfterDays = AutoDeleteAfterDays;

        s.WebcamEnabled = WebcamEnabled;
        s.WebcamWidth = WebcamWidth;
        s.WebcamHeight = WebcamHeight;
        s.WebcamOpacity = WebcamOpacity;
        s.WebcamBorder = WebcamBorder;

        s.Theme = Theme;
        s.MinimizeToTrayOnRecord = MinimizeToTrayOnRecord;
        s.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;

        await _settings.SaveAsync(s);

        // Re-register hotkeys with updated key bindings
        _hotkeys.RegisterAll(s);
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        await _settings.ResetToDefaultsAsync();
        LoadFromCurrent();
    }

    [RelayCommand]
    private async Task BrowseOutputDirectoryAsync()
    {
        // WPF has no built-in folder dialog — use FolderBrowserDialog via interop
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Output Folder",
            UseDescriptionForTitle = true,
            SelectedPath = OutputDirectory
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            OutputDirectory = dialog.SelectedPath;
    }
}
