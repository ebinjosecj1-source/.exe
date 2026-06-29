using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services.FileManagement;

/// <summary>
/// Handles output path generation, recording history scanning, disk space queries,
/// old recording cleanup, and launching files/folders in Explorer.
/// </summary>
public sealed class FileManagementService : IFileManagementService
{
    private readonly ILogger<FileManagementService> _logger;

    public FileManagementService(ILogger<FileManagementService> logger)
    {
        _logger = logger;
    }

    public string GenerateOutputPath(string directory, string format, ContainerFormat container)
    {
        string ext = container switch
        {
            ContainerFormat.Mkv => "mkv",
            ContainerFormat.Avi => "avi",
            _ => "mp4"
        };

        string filename = DateTime.Now.ToString(format) + "." + ext;
        // Sanitize any characters that are invalid in filenames
        foreach (char c in Path.GetInvalidFileNameChars())
            filename = filename.Replace(c, '-');

        return Path.Combine(directory, filename);
    }

    public string GenerateScreenshotPath(string directory)
    {
        string filename = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
        return Path.Combine(directory, filename);
    }

    public async Task<IReadOnlyList<RecordingHistoryEntry>> GetHistoryAsync(string directory)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<RecordingHistoryEntry>();

        var entries = new List<RecordingHistoryEntry>();

        var files = Directory.EnumerateFiles(directory, "*.*")
            .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetCreationTime)
            .Take(100); // Cap history at 100 entries

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                string thumbPath = Path.ChangeExtension(file, ".thumb.jpg");

                entries.Add(new RecordingHistoryEntry
                {
                    FilePath = file,
                    CreatedAt = info.CreationTime,
                    FileSizeBytes = info.Length,
                    Duration = await GetVideoDurationAsync(file).ConfigureAwait(false),
                    ThumbnailPath = File.Exists(thumbPath) ? thumbPath : string.Empty
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read recording info: {File}", file);
            }
        }

        return entries;
    }

    public Task DeleteRecordingAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            // Delete associated thumbnail
            string thumb = Path.ChangeExtension(filePath, ".thumb.jpg");
            if (File.Exists(thumb))
                File.Delete(thumb);

            _logger.LogInformation("Deleted recording: {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete recording: {Path}", filePath);
        }
        return Task.CompletedTask;
    }

    public Task PurgeOldRecordingsAsync(string directory, int olderThanDays)
    {
        if (!Directory.Exists(directory)) return Task.CompletedTask;

        var cutoff = DateTime.Now.AddDays(-olderThanDays);
        var old = Directory.EnumerateFiles(directory, "*.*")
            .Where(f => (f.EndsWith(".mp4") || f.EndsWith(".mkv") || f.EndsWith(".avi"))
                     && File.GetCreationTime(f) < cutoff);

        int count = 0;
        foreach (var file in old)
        {
            try
            {
                File.Delete(file);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete old recording: {File}", file);
            }
        }

        if (count > 0)
            _logger.LogInformation(
                "Auto-deleted {Count} recordings older than {Days} days", count, olderThanDays);

        return Task.CompletedTask;
    }

    public long GetAvailableDiskSpace(string path)
    {
        try
        {
            string root = Path.GetPathRoot(path) ?? path;
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    public Task OpenFolderAsync(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start("explorer.exe", path);
            else if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder: {Path}", path);
        }
        return Task.CompletedTask;
    }

    public Task OpenFileAsync(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open file: {Path}", path);
        }
        return Task.CompletedTask;
    }

    private static async Task<TimeSpan> GetVideoDurationAsync(string filePath)
    {
        // Use a minimal FFprobe approach via process
        try
        {
            var psi = new ProcessStartInfo("ffprobe")
            {
                Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{filePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return TimeSpan.Zero;

            string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await proc.WaitForExitAsync().ConfigureAwait(false);

            if (double.TryParse(output.Trim(), out double seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        catch { /* ffprobe not on PATH or other error */ }

        return TimeSpan.Zero;
    }
}

// ─── Notification Service ─────────────────────────────────────────────────────

namespace WindowsScreenRecorder.Services.Notifications;

using System.Windows;
using WindowsScreenRecorder.Core.Interfaces;

/// <summary>
/// Displays Windows toast notifications via the Win32 Shell_NotifyIcon API
/// wrapped through WPF's NotifyIcon equivalent, and also surfaces in-app
/// banner messages to the main ViewModel.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public event EventHandler<(string Title, string Message, bool IsError)>? NotificationPosted;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public void ShowRecordingStarted() =>
        Post("Recording Started", "Screen recording is now active.", false);

    public void ShowRecordingPaused() =>
        Post("Recording Paused", "Recording has been paused.", false);

    public void ShowRecordingResumed() =>
        Post("Recording Resumed", "Recording has resumed.", false);

    public void ShowRecordingSaved(string filePath, TimeSpan duration) =>
        Post("Recording Saved",
            $"Saved ({duration:hh\\:mm\\:ss}) to {Path.GetFileName(filePath)}", false);

    public void ShowRecordingFailed(string reason) =>
        Post("Recording Failed", reason, true);

    public void ShowError(string title, string message) =>
        Post(title, message, true);

    public void ShowInfo(string title, string message) =>
        Post(title, message, false);

    private void Post(string title, string message, bool isError)
    {
        _logger.LogInformation("Notification: [{Title}] {Message}", title, message);
        NotificationPosted?.Invoke(this, (title, message, isError));
    }
}

// ─── Update Service ───────────────────────────────────────────────────────────

namespace WindowsScreenRecorder.Services.Update;

using System.Net.Http;
using System.Text.Json;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

/// <summary>
/// Checks for application updates by querying a GitHub Releases API endpoint.
/// The update check is fire-and-forget and never blocks the UI.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private const string ApiUrl =
        "https://api.github.com/repos/your-org/windows-screen-recorder/releases/latest";
    private const string CurrentVersion = "1.0.0";

    private readonly ILogger<UpdateService> _logger;
    private static readonly HttpClient Http = new();

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsScreenRecorder/1.0");
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await Http.GetStringAsync(ApiUrl, cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(response);

            string latestVersion = doc.RootElement
                .GetProperty("tag_name")
                .GetString()
                ?.TrimStart('v') ?? "0.0.0";

            if (!IsNewerVersion(latestVersion, CurrentVersion))
                return null;

            string downloadUrl = doc.RootElement
                .GetProperty("assets")[0]
                .GetProperty("browser_download_url")
                .GetString() ?? string.Empty;

            string notes = doc.RootElement
                .GetProperty("body")
                .GetString() ?? string.Empty;

            _logger.LogInformation("Update available: {Version}", latestVersion);

            return new UpdateInfo
            {
                Version = latestVersion,
                DownloadUrl = downloadUrl,
                ReleaseNotes = notes
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update check failed (non-critical)");
            return null;
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(latest, out var l)) return false;
        if (!Version.TryParse(current, out var c)) return false;
        return l > c;
    }
}
