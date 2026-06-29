using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services.Encoding;

/// <summary>Stub video encoder service. Full FFmpeg encoding to be implemented in Phase 2.</summary>
public class VideoEncoderService : IVideoEncoderService
{
    public bool IsEncoding { get; private set; }

    public Task StartEncodingAsync(
        AppSettings settings,
        string outputPath,
        CancellationToken cancellationToken)
    {
        IsEncoding = true;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task EncodeFrameAsync(CaptureFrame frame) => Task.CompletedTask;

    public Task EncodeAudioAsync(AudioFrame audio) => Task.CompletedTask;

    public Task<string> StopEncodingAsync()
    {
        IsEncoding = false;
        return Task.FromResult(string.Empty);
    }

    public void Dispose()
    {
        IsEncoding = false;
    }
}
