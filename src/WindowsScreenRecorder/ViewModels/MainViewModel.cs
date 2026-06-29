using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;
using WindowsScreenRecorder.Services.Notifications;

namespace WindowsScreenRecorder.ViewModels;

/// <summary>
/// Main window ViewModel. Binds recording controls, live stats,
/// device selections, and navigation to the Settings page.
/// Implements MVVM via CommunityToolkit.Mvvm source generators.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IRecordingService _recording;
    private readonly IDeviceEnumerationService _devices;
    private readonly ISettingsService _settings;
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly IHardwareDetectionService _hwDetection;
    private readonly IFileManagementService _fileManager;
    private readonly NotificationService _notifications;
    private readonly ILogger<MainViewModel> _logger;

    // ─── Observable Properties ────────────────────────────────────────────────

    [ObservableProperty]
    private RecordingState _recordingState = RecordingState.Idle;

    [ObservableProperty]
    private string _recordingTimer = "00:00:00";

    [ObservableProperty]
    private ObservableCollection<MonitorInfo> _monitors = new();

    [ObservableProperty]
    private MonitorInfo? _selectedMonitor;

    [ObservableProperty]
    private ObservableCollection<WindowInfo> _capturableWindows = new();

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _audioOutputDevices = new();

    [ObservableProperty]
    private AudioDeviceInfo? _selectedAudioOutput;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _microphoneDevices = new();

    [ObservableProperty]
    private AudioDeviceInfo? _selectedMicrophone;

    [ObservableProperty]
    private ObservableCollection<WebcamInfo> _webcams = new();

    [ObservableProperty]
    private WebcamInfo? _selectedWebcam;

    [ObservableProperty]
    private RecordingStats _stats = new();

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private string _gpuInfo = "Detecting...";

    [ObservableProperty]
    private float _systemAudioLevel;

    [ObservableProperty]
    private float _microphoneLevel;

    [ObservableProperty]
    private string _notificationMessage = string.Empty;

    [ObservableProperty]
    private bool _notificationIsError;

    [ObservableProperty]
    private bool _notificationVisible;

    [ObservableProperty]
    private CaptureMode _captureMode = CaptureMode.FullDesktop;

    [ObservableProperty]
    private bool _isSettingsOpen;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public MainViewModel(
        IRecordingService recording,
        IDeviceEnumerationService devices,
        ISettingsService settings,
        IGlobalHotkeyService hotkeys,
        IHardwareDetectionService hwDetection,
        IFileManagementService fileManager,
        NotificationService notifications,
        ILogger<MainViewModel> logger)
    {
        _recording = recording;
        _devices = devices;
        _settings = settings;
        _hotkeys = hotkeys;
        _hwDetection = hwDetection;
        _fileManager = fileManager;
        _notifications = notifications;
        _logger = logger;

        SubscribeToEvents();
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecordingAsync()
    {
        try
        {
            var s = _settings.Current;
            ApplyDeviceSelections(s);

            await _recording.StartAsync(s);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording");
            ShowNotification("Recording Failed", ex.Message, isError: true);
        }
    }
    private bool CanStartRecording() =>
        RecordingState is RecordingState.Idle;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseRecordingAsync()
    {
        if (RecordingState == RecordingState.Recording)
            await _recording.PauseAsync();
        else
            await _recording.ResumeAsync();
    }
    private bool CanPause() =>
        RecordingState is RecordingState.Recording or RecordingState.Paused;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopRecordingAsync()
    {
        string path = await _recording.StopAsync();
        if (!string.IsNullOrEmpty(path))
            _notifications.ShowRecordingSaved(path, Stats.Duration);
    }
    private bool CanStop() =>
        RecordingState is RecordingState.Recording or RecordingState.Paused;

    [RelayCommand]
    private async Task TakeScreenshotAsync()
    {
        try
        {
            await _recording.TakeScreenshotAsync(_settings.Current);
            ShowNotification("Screenshot Saved",
                $"Saved to {_settings.Current.OutputDirectory}", isError: false);
        }
        catch (Exception ex)
        {
            ShowNotification("Screenshot Failed", ex.Message, isError: true);
        }
    }

    [RelayCommand]
    private async Task OpenOutputFolderAsync()
    {
        await _fileManager.OpenFolderAsync(_settings.Current.OutputDirectory);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        LoadDevices();
    }

    // ─── Initialization ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var s = await _settings.LoadAsync();
        OutputFolder = s.OutputDirectory;
        CaptureMode = s.CaptureMode;

        LoadDevices();

        // Register global hotkeys
        _hotkeys.RegisterAll(s);

        // Detect hardware in background
        _ = Task.Run(async () =>
        {
            var caps = await _hwDetection.DetectEncodersAsync();
            GpuInfo = _hwDetection.GetGpuDescription();
        });

        // Auto-delete old recordings if configured
        if (s.AutoDeleteOldRecordings)
            await _fileManager.PurgeOldRecordingsAsync(
                s.OutputDirectory, s.AutoDeleteAfterDays);

        _logger.LogInformation("MainViewModel initialized");
    }

    private void LoadDevices()
    {
        Monitors.Clear();
        foreach (var m in _devices.GetMonitors()) Monitors.Add(m);

        CapturableWindows.Clear();
        foreach (var w in _devices.GetCapturableWindows()) CapturableWindows.Add(w);

        AudioOutputDevices.Clear();
        foreach (var d in _devices.GetAudioOutputDevices()) AudioOutputDevices.Add(d);

        MicrophoneDevices.Clear();
        foreach (var d in _devices.GetAudioInputDevices()) MicrophoneDevices.Add(d);

        Webcams.Clear();
        foreach (var c in _devices.GetWebcams()) Webcams.Add(c);

        // Select defaults
        var s = _settings.Current;
        SelectedMonitor = Monitors.FirstOrDefault(m => m.DeviceName == s.SelectedMonitorName)
                       ?? Monitors.FirstOrDefault(m => m.IsPrimary)
                       ?? Monitors.FirstOrDefault();

        SelectedAudioOutput = AudioOutputDevices.FirstOrDefault(d => d.Id == s.SystemAudioDeviceId)
                           ?? AudioOutputDevices.FirstOrDefault(d => d.IsDefault)
                           ?? AudioOutputDevices.FirstOrDefault();

        SelectedMicrophone = MicrophoneDevices.FirstOrDefault(d => d.Id == s.MicrophoneDeviceId)
                          ?? MicrophoneDevices.FirstOrDefault(d => d.IsDefault)
                          ?? MicrophoneDevices.FirstOrDefault();

        SelectedWebcam = Webcams.FirstOrDefault(w => w.MonikerString == s.WebcamMonikerString)
                      ?? Webcams.FirstOrDefault();
    }

    private void ApplyDeviceSelections(AppSettings s)
    {
        if (SelectedMonitor is not null)
            s.SelectedMonitorName = SelectedMonitor.DeviceName;

        if (SelectedWindow is not null)
            s.SelectedWindowHandle = SelectedWindow.Handle.ToInt32();

        if (SelectedAudioOutput is not null)
            s.SystemAudioDeviceId = SelectedAudioOutput.Id;

        if (SelectedMicrophone is not null)
            s.MicrophoneDeviceId = SelectedMicrophone.Id;

        if (SelectedWebcam is not null)
            s.WebcamMonikerString = SelectedWebcam.MonikerString;

        s.CaptureMode = CaptureMode;
    }

    // ─── Event Subscriptions ──────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        _recording.StateChanged += OnRecordingStateChanged;
        _recording.StatsUpdated += OnStatsUpdated;

        _hotkeys.StartHotkeyPressed += (_, _) => StartRecordingCommand.Execute(null);
        _hotkeys.PauseHotkeyPressed += (_, _) => PauseRecordingCommand.Execute(null);
        _hotkeys.StopHotkeyPressed += (_, _) => StopRecordingCommand.Execute(null);
        _hotkeys.ScreenshotHotkeyPressed += (_, _) => TakeScreenshotCommand.Execute(null);

        _notifications.NotificationPosted += (_, n) =>
            ShowNotification(n.Title, n.Message, n.IsError);
    }

    private void OnRecordingStateChanged(object? sender, RecordingStateChangedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            RecordingState = e.NewState;

            StatusMessage = e.NewState switch
            {
                RecordingState.Countdown => "Starting...",
                RecordingState.Recording => "Recording",
                RecordingState.Paused => "Paused",
                RecordingState.Stopping => "Stopping...",
                RecordingState.Saving => "Saving...",
                _ => "Ready"
            };

            if (e.NewState == RecordingState.Idle && e.ErrorMessage is not null)
                ShowNotification("Error", e.ErrorMessage, isError: true);

            // Re-evaluate command availability
            StartRecordingCommand.NotifyCanExecuteChanged();
            PauseRecordingCommand.NotifyCanExecuteChanged();
            StopRecordingCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnStatsUpdated(object? sender, RecordingStats stats)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Stats = stats;
            RecordingTimer = stats.Duration.ToString(@"hh\:mm\:ss");
        });
    }

    private void ShowNotification(string title, string message, bool isError)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            NotificationMessage = $"{title}: {message}";
            NotificationIsError = isError;
            NotificationVisible = true;

            // Auto-dismiss after 4 seconds
            _ = Task.Delay(4000).ContinueWith(_ =>
            {
                App.Current.Dispatcher.Invoke(() => NotificationVisible = false);
            });
        });
    }

    /// <summary>
    /// Called by MainWindow code-behind when the window is closing.
    /// Stops any active recording and flushes the encoder pipeline
    /// so the output file is finalized before the process exits.
    /// </summary>
    public void OnWindowClosing()
    {
        if (RecordingState is RecordingState.Recording or RecordingState.Paused)
        {
            _logger.LogInformation("Window closing while recording active — stopping recording");

            // Block briefly to allow the encoder to finalize the file
            _recording.StopAsync().GetAwaiter().GetResult();
        }

        _hotkeys.Dispose();
        _logger.LogInformation("Application shutting down");
    }
}
