using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using System.Windows;
using Application = System.Windows.Application;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Services;
using WindowsScreenRecorder.Services.Audio;
using WindowsScreenRecorder.Services.Capture;
using WindowsScreenRecorder.Services.Encoding;
using WindowsScreenRecorder.Services.FileManagement;
using WindowsScreenRecorder.Services.Hotkeys;
using WindowsScreenRecorder.ViewModels;
using WindowsScreenRecorder.Views;

namespace WindowsScreenRecorder;

/// <summary>
/// Application entry point. Configures the DI container (Microsoft.Extensions.DI),
/// Serilog structured logging, and launches the main window.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigureLogging();
        _serviceProvider = BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Async initialization (device scan, settings load, hardware detect)
        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
        await mainVm.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsScreenRecorder", "Logs");

        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "wsr-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
#if DEBUG
            .WriteTo.Debug()
#endif
            .CreateLogger();
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddSerilog(dispose: true);
        });

        // Core services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDeviceEnumerationService, DeviceEnumerationService>();
        services.AddSingleton<IFileManagementService, FileManagementService>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp =>
            sp.GetRequiredService<NotificationService>());

        // Capture services
        services.AddTransient<IScreenCaptureService, ScreenCaptureService>();
        services.AddTransient<ISystemAudioCaptureService, SystemAudioCaptureService>();
        services.AddTransient<IMicrophoneCaptureService, MicrophoneCaptureService>();

        // Encoding services
        services.AddSingleton<IHardwareDetectionService, HardwareDetectionService>();
        services.AddTransient<IVideoEncoderService, VideoEncoderService>();

        // Recording orchestration
        services.AddSingleton<IRecordingService, RecordingService>();

        // Update service
        // Webcam overlay
        services.AddTransient<IWebcamService, WebcamService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Views
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
