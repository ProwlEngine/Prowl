// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Runtime.InteropServices;

using Prowl.Echo;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Resources;

/// <summary>
/// Represents audio data that can be played back or streamed by an AudioSource. Supported file types are WAV/MP3/FlAC/OGG.
/// </summary>
public sealed class AudioClip : EngineObject, ISerializable
{
    private string filePath;
    private string clipName;
    private IntPtr handle;
    private UInt64 dataSize;
    private UInt64 hashCode;
    private bool streamFromDisk;

    // Format described by the encoded data, worked out by decoding it once on first ask. There is no
    // cheaper way to ask the decoder, so the PCM is thrown away again and only the shape is kept.
    private bool infoLoaded;
    private int channels;
    private int sampleRate;
    private UInt64 frameCount;

    /// <summary>
    /// If the constructor with 'string filePath' overloaded is used this will contain the file path, or string.Empty otherwise.
    /// </summary>
    /// <value></value>
    public string FilePath
    {
        get { EnsureNotDisposed(); return filePath; }
    }

    /// <summary>
    /// The name of this AudioClip. If the filepath constructor is used it will contain the filepath, otherwise the string is empty.
    /// </summary>
    /// <value></value>
    public string ClipName
    {
        get { EnsureNotDisposed(); return clipName; }
        set { EnsureNotDisposed(); clipName = value; }
    }

    /// <summary>
    /// If true, data will be streamed from disk. This is useful when a sound is longer than just a couple of seconds. If data is loaded from memory, this property has no effect.
    /// </summary>
    /// <value></value>
    public bool StreamFromDisk
    {
        get { EnsureNotDisposed(); return streamFromDisk; }
    }

    /// <summary>
    /// If the constructor with 'byte[] data' overload is used this will contain a pointer to the allocated memory of the data. Do not manually free!
    /// </summary>
    /// <value></value>
    public IntPtr Handle
    {
        get { EnsureNotDisposed(); return handle; }
    }

    /// <summary>
    /// Gets the hash code used to identify the data of this AudioClip. Only applicable if the 'byte[] data' overload is used.
    /// </summary>
    /// <value></value>
    public UInt64 Hash
    {
        get { EnsureNotDisposed(); return hashCode; }
    }

    /// <summary>
    /// If the constructor with 'byte[] data' overload is used this will contain the size of the data in number of bytes.
    /// </summary>
    /// <value></value>
    public UInt64 DataSize
    {
        get
        {
            EnsureNotDisposed();
            if(handle != IntPtr.Zero)
            {
                return dataSize;
            }
            return 0;
        }
    }

    /// <summary>
    /// For the serializer, which constructs the instance before filling it in from Deserialize.
    /// Without it every attempt to load a clip from the asset cache fails to construct and resolves
    /// to null, which is silence with no failure at the point of playback.
    /// </summary>
    private AudioClip()
    {
        filePath = string.Empty;
        clipName = string.Empty;
    }

    /// <summary>
    /// Creates a new AudioClip instance which gets its data from a file on disk. The file must be in an encoded format.
    /// </summary>
    /// <param name="filePath">The filepath of the encoded audio file (WAV/MP3/FLAC/OGG)</param>
    /// <param name="streamFromDisk">If true, streams data from disk rather than loading the entire file into memory for playback. Typically you'd stream from disk if a sound is more than just a couple of seconds long.</param>
    public AudioClip(string filePath, bool streamFromDisk = true)
    {
        if(!System.IO.File.Exists(filePath))
            throw new System.IO.FileNotFoundException("Can't create AudioClip because the file does not exist: " + filePath);

        this.AssetPath = filePath;

        this.filePath = filePath;
        this.clipName = filePath;
        this.streamFromDisk = streamFromDisk;
        this.handle = IntPtr.Zero;
        this.hashCode = 0;
    }

    /// <summary>
    /// Creates a new AudioClip instance which gets its data from memory. The data must be in an encoded format.
    /// </summary>
    /// <param name="data">Must be encoded audio data (either WAV/MP3/FLAC/OGG)</param>
    /// <param name="isUnique">If true, then this clip will not use shared memory. If true, this clip will reuse existing memory if possible.</param>
    public AudioClip(byte[] data, bool isUnique = false)
    {
        if(data == null)
            throw new System.ArgumentException("Can't create AudioClip because the data is null");

        this.filePath = string.Empty;
        this.clipName = string.Empty;
        this.streamFromDisk = false;
        this.dataSize = (UInt64)data.Length;

        AcquireHandle(data, isUnique ? (UInt64)data.GetHashCode() : GetHashCode(data, data.Length));
    }

    /// <summary>
    /// Points this clip at the shared buffer for <paramref name="hash"/> and takes a reference on it,
    /// letting go of whatever it held before. The byte array is the authority on the size, so a stored
    /// length that disagrees with the stored bytes can't make playback read past the allocation.
    /// </summary>
    private void AcquireHandle(byte[] data, UInt64 hash)
    {
        ReleaseHandle();

        handle = AudioContext.AcquireClipHandle(hash, data, data.Length);

        if (handle == IntPtr.Zero)
            return;

        hashCode = hash;
        dataSize = (UInt64)data.Length;
    }

    private void ReleaseHandle()
    {
        if (handle == IntPtr.Zero)
            return;

        AudioContext.ReleaseClipHandle(hashCode);
        handle = IntPtr.Zero;
        hashCode = 0;
        dataSize = 0;

        // The cached format described the data that just went away.
        infoLoaded = false;
        channels = 0;
        sampleRate = 0;
        frameCount = 0;
    }

    #region Format

    /// <summary>Channel count of the decoded audio, 0 if it cannot be decoded.</summary>
    public int Channels
    {
        get { EnsureNotDisposed(); EnsureInfo(); return channels; }
    }

    /// <summary>Sample rate of the decoded audio in hertz, 0 if it cannot be decoded.</summary>
    public int SampleRate
    {
        get { EnsureNotDisposed(); EnsureInfo(); return sampleRate; }
    }

    /// <summary>Length in sample frames. One frame holds one sample for each channel.</summary>
    public UInt64 SampleCount
    {
        get { EnsureNotDisposed(); EnsureInfo(); return frameCount; }
    }

    /// <summary>Length in seconds, 0 if the clip cannot be decoded.</summary>
    public float LengthInSeconds
    {
        get
        {
            EnsureNotDisposed();
            EnsureInfo();
            return sampleRate > 0 ? (float)(frameCount / (double)sampleRate) : 0.0f;
        }
    }

    /// <summary>
    /// Decodes the clip and returns its samples as interleaved 32 bit floats, one frame at a time
    /// across the channels. Returns an empty array if it cannot be decoded.
    /// </summary>
    /// <remarks>
    /// This decodes on every call and hands back a fresh array, so it is for tooling and analysis
    /// (waveforms, beat detection, custom streaming), not something to call per frame.
    /// </remarks>
    public float[] GetSampleData()
    {
        EnsureNotDisposed();

        IntPtr decoded = Decode(out UInt64 sampleCount, out uint decodedChannels, out uint decodedRate);

        if (decoded == IntPtr.Zero)
            return [];

        try
        {
            CaptureInfo(sampleCount, decodedChannels, decodedRate);

            // The count is total samples across every channel, not frames.
            var samples = new float[sampleCount];
            Marshal.Copy(decoded, samples, 0, samples.Length);
            return samples;
        }
        finally
        {
            MiniAudioExNative.ma_ex_free(decoded);
        }
    }

    /// <summary>
    /// Builds a playable clip from raw interleaved samples, for procedurally generated audio.
    /// </summary>
    /// <param name="name">Name for the clip.</param>
    /// <param name="samples">Interleaved 32 bit float samples, one frame at a time across the channels.</param>
    /// <param name="channels">How many channels the samples are interleaved across.</param>
    /// <param name="sampleRate">Sample rate of the samples in hertz.</param>
    /// <remarks>
    /// The samples are wrapped in a WAVE container, because playback decodes an encoded stream and
    /// bare PCM is not one. That keeps a procedural clip identical to an imported one everywhere else.
    /// </remarks>
    public static AudioClip Create(string name, float[] samples, int channels, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels), "A clip needs at least one channel.");
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), "A clip needs a positive sample rate.");

        var clip = new AudioClip(WriteWave(samples, channels, sampleRate))
        {
            Name = name,
            ClipName = name,
        };

        return clip;
    }

    /// <summary>
    /// Wraps interleaved float samples in a minimal WAVE container.
    /// </summary>
    /// <param name="sixteenBit">
    /// Store as 16 bit PCM rather than 32 bit float, halving the size for a noise floor no game source
    /// material gets near. Float keeps a procedurally built clip bit exact through a round trip, which
    /// is why it is the default.
    /// </param>
    internal static byte[] WriteWave(float[] samples, int channels, int sampleRate, bool sixteenBit = false)
    {
        const int formatPcm = 1;
        const int formatIeeeFloat = 3;

        int bytesPerSample = sixteenBit ? 2 : 4;
        int blockAlign = channels * bytesPerSample;
        int dataBytes = samples.Length * bytesPerSample;

        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)(sixteenBit ? formatPcm : formatIeeeFloat));
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign);
        writer.Write((short)blockAlign);
        writer.Write((short)(bytesPerSample * 8));

        writer.Write("data"u8);
        writer.Write(dataBytes);

        if (sixteenBit)
        {
            foreach (float sample in samples)
            {
                // Rounded rather than truncated, and clamped so a sample over full scale wraps to the
                // opposite polarity instead of the loudest possible click.
                float scaled = MathF.Round(Math.Clamp(sample, -1.0f, 1.0f) * short.MaxValue);
                writer.Write((short)scaled);
            }
        }
        else
        {
            foreach (float sample in samples)
                writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Decodes to interleaved floats. The caller frees the result with ma_ex_free.</summary>
    private IntPtr Decode(out UInt64 sampleCount, out uint decodedChannels, out uint decodedRate)
        => Decode(0, 0, out sampleCount, out decodedChannels, out decodedRate);

    private IntPtr Decode(uint desiredChannels, uint desiredRate, out UInt64 sampleCount, out uint decodedChannels, out uint decodedRate)
    {
        // 0 for either desired value means "whatever the source already is".
        if (handle != IntPtr.Zero && dataSize > 0)
            return MiniAudioExNative.ma_ex_decode_memory(handle, dataSize, out sampleCount, out decodedChannels, out decodedRate, desiredChannels, desiredRate);

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            return MiniAudioExNative.ma_ex_decode_file(filePath, out sampleCount, out decodedChannels, out decodedRate, desiredChannels, desiredRate);

        sampleCount = 0;
        decodedChannels = 0;
        decodedRate = 0;
        return IntPtr.Zero;
    }

    /// <summary>The encoded bytes this clip plays from, as they are stored. Empty for a file backed clip.</summary>
    public byte[] GetEncodedData()
    {
        EnsureNotDisposed();

        if (handle == IntPtr.Zero || dataSize == 0)
            return [];

        var data = new byte[dataSize];
        Marshal.Copy(handle, data, 0, data.Length);
        return data;
    }

    /// <summary>
    /// Decodes this clip, optionally converting the channel count and sample rate, and returns the
    /// result as a WAVE stream ready to be stored as an asset. Empty if it cannot be decoded.
    /// </summary>
    /// <param name="targetChannels">Channels to convert to, or 0 to keep the source's.</param>
    /// <param name="targetSampleRate">Sample rate to convert to, or 0 to keep the source's.</param>
    internal byte[] DecodeToWave(uint targetChannels, uint targetSampleRate)
    {
        EnsureNotDisposed();

        IntPtr decoded = Decode(targetChannels, targetSampleRate, out UInt64 sampleCount, out uint decodedChannels, out uint decodedRate);

        if (decoded == IntPtr.Zero || decodedChannels == 0 || sampleCount == 0)
            return [];

        try
        {
            var samples = new float[sampleCount];
            Marshal.Copy(decoded, samples, 0, samples.Length);

            // 16 bit, because this is the asset that ships. Storing the decoder's own float output
            // would double the size of every decompressed clip for a difference nobody can hear.
            return WriteWave(samples, (int)decodedChannels, (int)decodedRate, sixteenBit: true);
        }
        finally
        {
            MiniAudioExNative.ma_ex_free(decoded);
        }
    }

    private void EnsureInfo()
    {
        if (infoLoaded) return;

        infoLoaded = true;

        IntPtr decoded = Decode(out UInt64 sampleCount, out uint decodedChannels, out uint decodedRate);

        if (decoded == IntPtr.Zero)
            return;

        CaptureInfo(sampleCount, decodedChannels, decodedRate);

        // Only the shape is wanted here. Holding the decoded audio would multiply a compressed clip's
        // footprint for the sake of a duration readout.
        MiniAudioExNative.ma_ex_free(decoded);
    }

    private void CaptureInfo(UInt64 sampleCount, uint decodedChannels, uint decodedRate)
    {
        infoLoaded = true;
        channels = (int)decodedChannels;
        sampleRate = (int)decodedRate;
        frameCount = decodedChannels > 0 ? sampleCount / decodedChannels : 0;
    }

    #endregion

    protected override void OnDispose()
    {
        ReleaseHandle();
    }

    ~AudioClip() => Dispose();

    /// <summary>
    /// This methods creates a hash of the given data.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    private UInt64 GetHashCode(byte[] data, int size)
    {
        UInt64 hash = 0;

        for(int i = 0; i < size; i++) 
        {
            hash = data[i] + (hash << 6) + (hash << 16) - hash;
        }

        return hash;            
    }

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        // Save the name
        compound.Add("Name", new EchoObject(clipName ?? string.Empty));

        // Check if this is a file-based clip
        bool isFileBased = !string.IsNullOrEmpty(filePath);
        compound.Add("IsFileBased", new EchoObject(isFileBased));

        if (isFileBased)
        {
            // For file-based clips, just save the file path and streaming flag
            compound.Add("FilePath", new EchoObject(filePath));
            compound.Add("StreamFromDisk", new EchoObject(streamFromDisk));

            // Write empty data for consistency
            compound.Add("AudioData", new EchoObject(new byte[0]));
            compound.Add("DataSize", new EchoObject(0L));
            compound.Add("HashCode", new EchoObject(0L));
        }
        else if (handle != IntPtr.Zero && dataSize > 0)
        {
            // For in-memory clips, serialize the actual data
            // Copy the audio data from unmanaged memory to a managed byte array
            byte[] audioData = new byte[dataSize];
            Marshal.Copy(handle, audioData, 0, (int)dataSize);

            compound.Add("FilePath", new EchoObject(string.Empty));
            compound.Add("StreamFromDisk", new EchoObject(false));
            compound.Add("AudioData", new EchoObject(audioData));
            compound.Add("DataSize", new EchoObject((long)dataSize));
            compound.Add("HashCode", new EchoObject((long)hashCode));
        }
        else
        {
            // Invalid state - no file path and no data
            compound.Add("FilePath", new EchoObject(string.Empty));
            compound.Add("StreamFromDisk", new EchoObject(false));
            compound.Add("AudioData", new EchoObject(new byte[0]));
            compound.Add("DataSize", new EchoObject(0L));
            compound.Add("HashCode", new EchoObject(0L));
        }
    }

    // Every read defaults instead of indexing. A missing key used to throw out of the middle of a
    // load, which takes the rest of the scene down with it rather than costing one clip.
    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        // Restore the name
        clipName = value.Get("Name")?.StringValue ?? string.Empty;
        if (!string.IsNullOrEmpty(clipName))
            Name = clipName;

        bool isFileBased = value.Get("IsFileBased")?.BoolValue ?? false;

        if (isFileBased)
        {
            // Reconstruct file-based clip
            ReleaseHandle();
            filePath = value.Get("FilePath")?.StringValue ?? string.Empty;
            streamFromDisk = value.Get("StreamFromDisk")?.BoolValue ?? false;

            // Verify the file still exists
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                Debug.LogError($"AudioClip '{Name}' points at a file that does not exist: '{filePath}'");
            }
        }
        else
        {
            // Reconstruct in-memory clip
            filePath = string.Empty;
            streamFromDisk = false;

            byte[]? audioData = value.Get("AudioData")?.ByteArrayValue;

            if (audioData == null || audioData.Length == 0)
            {
                ReleaseHandle();
                return;
            }

            // The bytes are the fallback authority for the hash too, so a compound written without
            // one still loads instead of resolving to a clip with no data.
            ulong hash = (ulong)(value.Get("HashCode")?.LongValue ?? 0);
            AcquireHandle(audioData, hash != 0 ? hash : GetHashCode(audioData, audioData.Length));
        }
    }
}
