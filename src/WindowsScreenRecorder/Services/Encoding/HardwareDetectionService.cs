using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services.Encoding;

/// <summary>
/// Detects available hardware video encoders by attempting to create a minimal
/// encoding context for each accelerator. This is more reliable than querying
/// registry entries or driver APIs, since it validates actual functionality.
/// </summary>
public sealed class HardwareDetectionService : IHardwareDetectionService
{
    private readonly ILogger<HardwareDetectionService> _logger;
    private IReadOnlyList<EncoderCapability>? _cached;

    public HardwareDetectionService(ILogger<HardwareDetectionService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<EncoderCapability>> DetectEncodersAsync()
    {
        if (_cached is not null)
            return _cached;

        var results = await Task.Run(DetectAllEncoders).ConfigureAwait(false);
        _cached = results;
        return results;
    }

    public async Task<HardwareAccelerator> GetBestAvailableAsync(VideoCodec codec)
    {
        var encoders = await DetectEncodersAsync().ConfigureAwait(false);

        // Priority: NVENC > AMF > QuickSync > Software
        var order = new[]
        {
            HardwareAccelerator.NVENC,
            HardwareAccelerator.AMF,
            HardwareAccelerator.QuickSync
        };

        foreach (var acc in order)
        {
            var cap = encoders.FirstOrDefault(e => e.Accelerator == acc && e.IsAvailable);
            if (cap is null) continue;

            bool supports = codec switch
            {
                VideoCodec.H265 => cap.SupportsH265,
                VideoCodec.AV1 => cap.SupportsAV1,
                _ => cap.SupportsH264
            };

            if (supports)
            {
                _logger.LogInformation(
                    "Selected hardware accelerator: {Acc} on {Device}",
                    acc, cap.DeviceName);
                return acc;
            }
        }

        _logger.LogInformation("No suitable hardware encoder found, using software");
        return HardwareAccelerator.None;
    }

    public string GetGpuDescription()
    {
        if (_cached is null) return "Detecting...";

        var available = _cached.Where(c => c.IsAvailable).ToList();
        if (!available.Any()) return "No hardware encoder (software encoding)";

        return string.Join(", ", available.Select(c =>
            $"{c.Accelerator}: {c.DeviceName}"));
    }

    // ─── Detection Logic ──────────────────────────────────────────────────────

    private unsafe List<EncoderCapability> DetectAllEncoders()
    {
        var results = new List<EncoderCapability>();

        var tests = new[]
        {
            (HardwareAccelerator.NVENC, "h264_nvenc", "hevc_nvenc", "av1_nvenc"),
            (HardwareAccelerator.AMF,   "h264_amf",   "hevc_amf",   "av1_amf"),
            (HardwareAccelerator.QuickSync, "h264_qsv", "hevc_qsv", (string?)null)
        };

        foreach (var (acc, h264, h265, av1) in tests)
        {
            bool supportsH264 = TestEncoder(h264);
            bool supportsH265 = TestEncoder(h265);
            bool supportsAv1 = av1 is not null && TestEncoder(av1);

            bool available = supportsH264 || supportsH265 || supportsAv1;
            string deviceName = available ? GetDeviceName(acc) : "Not available";

            _logger.LogInformation(
                "Encoder probe {Acc}: H264={H264}, H265={H265}, AV1={AV1}",
                acc, supportsH264, supportsH265, supportsAv1);

            results.Add(new EncoderCapability
            {
                Accelerator = acc,
                DeviceName = deviceName,
                SupportsH264 = supportsH264,
                SupportsH265 = supportsH265,
                SupportsAV1 = supportsAv1,
                IsAvailable = available
            });
        }

        return results;
    }

    private static unsafe bool TestEncoder(string codecName)
    {
        try
        {
            var codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
            if (codec == null) return false;

            var ctx = ffmpeg.avcodec_alloc_context3(codec);
            if (ctx == null) return false;

            try
            {
                ctx->width = 64;
                ctx->height = 64;
                ctx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                ctx->time_base = new AVRational { num = 1, den = 30 };
                ctx->framerate = new AVRational { num = 30, den = 1 };

                int ret = ffmpeg.avcodec_open2(ctx, codec, null);
                return ret >= 0;
            }
            finally
            {
                ffmpeg.avcodec_free_context(&ctx);
            }
        }
        catch
        {
            return false;
        }
    }

    private static string GetDeviceName(HardwareAccelerator acc)
    {
        // Use LibreHardwareMonitor to get the GPU name
        try
        {
            var computer = new LibreHardwareMonitor.Hardware.Computer
            {
                IsGpuEnabled = true
            };
            computer.Open();

            foreach (var hw in computer.Hardware)
            {
                string name = hw.Name.ToUpperInvariant();
                bool matches = acc switch
                {
                    HardwareAccelerator.NVENC => name.Contains("NVIDIA") || name.Contains("GEFORCE") || name.Contains("RTX") || name.Contains("GTX"),
                    HardwareAccelerator.AMF => name.Contains("AMD") || name.Contains("RADEON") || name.Contains("RX "),
                    HardwareAccelerator.QuickSync => name.Contains("INTEL") || name.Contains("UHD") || name.Contains("IRIS"),
                    _ => false
                };

                if (matches)
                {
                    computer.Close();
                    return hw.Name;
                }
            }

            computer.Close();
        }
        catch { /* Hardware monitor may fail in some environments */ }

        return acc switch
        {
            HardwareAccelerator.NVENC => "NVIDIA GPU",
            HardwareAccelerator.AMF => "AMD GPU",
            HardwareAccelerator.QuickSync => "Intel GPU",
            _ => "Unknown"
        };
    }
}
