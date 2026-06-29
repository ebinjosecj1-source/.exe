using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services.Capture;

public class ScreenCaptureService : IScreenCaptureService
{
    private bool _isCapturing;
    private CancellationTokenSource? _cts;
    private RecordingConfiguration? _config;
    public event Action<byte[]>? FrameCaptured;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
    [DllImport("user32.dll")]
    private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

    public Task StartCaptureAsync(RecordingConfiguration config, CancellationToken ct)
    {
        _config = config;
        _isCapturing = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task.Run(() => CaptureLoop(_cts.Token));
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        _isCapturing = false;
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task CaptureLoop(CancellationToken ct)
    {
        var screenWidth = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920;
        var screenHeight = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080;
        while (!ct.IsCancellationRequested && _isCapturing)
        {
            try
            {
                using var bmp = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(0, 0, 0, 0, new Size(screenWidth, screenHeight));
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Bmp);
                FrameCaptured?.Invoke(ms.ToArray());
            }
            catch { }
            await Task.Delay(33, ct).ConfigureAwait(false);
        }
    }

    public Task<byte[]> CaptureScreenshotAsync()
    {
        var screenWidth = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920;
        var screenHeight = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080;
        using var bmp = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(0, 0, 0, 0, new Size(screenWidth, screenHeight));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return Task.FromResult(ms.ToArray());
    }
}
