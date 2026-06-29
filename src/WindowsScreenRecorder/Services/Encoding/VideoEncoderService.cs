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
    public Task InitializeAsync(EncoderConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(config.OutputPath) ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task WriteVideoFrameAsync(CaptureFrame frame) => Task.CompletedTask;

    public Task WriteAudioFrameAsync(AudioFrame frame) => Task.CompletedTask;

    public Task FinalizeAsync() => Task.CompletedTask;

    public void Dispose() { }
}
