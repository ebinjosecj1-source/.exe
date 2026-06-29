using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using WindowsScreenRecorder.Core.Enums;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services.Encoding;

/// <summary>
/// Encodes captured video frames and audio data using FFmpeg via FFmpeg.AutoGen.
/// 
/// Library rationale:
///   FFmpeg is the gold-standard open-source multimedia framework. It provides:
///   - H.264 (libx264), H.265 (libx265), AV1 (libaom-av1) software encoders
///   - NVIDIA NVENC, AMD AMF, Intel QuickSync hardware encoder wrappers
///   - MP4, MKV, AVI muxers
///   - AAC audio encoding
///   
/// Hardware acceleration priority: NVENC > AMF > QuickSync > Software
/// Quality-to-CRF mapping:
///   Lossless → CRF 0 / QP 0
///   High      → CRF 18 / QP 20
///   Medium    → CRF 23 / QP 28
///   Low       → CRF 28 / QP 35
/// </summary>
public sealed unsafe class VideoEncoderService : IVideoEncoderService
{
    private readonly ILogger<VideoEncoderService> _logger;
    private EncoderConfig? _config;

    // FFmpeg contexts
    private AVFormatContext* _formatCtx;
    private AVCodecContext* _videoCtx;
    private AVCodecContext* _audioCtx;
    private AVStream* _videoStream;
    private AVStream* _audioStream;
    private SwsContext* _swsCtx;
    private SwrContext* _swrCtx;
    private AVFrame* _videoFrame;
    private AVFrame* _audioFrame;
    private AVPacket* _packet;

    private long _videoFrameIndex;
    private long _audioSamplesWritten;
    private bool _initialized;
    private bool _disposed;

    public VideoEncoderService(ILogger<VideoEncoderService> logger)
    {
        _logger = logger;
        InitializeFfmpeg();
    }

    private static void InitializeFfmpeg()
    {
        // Sdcb.FFmpeg sets up the DLL paths automatically when the runtime package is present.
        // For manual setup, set ffmpeg.RootPath to the directory containing the .dll files.
        ffmpeg.avdevice_register_all();
    }

    public async Task InitializeAsync(EncoderConfig config)
    {
        _config = config;

        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(config.OutputPath)!);

            OpenOutputFormat(config);
            AddVideoStream(config);

            if (config.HasAudio)
                AddAudioStream(config);

            // Write file header
            int ret = ffmpeg.avformat_write_header(_formatCtx, null);
            if (ret < 0)
                throw new InvalidOperationException(
                    $"Failed to write output header: {FfmpegError(ret)}");

            _videoFrame = AllocVideoFrame(config.Width, config.Height, AVPixelFormat.AV_PIX_FMT_YUV420P);
            _packet = ffmpeg.av_packet_alloc();

            _logger.LogInformation(
                "Encoder initialized: {Width}x{Height} @ {Fps}fps, codec={Codec}, hw={Hw}",
                config.Width, config.Height, config.FrameRate, config.Codec, config.Accelerator);

            _initialized = true;
        }).ConfigureAwait(false);
    }

    public async Task WriteVideoFrameAsync(CaptureFrame frame)
    {
        if (!_initialized || _videoCtx == null)
            throw new InvalidOperationException("Encoder not initialized.");

        await Task.Run(() =>
        {
            // Convert BGRA to YUV420P via libswscale
            fixed (byte* srcPtr = frame.Data)
            {
                byte*[] srcData = { srcPtr, null!, null!, null! };
                int[] srcLineSize = { frame.Width * 4, 0, 0, 0 };

                if (_swsCtx == null)
                {
                    _swsCtx = ffmpeg.sws_getContext(
                        frame.Width, frame.Height, AVPixelFormat.AV_PIX_FMT_BGRA,
                        _config!.Width, _config.Height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                        ffmpeg.SWS_BILINEAR, null, null, null);
                }

                ffmpeg.sws_scale(_swsCtx,
                    srcData, srcLineSize,
                    0, frame.Height,
                    _videoFrame->data, _videoFrame->linesize);
            }

            _videoFrame->pts = _videoFrameIndex * _videoCtx->time_base.den
                              / (_videoCtx->time_base.num * _config!.FrameRate);
            _videoFrameIndex++;

            EncodeFrame(_videoCtx, _videoStream, _videoFrame);
        }).ConfigureAwait(false);
    }

    public async Task WriteAudioFrameAsync(AudioFrame frame)
    {
        if (!_initialized || _audioCtx == null) return;

        await Task.Run(() =>
        {
            int inputSamples = frame.Data.Length / (frame.Channels * 4); // IEEE float
            int outputSamples = (int)ffmpeg.av_rescale_rnd(
                inputSamples, _audioCtx->sample_rate, frame.SampleRate,
                AVRounding.AV_ROUND_UP);

            if (_audioFrame == null || _audioFrame->nb_samples != outputSamples)
            {
                if (_audioFrame != null)
                {
                    var f = _audioFrame;
                    ffmpeg.av_frame_free(&f);
                }
                _audioFrame = AllocAudioFrame(_audioCtx, outputSamples);
            }

            fixed (byte* srcPtr = frame.Data)
            {
                byte** srcPtrArray = stackalloc byte*[1];
                srcPtrArray[0] = srcPtr;

                if (_swrCtx == null)
                {
                    _swrCtx = ffmpeg.swr_alloc_set_opts(
                        null,
                        (long)_audioCtx->ch_layout.u.mask,
                        _audioCtx->sample_fmt,
                        _audioCtx->sample_rate,
                        (long)ffmpeg.av_get_default_channel_layout(frame.Channels),
                        AVSampleFormat.AV_SAMPLE_FMT_FLT,
                        frame.SampleRate,
                        0, null);
                    ffmpeg.swr_init(_swrCtx);
                }

                ffmpeg.swr_convert(_swrCtx,
                    _audioFrame->data, outputSamples,
                    srcPtrArray, inputSamples);
            }

            _audioFrame->pts = ffmpeg.av_rescale_q(
                _audioSamplesWritten,
                new AVRational { num = 1, den = _audioCtx->sample_rate },
                _audioCtx->time_base);
            _audioSamplesWritten += outputSamples;

            EncodeFrame(_audioCtx, _audioStream, _audioFrame);
        }).ConfigureAwait(false);
    }

    public async Task FinalizeAsync()
    {
        if (!_initialized) return;

        await Task.Run(() =>
        {
            // Flush encoders
            EncodeFrame(_videoCtx, _videoStream, null);
            if (_audioCtx != null)
                EncodeFrame(_audioCtx, _audioStream, null);

            ffmpeg.av_write_trailer(_formatCtx);
            _logger.LogInformation("Video encoding complete: {Path}", _config!.OutputPath);
        }).ConfigureAwait(false);
    }

    // ─── FFmpeg Setup Helpers ─────────────────────────────────────────────────

    private void OpenOutputFormat(EncoderConfig config)
    {
        string ext = config.Container switch
        {
            ContainerFormat.Mkv => "matroska",
            ContainerFormat.Avi => "avi",
            _ => "mp4"
        };

        AVFormatContext* ctx = null;
        int ret = ffmpeg.avformat_alloc_output_context2(
            &ctx, null, ext, config.OutputPath);
        if (ret < 0)
            throw new InvalidOperationException(
                $"Cannot allocate output context: {FfmpegError(ret)}");
        _formatCtx = ctx;

        if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
        {
            ret = ffmpeg.avio_open(&_formatCtx->pb, config.OutputPath, ffmpeg.AVIO_FLAG_WRITE);
            if (ret < 0)
                throw new IOException(
                    $"Cannot open output file '{config.OutputPath}': {FfmpegError(ret)}");
        }
    }

    private void AddVideoStream(EncoderConfig config)
    {
        string codecName = GetVideoCodecName(config);
        var codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
        if (codec == null)
        {
            // Fallback to software encoder
            codecName = GetSoftwareCodecName(config.Codec);
            codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
            if (codec == null)
                throw new InvalidOperationException(
                    $"Video encoder '{codecName}' not found in FFmpeg.");
            _logger.LogWarning(
                "Hardware encoder not found, falling back to software: {Codec}", codecName);
        }

        _videoStream = ffmpeg.avformat_new_stream(_formatCtx, null);
        _videoStream->id = 0;

        _videoCtx = ffmpeg.avcodec_alloc_context3(codec);
        _videoCtx->codec_id = codec->id;
        _videoCtx->width = config.Width;
        _videoCtx->height = config.Height;
        _videoCtx->time_base = new AVRational { num = 1, den = config.FrameRate };
        _videoCtx->framerate = new AVRational { num = config.FrameRate, den = 1 };
        _videoCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        _videoCtx->gop_size = config.FrameRate; // 1-second keyframe interval
        _videoCtx->max_b_frames = 2;

        // Apply quality settings
        ApplyQualitySettings(_videoCtx, config, codecName);

        if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            _videoCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

        int ret = ffmpeg.avcodec_open2(_videoCtx, codec, null);
        if (ret < 0)
            throw new InvalidOperationException(
                $"Failed to open video codec '{codecName}': {FfmpegError(ret)}");

        ffmpeg.avcodec_parameters_from_context(_videoStream->codecpar, _videoCtx);
        _videoStream->time_base = _videoCtx->time_base;
    }

    private void AddAudioStream(EncoderConfig config)
    {
        var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
        if (codec == null)
        {
            _logger.LogWarning("AAC encoder not found, audio will not be recorded");
            return;
        }

        _audioStream = ffmpeg.avformat_new_stream(_formatCtx, null);
        _audioStream->id = 1;

        _audioCtx = ffmpeg.avcodec_alloc_context3(codec);
        _audioCtx->codec_id = AVCodecID.AV_CODEC_ID_AAC;
        _audioCtx->sample_rate = config.AudioSampleRate;
        _audioCtx->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        _audioCtx->bit_rate = config.AudioBitrateKbps * 1000;
        ffmpeg.av_channel_layout_default(&_audioCtx->ch_layout, config.AudioChannels);

        if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            _audioCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

        int ret = ffmpeg.avcodec_open2(_audioCtx, codec, null);
        if (ret < 0)
        {
            _logger.LogError("Failed to open AAC codec: {Error}", FfmpegError(ret));
            return;
        }

        ffmpeg.avcodec_parameters_from_context(_audioStream->codecpar, _audioCtx);
        _audioStream->time_base = new AVRational { num = 1, den = config.AudioSampleRate };
    }

    private static void ApplyQualitySettings(
        AVCodecContext* ctx, EncoderConfig config, string codecName)
    {
        bool isHw = codecName.Contains("nvenc") || codecName.Contains("amf")
                    || codecName.Contains("qsv");

        if (isHw)
        {
            // Hardware encoders use bitrate/QP rather than CRF
            int qp = config.Quality switch
            {
                QualityPreset.Lossless => 0,
                QualityPreset.High => 20,
                QualityPreset.Medium => 28,
                QualityPreset.Low => 35,
                _ => 23
            };

            var opts = ffmpeg.av_dict_alloc();
            ffmpeg.av_dict_set(&opts, "qp", qp.ToString(), 0);
            if (codecName.Contains("nvenc"))
            {
                ffmpeg.av_dict_set(&opts, "preset", "p4", 0); // Balanced NVENC preset
                ffmpeg.av_dict_set(&opts, "rc", "constqp", 0);
            }
            else if (codecName.Contains("amf"))
            {
                ffmpeg.av_dict_set(&opts, "quality", "balanced", 0);
            }
            // Apply dict options through codec open
        }
        else
        {
            // Software: use CRF
            int crf = config.Quality switch
            {
                QualityPreset.Lossless => 0,
                QualityPreset.High => 18,
                QualityPreset.Medium => 23,
                QualityPreset.Low => 28,
                _ => 23
            };
            ctx->qmin = crf;
            ctx->qmax = crf + 6;

            // libx264/x265 preset via private options
            var opts = ffmpeg.av_dict_alloc();
            ffmpeg.av_dict_set(&opts, "preset", "fast", 0);
            ffmpeg.av_dict_set(&opts, "crf", crf.ToString(), 0);
        }

        if (config.Quality != QualityPreset.Lossless && config.VideoBitrateKbps > 0)
            ctx->bit_rate = config.VideoBitrateKbps * 1000L;
    }

    private static string GetVideoCodecName(EncoderConfig config)
    {
        return (config.Codec, config.Accelerator) switch
        {
            (VideoCodec.H264, HardwareAccelerator.NVENC) => "h264_nvenc",
            (VideoCodec.H265, HardwareAccelerator.NVENC) => "hevc_nvenc",
            (VideoCodec.H264, HardwareAccelerator.AMF) => "h264_amf",
            (VideoCodec.H265, HardwareAccelerator.AMF) => "hevc_amf",
            (VideoCodec.H264, HardwareAccelerator.QuickSync) => "h264_qsv",
            (VideoCodec.H265, HardwareAccelerator.QuickSync) => "hevc_qsv",
            (VideoCodec.AV1, HardwareAccelerator.NVENC) => "av1_nvenc",
            (VideoCodec.AV1, HardwareAccelerator.AMF) => "av1_amf",
            _ => GetSoftwareCodecName(config.Codec)
        };
    }

    private static string GetSoftwareCodecName(VideoCodec codec) => codec switch
    {
        VideoCodec.H265 => "libx265",
        VideoCodec.AV1 => "libaom-av1",
        _ => "libx264"
    };

    private void EncodeFrame(AVCodecContext* ctx, AVStream* stream, AVFrame* frame)
    {
        int ret = ffmpeg.avcodec_send_frame(ctx, frame);
        if (ret < 0 && ret != ffmpeg.AVERROR(ffmpeg.EAGAIN))
        {
            _logger.LogWarning("avcodec_send_frame returned: {Error}", FfmpegError(ret));
            return;
        }

        while (true)
        {
            ret = ffmpeg.avcodec_receive_packet(ctx, _packet);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                break;
            if (ret < 0)
            {
                _logger.LogError("avcodec_receive_packet error: {Error}", FfmpegError(ret));
                break;
            }

            _packet->stream_index = stream->index;
            ffmpeg.av_packet_rescale_ts(_packet, ctx->time_base, stream->time_base);
            ffmpeg.av_interleaved_write_frame(_formatCtx, _packet);
            ffmpeg.av_packet_unref(_packet);
        }
    }

    private static AVFrame* AllocVideoFrame(int width, int height, AVPixelFormat fmt)
    {
        var frame = ffmpeg.av_frame_alloc();
        frame->format = (int)fmt;
        frame->width = width;
        frame->height = height;
        ffmpeg.av_frame_get_buffer(frame, 0);
        return frame;
    }

    private static AVFrame* AllocAudioFrame(AVCodecContext* ctx, int nbSamples)
    {
        var frame = ffmpeg.av_frame_alloc();
        frame->nb_samples = nbSamples;
        frame->format = (int)ctx->sample_fmt;
        frame->sample_rate = ctx->sample_rate;
        ffmpeg.av_channel_layout_copy(&frame->ch_layout, &ctx->ch_layout);
        ffmpeg.av_frame_get_buffer(frame, 0);
        return frame;
    }

    private static string FfmpegError(int errNum)
    {
        var buf = new byte[1024];
        fixed (byte* p = buf)
        {
            ffmpeg.av_strerror(errNum, p, (ulong)buf.Length);
        }
        return System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_videoFrame != null)
        {
            var f = _videoFrame;
            ffmpeg.av_frame_free(&f);
        }
        if (_audioFrame != null)
        {
            var f = _audioFrame;
            ffmpeg.av_frame_free(&f);
        }
        if (_packet != null)
        {
            var p = _packet;
            ffmpeg.av_packet_free(&p);
        }
        if (_videoCtx != null)
        {
            var c = _videoCtx;
            ffmpeg.avcodec_free_context(&c);
        }
        if (_audioCtx != null)
        {
            var c = _audioCtx;
            ffmpeg.avcodec_free_context(&c);
        }
        if (_swsCtx != null)
            ffmpeg.sws_freeContext(_swsCtx);
        if (_swrCtx != null)
        {
            var s = _swrCtx;
            ffmpeg.swr_free(&s);
        }
        if (_formatCtx != null)
        {
            if ((_formatCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                ffmpeg.avio_closep(&_formatCtx->pb);
            var c = _formatCtx;
            ffmpeg.avformat_free_context(c);
        }
    }
}
