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
using WindowsScreenRecorder.Services.Video;
using WindowsScreenRecorder.ViewModels;
using WindowsScreenRecorder.Views;

namespace WindowsScreenRecorder;

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
                retainedFileCountLimit: 7)
#if DEBUG
            .WriteTo.Debug()
#endif
            .CreateLogger();
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(lb => { lb.ClearProviders(); lb.AddSerilog(dispose: true); });
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDeviceEnumerationService, DeviceEnumerationService>();
        services.AddSingleton<IFileManagementService, FileManagementService>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
        services.AddTransient<IScreenCaptureService, ScreenCaptureService>();
        services.AddTransient<ISystemAudioCaptureService, SystemAudioCaptureService>();
        services.AddTransient<IMicrophoneCaptureService, MicrophoneCaptureService>();
        services.AddSingleton<IHardwareDetectionService, HardwareDetectionService>();
        services.AddTransient<IVideoEncoderService, VideoEncoderService>();
        services.AddSingleton<IRecordingService, RecordingService>();
        services.AddTransient<IWebcamService, WebcamService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
