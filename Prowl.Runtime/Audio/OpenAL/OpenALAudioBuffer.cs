using Silk.NET.OpenAL;
using System;

namespace Prowl.Runtime.Audio.OpenAL
{
    public class OpenALAudioBuffer : AudioBuffer, IDisposable
    {
        public uint ID { get; }
        public int ByteCount { get; private set; }

        public OpenALAudioBuffer()
        {
            ID = OpenALEngine.al.GenBuffer();
            ByteCount = 0;
        }

        public override void BufferData(byte[] buffer, BufferAudioFormat format, int sampleRate)
        {
            unsafe
            {
                fixed (void* bufferptr = buffer)
                {
                    OpenALEngine.al.BufferData(ID, MapAudioFormat(format), bufferptr, buffer.Length, sampleRate);
                    ByteCount = buffer.Length;
                }
            }
        }

        private BufferFormat MapAudioFormat(BufferAudioFormat format)
        {
            switch (format)
            {
                case BufferAudioFormat.Mono8:
                    return BufferFormat.Mono8;
                case BufferAudioFormat.Mono16:
                    return BufferFormat.Mono16;
                case BufferAudioFormat.Stereo8:
                    return BufferFormat.Stereo8;
                case BufferAudioFormat.Stereo16:
                    return BufferFormat.Stereo16;
                case BufferAudioFormat.MonoF:
                    return (BufferFormat)65552;
                case BufferAudioFormat.StereoF:
                    return (BufferFormat)65553;
                default:
                    throw new InvalidOperationException("Illegal BufferAudioFormat: " + format);
            }
        }

        public override void Dispose()
        {
            OpenALEngine.al.DeleteBuffer(ID);
        }
    }
}
