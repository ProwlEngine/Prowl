// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen.Abstractions;

namespace Prowl.Runtime.Video;

public sealed class VideoFrame
{
    public byte[] Data = [];
    public int Width;
    public int Height;
    public double Time;
}

public sealed unsafe class FFmpegVideoDecoder : IDisposable
{
    private AVFormatContext* _formatCtx;

    // Video
    private AVCodecContext* _videoCodecCtx;
    private SwsContext* _swsCtx; // Used to convert YUV to RGBA on the CPU
    private int _videoStreamIndex = -1;
    private double _videoTimeBase;

    // Audio
    private AVCodecContext* _audioCodecCtx;
    private SwrContext* _swrCtx;
    private int _audioStreamIndex = -1;
    private double _audioTimeBase;
    public int AudioSampleRate { get; private set; } = 48000;
    public int AudioChannels { get; private set; } = 2;

    private AVFrame* _frame;
    private AVFrame* _audioFrame;
    private AVPacket* _packet;

    private ConcurrentQueue<float[]>? _audioQueue;
    private bool _disposed;

    private AVIOContext* _avioCtx;
    private byte* _avioBuffer;
    private GCHandle _pinnedData;
    private int _dataLength;
    private int _dataPos;
    private avio_alloc_context_read_packet? _readCallback;
    private avio_alloc_context_seek? _seekCallback;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public double Duration { get; private set; }
    public double FrameRate { get; private set; }
    public double CurrentTime { get; private set; }
    public bool HasAudio => _audioStreamIndex >= 0;

    public FFmpegVideoDecoder(ConcurrentQueue<float[]>? audioQueue = null)
    {
        _audioQueue = audioQueue;
    }

    public bool TryOpenFile(string filePath)
    {
        if (!FFmpegLoader.IsAvailable) return false;

        AVFormatContext* formatCtx = ffmpeg.avformat_alloc_context();
        if (ffmpeg.avformat_open_input(&formatCtx, filePath, null, null) < 0)
        {
            ffmpeg.avformat_free_context(formatCtx);
            return false;
        }

        _formatCtx = formatCtx;
        return InitializeStreams();
    }

    public bool TryOpenMemory(byte[] data)
    {
        if (!FFmpegLoader.IsAvailable || data == null || data.Length == 0) return false;

        _pinnedData = GCHandle.Alloc(data, GCHandleType.Pinned);
        _dataLength = data.Length;
        _dataPos = 0;

        const int avioBufferSize = 32 * 1024;
        _avioBuffer = (byte*)ffmpeg.av_malloc(avioBufferSize);

        _readCallback = ReadPacketFromMemory;
        _seekCallback = SeekInMemory;

        _avioCtx = ffmpeg.avio_alloc_context(
            _avioBuffer,
            avioBufferSize,
            0,
            null,
            _readCallback,
            null,
            _seekCallback);

        if (_avioCtx == null)
        {
            Dispose();
            return false;
        }

        AVFormatContext* formatCtx = ffmpeg.avformat_alloc_context();
        formatCtx->pb = _avioCtx;
        formatCtx->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;

        if (ffmpeg.avformat_open_input(&formatCtx, null, null, null) < 0)
        {
            _formatCtx = formatCtx;
            Dispose();
            return false;
        }

        _formatCtx = formatCtx;
        return InitializeStreams();
    }

    private int ReadPacketFromMemory(void* opaque, byte* buf, int bufSize)
    {
        int remaining = _dataLength - _dataPos;
        if (remaining <= 0) return ffmpeg.AVERROR_EOF;

        int toCopy = Math.Min(bufSize, remaining);
        byte* src = (byte*)_pinnedData.AddrOfPinnedObject() + _dataPos;
        Buffer.MemoryCopy(src, buf, bufSize, toCopy);
        _dataPos += toCopy;
        return toCopy;
    }

    private long SeekInMemory(void* opaque, long offset, int whence)
    {
        if (whence == ffmpeg.AVSEEK_SIZE) return _dataLength;

        long target = whence switch
        {
            0 => offset,
            1 => _dataPos + offset,
            2 => _dataLength + offset,
            _ => -1
        };

        if (target < 0 || target > _dataLength) return -1;
        _dataPos = (int)target;
        return _dataPos;
    }

    private bool InitializeStreams()
    {
        if (ffmpeg.avformat_find_stream_info(_formatCtx, null) < 0) return false;

        AVCodec* vCodec = null;
        _videoStreamIndex = ffmpeg.av_find_best_stream(_formatCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vCodec, 0);
        if (_videoStreamIndex >= 0 && vCodec != null)
        {
            AVStream* vStream = _formatCtx->streams[_videoStreamIndex];
            _videoCodecCtx = ffmpeg.avcodec_alloc_context3(vCodec);
            ffmpeg.avcodec_parameters_to_context(_videoCodecCtx, vStream->codecpar);
            _videoCodecCtx->thread_count = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
            ffmpeg.avcodec_open2(_videoCodecCtx, vCodec, null);

            Width = _videoCodecCtx->width;
            Height = _videoCodecCtx->height;
            _videoTimeBase = ffmpeg.av_q2d(vStream->time_base);

            AVRational avgRate = vStream->avg_frame_rate.num != 0 ? vStream->avg_frame_rate : vStream->r_frame_rate;
            FrameRate = avgRate.den != 0 ? ffmpeg.av_q2d(avgRate) : 30.0;

            if (vStream->duration > 0)
                Duration = vStream->duration * _videoTimeBase;
            else if (_formatCtx->duration > 0)
                Duration = _formatCtx->duration / (double)ffmpeg.AV_TIME_BASE;
        }

        if (_audioQueue != null)
        {
            AVCodec* aCodec = null;
            _audioStreamIndex = ffmpeg.av_find_best_stream(_formatCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &aCodec, 0);
            if (_audioStreamIndex >= 0 && aCodec != null)
            {
                AVStream* aStream = _formatCtx->streams[_audioStreamIndex];
                _audioCodecCtx = ffmpeg.avcodec_alloc_context3(aCodec);
                ffmpeg.avcodec_parameters_to_context(_audioCodecCtx, aStream->codecpar);
                ffmpeg.avcodec_open2(_audioCodecCtx, aCodec, null);
                _audioTimeBase = ffmpeg.av_q2d(aStream->time_base);

                _swrCtx = ffmpeg.swr_alloc();
                ffmpeg.av_opt_set_chlayout(_swrCtx, "in_chlayout", &_audioCodecCtx->ch_layout, 0);

                AVChannelLayout outLayout;
                ffmpeg.av_channel_layout_default(&outLayout, AudioChannels);
                ffmpeg.av_opt_set_chlayout(_swrCtx, "out_chlayout", &outLayout, 0);

                ffmpeg.av_opt_set_int(_swrCtx, "in_sample_rate", _audioCodecCtx->sample_rate, 0);
                ffmpeg.av_opt_set_int(_swrCtx, "out_sample_rate", AudioSampleRate, 0);
                ffmpeg.av_opt_set_sample_fmt(_swrCtx, "in_sample_fmt", _audioCodecCtx->sample_fmt, 0);
                ffmpeg.av_opt_set_sample_fmt(_swrCtx, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_FLT, 0);
                ffmpeg.swr_init(_swrCtx);
            }
        }

        _frame = ffmpeg.av_frame_alloc();
        _audioFrame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();

        return _videoStreamIndex >= 0;
    }

    public VideoFrame? DecodeNextVideoFrame(out bool isEOF)
    {
        isEOF = false;
        if (_disposed || _formatCtx == null) return null;

        while (true)
        {
            if (ffmpeg.avcodec_receive_frame(_videoCodecCtx, _frame) == 0)
            {
                var vf = ExtractRGBA(_frame);
                CurrentTime = vf.Time;
                ffmpeg.av_frame_unref(_frame);
                return vf;
            }

            if (_audioCodecCtx != null && ffmpeg.avcodec_receive_frame(_audioCodecCtx, _audioFrame) == 0)
            {
                ExtractAudio(_audioFrame);
                ffmpeg.av_frame_unref(_audioFrame);
                continue;
            }

            int ret = ffmpeg.av_read_frame(_formatCtx, _packet);
            if (ret == ffmpeg.AVERROR_EOF)
            {
                ffmpeg.avcodec_send_packet(_videoCodecCtx, null);
                if (_audioCodecCtx != null) ffmpeg.avcodec_send_packet(_audioCodecCtx, null);
                isEOF = true;
                return null;
            }
            else if (ret < 0)
            {
                isEOF = true;
                return null;
            }

            if (_packet->stream_index == _videoStreamIndex)
                ffmpeg.avcodec_send_packet(_videoCodecCtx, _packet);
            else if (_packet->stream_index == _audioStreamIndex && _audioCodecCtx != null)
                ffmpeg.avcodec_send_packet(_audioCodecCtx, _packet);

            ffmpeg.av_packet_unref(_packet);
        }
    }

    // Direct conversion to RGBA on CPU
    private VideoFrame ExtractRGBA(AVFrame* frame)
    {
        int rgbaSize = Width * Height * 4;
        var vf = new VideoFrame
        {
            Width = Width,
            Height = Height,
            Data = new byte[rgbaSize],
            Time = frame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE ? frame->best_effort_timestamp * _videoTimeBase : CurrentTime
        };

        _swsCtx = ffmpeg.sws_getCachedContext(
            _swsCtx,
            frame->width, frame->height, (AVPixelFormat)frame->format,
            Width, Height, AVPixelFormat.AV_PIX_FMT_RGBA,
            2, // SWS_BILINEAR
            null, null, null);

        fixed (byte* dstPtr = vf.Data)
        {
            byte*[] dstData = [dstPtr, null, null, null];
            int[] dstLinesize = [Width * 4, 0, 0, 0];
            ffmpeg.sws_scale(_swsCtx, frame->data, frame->linesize, 0, frame->height, dstData, dstLinesize);
        }

        return vf;
    }

    private void ExtractAudio(AVFrame* frame)
    {
        if (_swrCtx == null || _audioQueue == null) return;

        int outSamples = ffmpeg.swr_get_out_samples(_swrCtx, frame->nb_samples);
        float[] pcm = new float[outSamples * AudioChannels];

        fixed (float* pOut = pcm)
        {
            byte*[] outData = [(byte*)pOut];
            fixed (byte** pOutData = outData)
            {
                int converted = ffmpeg.swr_convert(_swrCtx, pOutData, outSamples, frame->extended_data, frame->nb_samples);
                if (converted > 0)
                {
                    float[] finalPcm = pcm;
                    if (converted < outSamples)
                    {
                        finalPcm = new float[converted * AudioChannels];
                        Array.Copy(pcm, finalPcm, converted * AudioChannels);
                    }
                    _audioQueue.Enqueue(finalPcm);
                }
            }
        }
    }

    public void Seek(double timeInSeconds)
    {
        if (_disposed || _formatCtx == null || _videoStreamIndex < 0) return;

        long targetPts = (long)(Math.Max(0.0, timeInSeconds) / _videoTimeBase);
        ffmpeg.av_seek_frame(_formatCtx, _videoStreamIndex, targetPts, ffmpeg.AVSEEK_FLAG_BACKWARD);

        if (_videoCodecCtx != null) ffmpeg.avcodec_flush_buffers(_videoCodecCtx);
        if (_audioCodecCtx != null) ffmpeg.avcodec_flush_buffers(_audioCodecCtx);

        CurrentTime = Math.Max(0.0, timeInSeconds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_swsCtx != null)
        {
            SwsContext* sws = _swsCtx;
            ffmpeg.sws_freeContext(sws);
            _swsCtx = null;
        }

        if (_swrCtx != null)
        {
            SwrContext* s = _swrCtx;
            ffmpeg.swr_free(&s);
            _swrCtx = null;
        }

        if (_frame != null)
        {
            AVFrame* f = _frame;
            ffmpeg.av_frame_free(&f);
            _frame = null;
        }

        if (_audioFrame != null)
        {
            AVFrame* f = _audioFrame;
            ffmpeg.av_frame_free(&f);
            _audioFrame = null;
        }

        if (_packet != null)
        {
            AVPacket* p = _packet;
            ffmpeg.av_packet_free(&p);
            _packet = null;
        }

        if (_videoCodecCtx != null)
        {
            AVCodecContext* c = _videoCodecCtx;
            ffmpeg.avcodec_free_context(&c);
            _videoCodecCtx = null;
        }

        if (_audioCodecCtx != null)
        {
            AVCodecContext* c = _audioCodecCtx;
            ffmpeg.avcodec_free_context(&c);
            _audioCodecCtx = null;
        }

        if (_formatCtx != null)
        {
            AVFormatContext* fmt = _formatCtx;
            ffmpeg.avformat_close_input(&fmt);
            _formatCtx = null;
        }

        if (_avioCtx != null)
        {
            if (_avioCtx->buffer != null)
                ffmpeg.av_freep(&_avioCtx->buffer);
            AVIOContext* avio = _avioCtx;
            ffmpeg.avio_context_free(&avio);
            _avioCtx = null;
        }

        if (_pinnedData.IsAllocated)
            _pinnedData.Free();
    }
}
