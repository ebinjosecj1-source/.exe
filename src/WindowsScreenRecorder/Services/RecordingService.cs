using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services;

/// <summary>
/// Orchestrates the full recording pipeline:
///   1. Resolves capture targets (monitor, window)
///   2. Determines hardware encoder
///   3. Starts screen capture, system audio, and microphone in parallel
///   4. Feeds frames into the video encoder
///   5. Finalizes and reports stats
/// 
/// All inter-service communication uses async channels to prevent
/// producer/consumer blocking on the UI thread.
/// </summary>
public sealed class RecordingService : IRecordingService
{
    private readonly IScreenCaptureService _screenCapture;
    private readonly ISystemAudioCaptureService _systemAudio;
    private readonly IMicrophoneCaptureService _microphone;
    private readonly IVideoEncoderService _encoder;
    private readonly IHardwareDetectionService _hwDetection;
    private readonly IDeviceEnumerationService _deviceEnum;
    private readonly IFileManagementService _fileManager;
    private readonly ILogger<RecordingService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _pipelineTask;
    private RecordingState _state = RecordingState.Idle;
    private RecordingStats _stats = new();
    private string? _outputPath;

    public RecordingState State => _state;
    public RecordingStats CurrentStats => _stats;

    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
    public event EventHandler<RecordingStats>? StatsUpdated;

    public RecordingService(
        IScreenCaptureService screenCapture,
        ISystemAudioCaptureService systemAudio,
        IMicrophoneCaptureService microphone,
        IVideoEncoderService encoder,
        IHardwareDetectionService hwDetection,
        IDeviceEnumerationService deviceEnum,
        IFileManagementService fileManager,
        ILogger<RecordingService> logger)
    {
        _screenCapture = screenCapture;
        _systemAudio = systemAudio;
        _microphone = microphone;
        _encoder = encoder;
        _hwDetection = hwDetection;
        _deviceEnum = deviceEnum;
        _fileManager = fileManager;
        _logger = logger;
    }

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (_state != RecordingState.Idle)
            throw new InvalidOperationException($"Cannot start recording while in state {_state}.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            TransitionState(RecordingState.Countdown);

            // Wait for countdown
            if (settings.CountdownSeconds > 0)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(settings.CountdownSeconds),
                    _cts.Token).ConfigureAwait(false);
            }

            // Resolve capture target
            MonitorInfo? monitor = null;
            WindowInfo? window = null;

            if (settings.CaptureMode == CaptureMode.SingleMonitor
                && settings.SelectedMonitorName is not null)
            {
                monitor = _deviceEnum.GetMonitors()
                    .FirstOrDefault(m => m.DeviceName == settings.SelectedMonitorName);
            }
            else if (settings.CaptureMode == CaptureMode.ApplicationWindow
                     && settings.SelectedWindowHandle is int hwnd)
            {
                window = _deviceEnum.GetCapturableWindows()
                    .FirstOrDefault(w => w.Handle == new IntPtr(hwnd));
            }

            // Compute output dimensions
            (int width, int height) = ComputeOutputDimensions(settings, monitor, window);

            // Detect hardware encoder if auto
            HardwareAccelerator accelerator = settings.PreferredAccelerator;
            if (settings.AutoDetectHardware)
                accelerator = await _hwDetection.GetBestAvailableAsync(settings.Codec)
                    .ConfigureAwait(false);

            // Generate output file path
            _outputPath = _fileManager.GenerateOutputPath(
                settings.OutputDirectory,
                settings.FilenameFormat,
                settings.Container);

            Directory.CreateDirectory(settings.OutputDirectory);

            bool hasAudio = settings.AudioMode != AudioCaptureMode.None;

            // Initialize encoder
            var encoderConfig = new EncoderConfig
            {
                OutputPath = _outputPath,
                Width = width,
                Height = height,
                FrameRate = (int)settings.FrameRate,
                Codec = settings.Codec,
                Container = settings.Container,
                Quality = settings.Quality,
                VideoBitrateKbps = settings.CustomVideoBitrate,
                Accelerator = accelerator,
                HasAudio = hasAudio,
                AudioSampleRate = 48000,
                AudioChannels = 2,
                AudioBitrateKbps = 192
            };

            await _encoder.InitializeAsync(encoderConfig).ConfigureAwait(false);

            TransitionState(RecordingState.Recording);
            _logger.LogInformation("Recording started: {Path}", _outputPath);

            // Launch the parallel capture pipeline
            _pipelineTask = RunCaptureAsync(settings, monitor, window, _cts.Token);

            // Start stats update loop
            _ = UpdateStatsLoopAsync(_cts.Token);

            // Auto-stop timer
            if (settings.AutoStopEnabled)
            {
                _ = Task.Delay(
                    TimeSpan.FromMinutes(settings.AutoStopMinutes),
                    _cts.Token)
                    .ContinueWith(_ => StopAsync(),
                        TaskContinuationOptions.NotOnCanceled);
            }
        }
        catch (OperationCanceledException)
        {
            TransitionState(RecordingState.Idle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording");
            TransitionState(RecordingState.Idle, ex.Message);
            throw;
        }
    }

    public Task PauseAsync()
    {
        if (_state != RecordingState.Recording) return Task.CompletedTask;
        // Pause is signalled by setting state; capture loops check this
        TransitionState(RecordingState.Paused);
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_state != RecordingState.Paused) return Task.CompletedTask;
        TransitionState(RecordingState.Recording);
        return Task.CompletedTask;
    }

    public async Task<string> StopAsync()
    {
        if (_state is RecordingState.Idle or RecordingState.Stopping)
            return _outputPath ?? string.Empty;

        TransitionState(RecordingState.Stopping);

        try
        {
            _cts?.Cancel();

            if (_pipelineTask is not null)
            {
                try { await _pipelineTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            TransitionState(RecordingState.Saving);
            await _encoder.FinalizeAsync().ConfigureAwait(false);

            _logger.LogInformation("Recording saved: {Path}", _outputPath);
            TransitionState(RecordingState.Idle, outputFilePath: _outputPath);
            return _outputPath ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finalizing recording");
            TransitionState(RecordingState.Idle, ex.Message);
            return string.Empty;
        }
    }

    public async Task TakeScreenshotAsync(AppSettings settings)
    {
        string path = _fileManager.GenerateScreenshotPath(settings.OutputDirectory);
        Directory.CreateDirectory(settings.OutputDirectory);

        // Capture a single GDI frame for the screenshot
        // Uses the existing screen bounds or monitor bounds
        MonitorInfo? monitor = null;
        if (settings.CaptureMode == CaptureMode.SingleMonitor
            && settings.SelectedMonitorName is not null)
        {
            monitor = _deviceEnum.GetMonitors()
                .FirstOrDefault(m => m.DeviceName == settings.SelectedMonitorName);
        }

        var bounds = monitor is not null
            ? monitor.Bounds
            : new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

        await Task.Run(() =>
        {
            using var bmp = new System.Drawing.Bitmap(
                (int)bounds.Width, (int)bounds.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppRgb);

            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.CopyFromScreen(
                (int)bounds.X, (int)bounds.Y, 0, 0,
                new System.Drawing.Size((int)bounds.Width, (int)bounds.Height),
                System.Drawing.CopyPixelOperation.SourceCopy);

            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }).ConfigureAwait(false);

        _logger.LogInformation("Screenshot saved: {Path}", path);
    }

    // ─── Private Pipeline ─────────────────────────────────────────────────────

    private async Task RunCaptureAsync(
        AppSettings settings,
        MonitorInfo? monitor,
        WindowInfo? window,
        CancellationToken ct)
    {
        var tasks = new List<Task>();

        // Screen capture task
        tasks.Add(_screenCapture.StartAsync(
            settings.CaptureMode,
            monitor,
            window,
            settings.CustomRegion,
            (int)settings.FrameRate,
            async frame =>
            {
                // Skip frames while paused
                while (_state == RecordingState.Paused)
                    await Task.Delay(50, ct).ConfigureAwait(false);

                await _encoder.WriteVideoFrameAsync(frame).ConfigureAwait(false);
            },
            ct));

        // System audio task
        if (settings.AudioMode is AudioCaptureMode.SystemOnly or AudioCaptureMode.Both)
        {
            tasks.Add(_systemAudio.StartAsync(
                settings.SystemAudioDeviceId,
                settings.SystemVolume,
                async frame =>
                {
                    if (_state == RecordingState.Paused) return;
                    await _encoder.WriteAudioFrameAsync(frame).ConfigureAwait(false);
                },
                ct));
        }

        // Microphone task
        if (settings.AudioMode is AudioCaptureMode.MicrophoneOnly or AudioCaptureMode.Both)
        {
            tasks.Add(_microphone.StartAsync(
                settings.MicrophoneDeviceId,
                settings.MicrophoneVolume,
                settings.NoiseSuppressionEnabled,
                async frame =>
                {
                    if (_state == RecordingState.Paused) return;
                    if (settings.PushToTalkEnabled) return; // Handled by hotkey service
                    await _encoder.WriteAudioFrameAsync(frame).ConfigureAwait(false);
                },
                ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task UpdateStatsLoopAsync(CancellationToken ct)
    {
        var process = Process.GetCurrentProcess();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!ct.IsCancellationRequested && _state != RecordingState.Idle)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);

                process.Refresh();
                _stats = new RecordingStats
                {
                    Duration = stopwatch.Elapsed,
                    CpuUsagePercent = GetCpuUsage(process),
                    MemoryUsageMB = process.WorkingSet64 / (1024.0 * 1024),
                    FileSizeBytes = _outputPath is not null && File.Exists(_outputPath)
                        ? new FileInfo(_outputPath).Length : 0,
                    AvailableDiskBytes = _outputPath is not null
                        ? _fileManager.GetAvailableDiskSpace(_outputPath) : 0
                };

                StatsUpdated?.Invoke(this, _stats);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static (int Width, int Height) ComputeOutputDimensions(
        AppSettings s, MonitorInfo? monitor, WindowInfo? window)
    {
        int srcW, srcH;

        if (s.CaptureMode == CaptureMode.SingleMonitor && monitor is not null)
        {
            srcW = (int)monitor.Bounds.Width;
            srcH = (int)monitor.Bounds.Height;
        }
        else if (s.CaptureMode == CaptureMode.ApplicationWindow && window is not null)
        {
            srcW = (int)window.Bounds.Width;
            srcH = (int)window.Bounds.Height;
        }
        else if (s.CaptureMode == CaptureMode.CustomRegion && s.CustomRegion != Rect.Empty)
        {
            srcW = (int)s.CustomRegion.Width;
            srcH = (int)s.CustomRegion.Height;
        }
        else
        {
            srcW = (int)SystemParameters.VirtualScreenWidth;
            srcH = (int)SystemParameters.VirtualScreenHeight;
        }

        return s.Resolution switch
        {
            ResolutionPreset.Res1080p => (1920, 1080),
            ResolutionPreset.Res720p => (1280, 720),
            ResolutionPreset.Custom => (s.CustomWidth & ~1, s.CustomHeight & ~1),
            _ => (srcW & ~1, srcH & ~1)
        };
    }

    private static double _lastCpuTime;
    private static DateTime _lastCpuCheck = DateTime.MinValue;

    private static double GetCpuUsage(Process process)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (_lastCpuCheck == DateTime.MinValue)
            {
                _lastCpuTime = process.TotalProcessorTime.TotalMilliseconds;
                _lastCpuCheck = now;
                return 0;
            }

            double elapsed = (now - _lastCpuCheck).TotalMilliseconds;
            double cpuDelta = process.TotalProcessorTime.TotalMilliseconds - _lastCpuTime;
            _lastCpuTime = process.TotalProcessorTime.TotalMilliseconds;
            _lastCpuCheck = now;

            int cores = Environment.ProcessorCount;
            return Math.Min(100.0, cpuDelta / (elapsed * cores) * 100.0);
        }
        catch
        {
            return 0;
        }
    }

    private void TransitionState(
        RecordingState newState,
        string? error = null,
        string? outputFilePath = null)
    {
        var previous = _state;
        _state = newState;

        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs
        {
            NewState = newState,
            PreviousState = previous,
            OutputFilePath = outputFilePath,
            ErrorMessage = error
        });
    }
}
