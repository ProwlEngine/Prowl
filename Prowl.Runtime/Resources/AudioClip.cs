// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Audio;

namespace Prowl.Runtime;

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
        if (bitsPerSample == 24)
        {
            data = Convert24BitTo16Bit(data);
            bitsPerSample = 16; // Update bits per sample to 16
        }

        return new AudioClip
        {
            Name = name,
            Data = data,
            Format = MapFormat(numChannels, bitsPerSample),
            SizeInBytes = data.Length,
            SampleRate = sampleRate
        };
    }

    /// <summary>
    /// Loads an audio clip from a file (.wav, .mp3, .ogg)
    /// TODO: Implement actual audio file loading
    /// </summary>
    public static AudioClip LoadFromFile(string filePath)
    {
        throw new NotImplementedException("Audio loading from files is not yet implemented. Use AudioClip.Create() to create clips manually.");
    }

    /// <summary>
    /// Loads an audio clip from a stream
    /// TODO: Implement actual audio stream loading
    /// </summary>
    public static AudioClip LoadFromStream(System.IO.Stream stream, string virtualPath)
    {
        throw new NotImplementedException("Audio loading from streams is not yet implemented. Use AudioClip.Create() to create clips manually.");
    }

    public static BufferAudioFormat MapFormat(int numChannels, int bitsPerSample) => bitsPerSample switch
    {
        8  => numChannels == 1 ? BufferAudioFormat.Mono8 : BufferAudioFormat.Stereo8,
        16 => numChannels == 1 ? BufferAudioFormat.Mono16 : BufferAudioFormat.Stereo16,
        32 => numChannels == 1 ? BufferAudioFormat.MonoF : BufferAudioFormat.StereoF,
        _  => throw new NotSupportedException("The specified sound format is not supported."),
    };

    private static byte[] Convert24BitTo16Bit(byte[] data)
    {
        int sampleCount = data.Length / 3;
        byte[] result = new byte[sampleCount * 2];

        for (int i = 0; i < sampleCount; i++)
        {
            // Read 24-bit sample
            int sample = (data[i * 3] & 0xFF) |
                         ((data[i * 3 + 1] & 0xFF) << 8) |
                         ((data[i * 3 + 2] & 0xFF) << 16);

            // Handle sign extension if the sample is negative
            if ((sample & 0x800000) != 0)
            {
                sample |= unchecked((int)0xFF000000); // Sign extend
            }

            // Convert to 16-bit sample by shifting right and truncating
            short sample16 = (short)(sample >> 8);

            // Write 16-bit sample
            result[i * 2] = (byte)(sample16 & 0xFF);
            result[i * 2 + 1] = (byte)((sample16 >> 8) & 0xFF);
        }

        return result;
    }

    private static int GetChannelCount(BufferAudioFormat format)
    {
        return format == BufferAudioFormat.Mono8 || format == BufferAudioFormat.Mono16 || format == BufferAudioFormat.MonoF ? 1 : 2;
    }

    private static int GetBitsPerSample(BufferAudioFormat format)
    {
        return format == BufferAudioFormat.Mono8 || format == BufferAudioFormat.Stereo8 ? 8 :
            format == BufferAudioFormat.Mono16 || format == BufferAudioFormat.Stereo16  ? 16 : 32;
    }
}
