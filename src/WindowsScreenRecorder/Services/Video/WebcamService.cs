using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.Extensions.Logging;
using WindowsScreenRecorder.Core.Interfaces;

namespace WindowsScreenRecorder.Services.Video
{
    /// <summary>
    /// Captures frames from a DirectShow webcam device using AForge.Video.DirectShow.
    /// Each decoded frame is converted to a WPF <see cref="BitmapSource"/> and delivered
    /// to the caller via the <paramref name="frameCallback"/> supplied to <see cref="StartAsync"/>.
    /// The service is intentionally kept stateless between sessions — create a new instance
    /// or call <see cref="StopAsync"/> then <see cref="StartAsync"/> to switch cameras.
    /// </summary>
    public sealed class WebcamService : IWebcamService
    {
        private readonly ILogger<WebcamService> _logger;

        private VideoCaptureDevice? _device;
        private Func<BitmapSource, Task>? _frameCallback;
        private volatile bool _running;
        private bool _disposed;

        public bool IsRunning => _running;

        public WebcamService(ILogger<WebcamService> logger)
        {
            _logger = logger;
        }

        // ────────────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the webcam identified by <paramref name="monikerString"/> and begins
        /// delivering decoded frames to <paramref name="frameCallback"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the service is already running or the device cannot be opened.
        /// </exception>
        public Task StartAsync(string monikerString,
            Func<BitmapSource, Task> frameCallback)
        {
            if (_running)
                throw new InvalidOperationException("WebcamService is already running.");

            if (string.IsNullOrWhiteSpace(monikerString))
                throw new ArgumentException("Moniker string must not be empty.", nameof(monikerString));

            _frameCallback = frameCallback;

            _device = new VideoCaptureDevice(monikerString);
            _device.NewFrame += OnNewFrame;

            try
            {
                _device.Start();
                _running = true;
                _logger.LogInformation("Webcam started: {Moniker}", monikerString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start webcam {Moniker}", monikerString);
                CleanupDevice();
                throw;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Signals the webcam device to stop and waits up to 3 seconds for
        /// the final frame to flush. Safe to call even if not running.
        /// </summary>
        public Task StopAsync()
        {
            if (!_running)
                return Task.CompletedTask;

            _running = false;
            CleanupDevice();
            _logger.LogInformation("Webcam stopped");
            return Task.CompletedTask;
        }

        // ────────────────────────────────────────────────────────────────────────
        // Frame handler
        // ────────────────────────────────────────────────────────────────────────

        private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!_running || _frameCallback is null)
                return;

            try
            {
                // AForge delivers System.Drawing.Bitmap frames; convert to BitmapSource
                // for WPF without referencing System.Drawing from the UI layer.
                BitmapSource bitmapSource = ConvertBitmap(eventArgs.Frame);

                // Fire-and-forget: the callback is async but we don't await here
                // to avoid blocking the AForge capture thread.
                _ = _frameCallback(bitmapSource);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing webcam frame");
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // Bitmap conversion — System.Drawing.Bitmap → WPF BitmapSource
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts an AForge/GDI <see cref="System.Drawing.Bitmap"/> to a frozen WPF
        /// <see cref="BitmapSource"/>. The lock/unlock pattern is used to avoid
        /// copying pixel data through a MemoryStream for performance.
        /// </summary>
        private static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap)
        {
            var rect = new System.Drawing.Rectangle(
                0, 0, bitmap.Width, bitmap.Height);

            System.Drawing.Imaging.BitmapData? data = null;
            try
            {
                data = bitmap.LockBits(
                    rect,
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                BitmapSource source = BitmapSource.Create(
                    data.Width,
                    data.Height,
                    96, 96,
                    PixelFormats.Pbgra32,
                    null,
                    data.Scan0,
                    data.Stride * data.Height,
                    data.Stride);

                // Freeze so it can be safely accessed from non-UI threads
                source.Freeze();
                return source;
            }
            finally
            {
                if (data is not null)
                    bitmap.UnlockBits(data);
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // Cleanup
        // ────────────────────────────────────────────────────────────────────────

        private void CleanupDevice()
        {
            if (_device is null) return;

            _device.NewFrame -= OnNewFrame;

            if (_device.IsRunning)
            {
                _device.SignalToStop();
                _device.WaitForStop();
            }

            _device = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _running = false;
            CleanupDevice();
        }
    }
}
