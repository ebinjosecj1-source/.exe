using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services.Capture;

/// <summary>
/// Captures screen frames using the Windows Graphics Capture API (Windows 10 1903+).
/// This API is preferred over Desktop Duplication because it:
///   1. Captures hardware-accelerated and DRM-protected content (games, video)
///   2. Works without elevation
///   3. Automatically handles monitor changes and window resizes
///   4. Returns frames as GPU textures, minimizing CPU copies
/// Falls back to GDI BitBlt for edge cases where WGC is unavailable.
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    private readonly ILogger<ScreenCaptureService> _logger;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public bool IsCapturing { get; private set; }

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(
        CaptureMode mode,
        MonitorInfo? monitor,
        WindowInfo? window,
        Rect customRegion,
        int targetFps,
        Func<CaptureFrame, Task> frameCallback,
        CancellationToken cancellationToken)
    {
        if (IsCapturing)
            throw new InvalidOperationException("Capture is already running.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsCapturing = true;

        _logger.LogInformation(
            "Starting screen capture: mode={Mode}, fps={Fps}", mode, targetFps);

        try
        {
            if (GraphicsCaptureSession.IsSupported())
            {
                await CaptureViaWgcAsync(
                    mode, monitor, window, customRegion,
                    targetFps, frameCallback, _cts.Token).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(
                    "Windows Graphics Capture API not supported — falling back to GDI");
                await CaptureViaGdiAsync(
                    mode, monitor, window, customRegion,
                    targetFps, frameCallback, _cts.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            IsCapturing = false;
            _logger.LogInformation("Screen capture stopped");
        }
    }

    // ─── Windows Graphics Capture API ─────────────────────────────────────────

    private async Task CaptureViaWgcAsync(
        CaptureMode mode,
        MonitorInfo? monitor,
        WindowInfo? window,
        Rect customRegion,
        int targetFps,
        Func<CaptureFrame, Task> frameCallback,
        CancellationToken ct)
    {
        GraphicsCaptureItem captureItem = mode switch
        {
            CaptureMode.ApplicationWindow when window is not null =>
                CreateCaptureItemForWindow(window.Handle),
            CaptureMode.SingleMonitor when monitor is not null =>
                CreateCaptureItemForMonitor(monitor.DeviceName),
            _ => CreateCaptureItemForFullDesktop()
        };

        var frameQueue = new System.Collections.Concurrent.ConcurrentQueue<(byte[] Data, int W, int H, long Ts)>();
        using var frameArrived = new SemaphoreSlim(0);

        using var device = CreateDirect3DDevice();
        using var framePool = Direct3D11CaptureFramePool.Create(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,  // pool size — 2 frames in flight
            captureItem.Size);

        framePool.FrameArrived += (pool, _) =>
        {
            try
            {
                using var frame = pool.TryGetNextFrame();
                if (frame is null) return;

                var surface = frame.Surface;
                var desc = GetSurfaceDescription(surface);
                var data = CopySurfaceToBytes(surface, desc.Width, desc.Height);

                frameQueue.Enqueue((data, desc.Width, desc.Height,
                    DateTime.UtcNow.Ticks));
                frameArrived.Release();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WGC FrameArrived handler");
            }
        };

        using var session = framePool.CreateCaptureSession(captureItem);
        session.IsCursorCaptureEnabled = true;  // controlled per-frame in the encoder
        session.StartCapture();

        long frameIntervalTicks = TimeSpan.TicksPerSecond / targetFps;
        long nextFrameTicks = DateTime.UtcNow.Ticks;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await frameArrived.WaitAsync(ct).ConfigureAwait(false);

                while (frameQueue.TryDequeue(out var f))
                {
                    long now = DateTime.UtcNow.Ticks;
                    if (now < nextFrameTicks)
                        continue;  // skip if we're ahead of schedule

                    nextFrameTicks = now + frameIntervalTicks;

                    // Crop custom region if needed
                    byte[] frameData = f.Data;
                    int width = f.W, height = f.H;

                    if (mode == CaptureMode.CustomRegion && customRegion != Rect.Empty)
                    {
                        (frameData, width, height) =
                            CropFrame(f.Data, f.W, f.H, customRegion);
                    }

                    var captureFrame = new CaptureFrame
                    {
                        Data = frameData,
                        Width = width,
                        Height = height,
                        TimestampTicks = f.Ts
                    };

                    await frameCallback(captureFrame).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop
        }
    }

    // ─── GDI BitBlt Fallback ──────────────────────────────────────────────────

    private async Task CaptureViaGdiAsync(
        CaptureMode mode,
        MonitorInfo? monitor,
        WindowInfo? window,
        Rect customRegion,
        int targetFps,
        Func<CaptureFrame, Task> frameCallback,
        CancellationToken ct)
    {
        var captureRect = mode switch
        {
            CaptureMode.SingleMonitor when monitor is not null => monitor.Bounds,
            CaptureMode.ApplicationWindow when window is not null => window.Bounds,
            CaptureMode.CustomRegion => customRegion,
            _ => GetFullDesktopBounds()
        };

        int width = (int)captureRect.Width;
        int height = (int)captureRect.Height;
        int x = (int)captureRect.X;
        int y = (int)captureRect.Y;

        // Align dimensions to 2 for encoder compatibility
        width = width & ~1;
        height = height & ~1;

        long frameIntervalMs = 1000 / targetFps;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                long start = Environment.TickCount64;

                var data = CaptureGdi(x, y, width, height);
                var frame = new CaptureFrame
                {
                    Data = data,
                    Width = width,
                    Height = height,
                    TimestampTicks = DateTime.UtcNow.Ticks
                };

                await frameCallback(frame).ConfigureAwait(false);

                long elapsed = Environment.TickCount64 - start;
                long wait = frameIntervalMs - elapsed;
                if (wait > 0)
                    await Task.Delay((int)wait, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop
        }
    }

    private static byte[] CaptureGdi(int x, int y, int width, int height)
    {
        var destBitmap = new System.Drawing.Bitmap(
            width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

        using var gDest = System.Drawing.Graphics.FromImage(destBitmap);
        IntPtr hdcDest = gDest.GetHdc();
        IntPtr hdcSrc = NativeMethods.GetDC(IntPtr.Zero);

        NativeMethods.BitBlt(hdcDest, 0, 0, width, height,
            hdcSrc, x, y, 0x00CC0020 /* SRCCOPY */);

        NativeMethods.ReleaseDC(IntPtr.Zero, hdcSrc);
        gDest.ReleaseHdc(hdcDest);

        var bmpData = destBitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);

        int byteCount = Math.Abs(bmpData.Stride) * height;
        var result = new byte[byteCount];
        Marshal.Copy(bmpData.Scan0, result, 0, byteCount);
        destBitmap.UnlockBits(bmpData);
        destBitmap.Dispose();

        return result;
    }

    // ─── Helper Methods ───────────────────────────────────────────────────────

    private static (byte[] Data, int Width, int Height) CropFrame(
        byte[] src, int srcWidth, int srcHeight, Rect region)
    {
        int x = Math.Max(0, (int)region.X);
        int y = Math.Max(0, (int)region.Y);
        int w = Math.Min((int)region.Width, srcWidth - x) & ~1;
        int h = Math.Min((int)region.Height, srcHeight - y) & ~1;

        var result = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            int srcOffset = ((y + row) * srcWidth + x) * 4;
            int dstOffset = row * w * 4;
            Buffer.BlockCopy(src, srcOffset, result, dstOffset, w * 4);
        }

        return (result, w, h);
    }

    private static Rect GetFullDesktopBounds()
    {
        // Use virtual screen coordinates that span all monitors
        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        double width = SystemParameters.VirtualScreenWidth;
        double height = SystemParameters.VirtualScreenHeight;
        return new Rect(left, top, width, height);
    }

    // These methods wrap the WinRT COM interop — stubs that compile correctly
    // when Microsoft.WindowsAppSDK is referenced.

    private static GraphicsCaptureItem CreateCaptureItemForWindow(IntPtr hwnd)
    {
        var factory = WindowsRuntimeMarshal.GetActivationFactory(
            typeof(GraphicsCaptureItem));
        var interop = (IGraphicsCaptureItemInterop)factory;
        var guid = typeof(GraphicsCaptureItem).GUID;
        interop.CreateForWindow(hwnd, ref guid, out var raw);
        return GraphicsCaptureItem.FromAbi(raw);
    }

    private static GraphicsCaptureItem CreateCaptureItemForMonitor(string deviceName)
    {
        // Find the HMONITOR for the given device name
        IntPtr hMonitor = IntPtr.Zero;
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMon, _, _, _) =>
            {
                var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                if (NativeMethods.GetMonitorInfo(hMon, ref info))
                {
                    string name = new string(info.szDevice).TrimEnd('\0');
                    if (name.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        hMonitor = hMon;
                        return false;  // Stop enumeration
                    }
                }
                return true;
            }, IntPtr.Zero);

        var factory = WindowsRuntimeMarshal.GetActivationFactory(
            typeof(GraphicsCaptureItem));
        var interop = (IGraphicsCaptureItemInterop)factory;
        var guid = typeof(GraphicsCaptureItem).GUID;
        interop.CreateForMonitor(hMonitor, ref guid, out var raw);
        return GraphicsCaptureItem.FromAbi(raw);
    }

    private static GraphicsCaptureItem CreateCaptureItemForFullDesktop()
    {
        // Capture primary monitor for full desktop; a secondary loop handles
        // multi-monitor composition in the encoder phase.
        IntPtr primaryMonitor = NativeMethods.MonitorFromPoint(
            default, NativeMethods.MONITOR_DEFAULTTOPRIMARY);

        var factory = WindowsRuntimeMarshal.GetActivationFactory(
            typeof(GraphicsCaptureItem));
        var interop = (IGraphicsCaptureItemInterop)factory;
        var guid = typeof(GraphicsCaptureItem).GUID;
        interop.CreateForMonitor(primaryMonitor, ref guid, out var raw);
        return GraphicsCaptureItem.FromAbi(raw);
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        // Create a D3D11 device and wrap it as IDirect3DDevice for WGC
        NativeMethods.D3D11CreateDevice(
            IntPtr.Zero,
            3, // D3D_DRIVER_TYPE_HARDWARE
            IntPtr.Zero,
            0x20, // D3D11_CREATE_DEVICE_BGRA_SUPPORT
            null,
            0,
            7, // D3D11_SDK_VERSION
            out var d3dDevice,
            out _,
            out _);

        using var dxgiDevice = (SharpDX.DXGI.Device)d3dDevice.QueryInterface<SharpDX.DXGI.Device>();
        return WindowsRuntimeMarshal.PtrToInterface<IDirect3DDevice>(
            dxgiDevice.NativePointer);
    }

    private static (int Width, int Height) GetSurfaceDescription(IDirect3DSurface surface)
    {
        var desc = surface.Description;
        return (desc.Width, desc.Height);
    }

    private static byte[] CopySurfaceToBytes(IDirect3DSurface surface, int width, int height)
    {
        // Map the GPU texture to CPU memory via DXGI surface map
        var dxgiSurface = (SharpDX.DXGI.Surface)WindowsRuntimeMarshal.
            GetInterface<IDirect3DSurface>(surface, typeof(SharpDX.DXGI.Surface).GUID);

        dxgiSurface.Map(out var map, SharpDX.DXGI.MapFlags.Read);
        try
        {
            int stride = map.Pitch;
            var result = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(map.DataPointer + row * stride,
                    result, row * width * 4, width * 4);
            }
            return result;
        }
        finally
        {
            dxgiSurface.Unmap();
        }
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    // ─── COM Interop Interfaces ───────────────────────────────────────────────

    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        void CreateForWindow(IntPtr window, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
        void CreateForMonitor(IntPtr monitor, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }
}

// Additional native methods for capture
internal static partial class NativeMethods
{
    public const int MONITOR_DEFAULTTOPRIMARY = 1;

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromPoint(System.Drawing.Point pt, int dwFlags);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(
        IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [LibraryImport("d3d11.dll")]
    public static partial int D3D11CreateDevice(
        IntPtr pAdapter,
        int DriverType,
        IntPtr Software,
        uint Flags,
        [MarshalAs(UnmanagedType.LPArray)] int[]? pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out SharpDX.Direct3D11.Device ppDevice,
        out SharpDX.Direct3D.FeatureLevel pFeatureLevel,
        out SharpDX.Direct3D11.DeviceContext ppImmediateContext);
}
