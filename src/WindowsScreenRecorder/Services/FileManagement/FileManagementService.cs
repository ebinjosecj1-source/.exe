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

// âââ Notification Service âââââââââââââââââââââââââââââââââââââââââââââââââââââ
