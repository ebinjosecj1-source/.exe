using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services;

/// <summary>
/// Persists application settings to %AppData%\WindowsScreenRecorder\settings.json.
/// Thread-safe via SemaphoreSlim.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly string SettingsDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsScreenRecorder");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
        DefaultValueHandling = DefaultValueHandling.Include
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AppSettings _current = new();

    public AppSettings Current => _current;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                _logger.LogInformation("No settings file found, using defaults");
                _current = new AppSettings();
                await SaveInternalAsync(_current).ConfigureAwait(false);
                return _current;
            }

            var json = await File.ReadAllTextAsync(SettingsPath).ConfigureAwait(false);
            var loaded = JsonConvert.DeserializeObject<AppSettings>(json, JsonSettings);

            if (loaded is null)
            {
                _logger.LogWarning("Settings file was corrupt, resetting to defaults");
                _current = new AppSettings();
            }
            else
            {
                _current = loaded;
                _logger.LogInformation("Settings loaded from {Path}", SettingsPath);
            }

            return _current;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings, using defaults");
            _current = new AppSettings();
            return _current;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _current = settings;
            await SaveInternalAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ResetToDefaultsAsync()
    {
        var defaults = new AppSettings();
        await SaveAsync(defaults).ConfigureAwait(false);
        _logger.LogInformation("Settings reset to defaults");
    }

    private static async Task SaveInternalAsync(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonConvert.SerializeObject(settings, JsonSettings);
        // Write to a temp file then rename for atomic replacement
        var temp = SettingsPath + ".tmp";
        await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
        File.Move(temp, SettingsPath, overwrite: true);
    }
}
