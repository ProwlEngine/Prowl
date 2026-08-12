// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

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
    private bool unique;

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
    /// <param name="isUnique">
    /// Give this clip a buffer of its own instead of sharing one with every other clip holding the
    /// same bytes. Sharing is the default because it is what stops ten prefabs referencing one sound
    /// from holding ten copies of it, and nothing writes to the buffer once it is loaded.
    /// </param>
    public AudioClip(byte[] data, bool isUnique = false)
    {
        if(data == null)
            throw new System.ArgumentException("Can't create AudioClip because the data is null");

        this.filePath = string.Empty;
        this.clipName = string.Empty;
        this.streamFromDisk = false;
        this.dataSize = (UInt64)data.Length;

        AcquireHandle(data, isUnique);
    }

    /// <summary>
    /// Points this clip at a buffer holding <paramref name="data"/> and takes a reference on it,
    /// letting go of whatever it held before. The byte array is the authority on the size, so a stored
    /// length that disagrees with the stored bytes can't make playback read past the allocation.
    /// </summary>
    /// <remarks>
    /// A shared buffer is keyed on a hash of the bytes, so two clips holding the same audio hold one
    /// allocation. A unique one is keyed on a counter from a space content hashes cannot reach, which
    /// is what makes it actually unique: keyed on the identity hash of the array, as it was, two
    /// clips could be handed the same key and share a buffer neither of them expected to share.
    /// </remarks>
    private void AcquireHandle(byte[] data, bool isUnique)
    {
        ReleaseHandle();

        unique = isUnique;

        if (isUnique)
            handle = AudioContext.AcquireUniqueClipHandle(data, data.Length, out hashCode);
        else
            handle = AudioContext.AcquireClipHandle(hashCode = ContentHash(data), data, data.Length);

        if (handle == IntPtr.Zero)
        {
            hashCode = 0;
            return;
        }

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
        unique = false;

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

            if (sampleCount > int.MaxValue)
            {
                Debug.LogError($"AudioClip '{Name}' decodes to {sampleCount} samples, which is more than one array can hold.");
                return [];
            }

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
    /// <remarks>
    /// Written straight into the one array that is returned. A stream to build it in and a copy out
    /// of that stream meant three copies of a decoded clip alive at once during an import, which for
    /// a long track is hundreds of megabytes, most of it on the large object heap.
    /// </remarks>
    internal static byte[] WriteWave(ReadOnlySpan<float> samples, int channels, int sampleRate, bool sixteenBit = false)
    {
        const int HeaderBytes = 44;
        const short FormatPcm = 1;
        const short FormatIeeeFloat = 3;

        int bytesPerSample = sixteenBit ? 2 : 4;
        int blockAlign = channels * bytesPerSample;
        long dataBytes = (long)samples.Length * bytesPerSample;

        if (HeaderBytes + dataBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(samples), "The audio is too large to hold in one WAVE container.");

        var wave = new byte[HeaderBytes + (int)dataBytes];
        Span<byte> header = wave.AsSpan(0, HeaderBytes);

        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], (int)(36 + dataBytes));
        "WAVE"u8.CopyTo(header[8..]);

        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], sixteenBit ? FormatPcm : FormatIeeeFloat);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], (short)(bytesPerSample * 8));

        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], (int)dataBytes);

        Span<byte> body = wave.AsSpan(HeaderBytes);

        if (sixteenBit)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                // Rounded rather than truncated, and clamped so a sample over full scale wraps to the
                // opposite polarity instead of the loudest possible click.
                float scaled = MathF.Round(Math.Clamp(samples[i], -1.0f, 1.0f) * short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(body[(i * 2)..], (short)scaled);
            }
        }
        else if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(samples).CopyTo(body);
        }
        else
        {
            for (int i = 0; i < samples.Length; i++)
                BinaryPrimitives.WriteSingleLittleEndian(body[(i * 4)..], samples[i]);
        }

        return wave;
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
    /// <remarks>
    /// Converted straight out of the decoder's own buffer. Copying it into a managed array first left
    /// the decoded audio alive twice over, and a ten minute track decodes to hundreds of megabytes.
    /// </remarks>
    internal unsafe byte[] DecodeToWave(uint targetChannels, uint targetSampleRate)
    {
        EnsureNotDisposed();

        IntPtr decoded = Decode(targetChannels, targetSampleRate, out UInt64 sampleCount, out uint decodedChannels, out uint decodedRate);

        if (decoded == IntPtr.Zero || decodedChannels == 0 || sampleCount == 0)
            return [];

        try
        {
            if (sampleCount > int.MaxValue)
            {
                Debug.LogError($"AudioClip '{Name}' decodes to {sampleCount} samples, which is more than one asset can hold.");
                return [];
            }

            var samples = new ReadOnlySpan<float>((void*)decoded, (int)sampleCount);

            // 16 bit, because this is the asset that ships. Storing the decoder's own float output
            // would double the size of every decompressed clip for a difference nobody can hear.
            return WriteWave(samples, (int)decodedChannels, (int)decodedRate, sixteenBit: true);
        }
        finally
        {
            MiniAudioExNative.ma_ex_free(decoded);
        }
    }

    /// <summary>
    /// Works out this clip's format if it is not known yet, so an importer can put it in the asset
    /// and nothing has to decode audio at load time to answer how long a clip is.
    /// </summary>
    internal void EnsureFormatLoaded()
    {
        EnsureNotDisposed();
        EnsureInfo();
    }

    private void EnsureInfo()
    {
        if (infoLoaded) return;

        infoLoaded = true;

        // A WAVE container says what it holds in its first few dozen bytes, and everything this
        // engine writes itself is one: a procedurally built clip, and any clip an import converted.
        // Decoding a whole file to read what is written at the front of it would be absurd.
        if (TryReadWaveFormat())
            return;

        // Nothing else describes itself without being decoded, so this is the fallback for a
        // compressed clip that arrived without its format recorded alongside it.
        IntPtr decoded = Decode(out UInt64 sampleCount, out uint decodedChannels, out uint decodedRate);

        if (decoded == IntPtr.Zero)
            return;

        CaptureInfo(sampleCount, decodedChannels, decodedRate);

        // Only the shape is wanted here. Holding the decoded audio would multiply a compressed clip's
        // footprint for the sake of a duration readout.
        MiniAudioExNative.ma_ex_free(decoded);
    }

    /// <summary>
    /// Reads the format out of a WAVE header, if that is what this clip holds. False for anything
    /// else, including a WAVE laid out in a way this does not recognise, which then decodes instead.
    /// </summary>
    private unsafe bool TryReadWaveFormat()
    {
        if (handle == IntPtr.Zero || dataSize < 44)
            return false;

        // Only the header is read. The cap keeps a hand written file with an enormous chunk in front
        // of the format from being walked forever.
        int available = (int)Math.Min(dataSize, 4096);
        var data = new ReadOnlySpan<byte>((void*)handle, available);

        if (!data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
            return false;

        int waveChannels = 0;
        int waveRate = 0;
        int blockAlign = 0;
        int at = 12;

        while (at + 8 <= available)
        {
            ReadOnlySpan<byte> id = data.Slice(at, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at + 4, 4));
            int body = at + 8;

            if (id.SequenceEqual("fmt "u8) && size >= 16 && body + 16 <= available)
            {
                waveChannels = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 2, 2));
                waveRate = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(body + 4, 4));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 12, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (waveChannels <= 0 || waveRate <= 0 || blockAlign <= 0)
                    return false;

                // The declared size is not trusted over the bytes that are actually there, the same
                // way the stored length is not trusted when a clip is deserialized.
                ulong dataBytes = Math.Min(size, dataSize - (ulong)body);

                infoLoaded = true;
                channels = waveChannels;
                sampleRate = waveRate;
                frameCount = dataBytes / (ulong)blockAlign;
                return true;
            }

            // Chunks are padded to an even length, and the padding is not counted in the size.
            at = body + (int)size + ((int)size & 1);
        }

        return false;
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
    /// The key a shared buffer holding <paramref name="data"/> is held under.
    /// </summary>
    /// <remarks>
    /// A digest rather than something rolled by hand, because of what a collision costs here: two
    /// clips landing on one key share a buffer, so one plays the other's audio, and the reference
    /// count frees it while the other is still reading. Neither symptom points anywhere near a hash
    /// function. This runs once per clip when it is loaded, and the hardware does it faster than a
    /// byte at a time loop could.
    ///
    /// The top bit is dropped because <see cref="AudioContext"/> keys clips that asked not to share
    /// above it.
    /// </remarks>
    private static UInt64 ContentHash(byte[] data)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(data, digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest) & AudioContext.ContentKeyMask;
    }

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        // Save the name
        compound.Add("Name", new EchoObject(clipName ?? string.Empty));

        // Free for a WAVE, which everything built here is, and deliberately not decoded when it is
        // not one: working out a compressed clip's format is the importer's job, done once offline,
        // rather than something serializing a scene should discover it has to do.
        if (!infoLoaded)
            TryReadWaveFormat();

        // Carried with the asset so nothing has to decode the audio to find out what shape it is.
        // Only written once it is actually known, since a zero here would be read back as a clip
        // that decodes to nothing rather than as a clip nobody has asked about yet.
        if (infoLoaded && sampleRate > 0)
        {
            compound.Add("Channels", new EchoObject(channels));
            compound.Add("SampleRate", new EchoObject(sampleRate));
            compound.Add("FrameCount", new EchoObject((long)frameCount));
        }

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
            compound.Add("IsUnique", new EchoObject(unique));

            // Written for anything reading the file by hand. The bytes are what the buffer is keyed
            // on when this is loaded back, not this.
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

            // The bytes are the only authority on which buffer this is. The stored key is not read
            // back: a hash that disagrees with the bytes beside it, whether it was written by an
            // older build or edited by hand, would point this clip at somebody else's audio.
            AcquireHandle(audioData, value.Get("IsUnique")?.BoolValue ?? false);
        }

        // After the handle, which clears the format along with whatever buffer it replaced. Recorded
        // by whatever wrote the asset so a compressed clip is not decoded at load time just to answer
        // how long it is. A file written before this was carried falls back to decoding once.
        int storedChannels = value.Get("Channels")?.IntValue ?? 0;
        int storedRate = value.Get("SampleRate")?.IntValue ?? 0;

        if (storedChannels > 0 && storedRate > 0)
        {
            channels = storedChannels;
            sampleRate = storedRate;
            frameCount = (ulong)(value.Get("FrameCount")?.LongValue ?? 0);
            infoLoaded = true;
        }
    }
}
