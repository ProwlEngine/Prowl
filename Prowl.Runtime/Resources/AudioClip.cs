using Prowl.Runtime.Audio;
using Silk.NET.OpenAL;
using System;
using System.Linq;

namespace Prowl.Runtime
{
    public sealed class AudioClip : EngineObject
    {
        public byte[] Data;
        public BufferAudioFormat Format;
        public int SizeInBytes;
        public int SampleRate;

        public int Channels => GetChannelCount(Format);
        public int BitsPerSample => GetBitsPerSample(Format);
        public float Duration => (float)SampleCount / SampleRate;
        public int SampleCount => Data.Length / Channels;

        public static AudioClip Create(string name, byte[] data, short numChannels, short bitsPerSample, int sampleRate)
        {
            return new AudioClip {
                Name = name,
                Data = data,
                Format = MapFormat(numChannels, bitsPerSample),
                SizeInBytes = data.Length,
                SampleRate = sampleRate
            };
        }

        public static BufferAudioFormat MapFormat(int numChannels, int bitsPerSample) => bitsPerSample switch {
            8 => numChannels == 1 ? BufferAudioFormat.Mono8 : BufferAudioFormat.Stereo8,
            16 => numChannels == 1 ? BufferAudioFormat.Mono16 : BufferAudioFormat.Stereo16,
            32 => numChannels == 1 ? BufferAudioFormat.MonoF : BufferAudioFormat.StereoF,
            _ => throw new NotSupportedException("The specified sound format is not supported."),
        };

        private static int GetChannelCount(BufferAudioFormat format)
        {
            return format == BufferAudioFormat.Mono8 || format == BufferAudioFormat.Mono16 || format == BufferAudioFormat.MonoF ? 1 : 2;
        }

        private static int GetBitsPerSample(BufferAudioFormat format)
        {
            return format == BufferAudioFormat.Mono8 || format == BufferAudioFormat.Stereo8 ? 8 :
                   format == BufferAudioFormat.Mono16 || format == BufferAudioFormat.Stereo16 ? 16 : 32;
        }
    }
}
