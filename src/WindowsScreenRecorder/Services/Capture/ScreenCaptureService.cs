using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services.Capture;

public class ScreenCaptureService : IScreenCaptureService
{
    private volatile bool _isCapturing;
    private CancellationTokenSource? _cts;

    public bool IsCapturing => _isCapturing;

    public Task StartAsync(
        CaptureMode mode,
        MonitorInfo? monitor,
        WindowInfo? window,
        Rect customRegion,
        int targetFps,
        Func<CaptureFrame, Task> frameCallback,
        CancellationToken cancellationToken)
    {
        _isCapturing = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            var screenWidth = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920;
            var screenHeight = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080;
            int delay = targetFps > 0 ? 1000 / targetFps : 33;
            while (!token.IsCancellationRequested && _isCapturing)
            {
                try
                {
                    using var bmp = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(bmp);
                    g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenWidth, screenHeight));
                    var bmpData = bmp.LockBits(new Rectangle(0, 0, screenWidth, screenHeight),
                        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var bytes = new byte[Math.Abs(bmpData.Stride) * screenHeight];
                    System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, bytes, 0, bytes.Length);
                    bmp.UnlockBits(bmpData);
                    var frame = new CaptureFrame
                    {
                        Data = bytes,
                        Width = screenWidth,
                        Height = screenHeight,
                        TimestampTicks = DateTime.UtcNow.Ticks
                    };
                    await frameCallback(frame).ConfigureAwait(false);
                }
                catch { }
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
        }, token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _isCapturing = false;
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
