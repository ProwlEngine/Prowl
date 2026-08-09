// This software is available as a choice of the following licenses. Choose
// whichever you prefer.

using System;
using System.Runtime.InteropServices;

using Prowl.Echo;
using Prowl.Runtime.Audio;

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
    }

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
