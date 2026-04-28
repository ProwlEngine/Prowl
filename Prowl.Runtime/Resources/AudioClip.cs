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
        get => filePath;
    }

    /// <summary>
    /// The name of this AudioClip. If the filepath constructor is used it will contain the filepath, otherwise the string is empty.
    /// </summary>
    /// <value></value>
    public string ClipName
    {
        get => clipName;
        set => clipName = value;
    }

    /// <summary>
    /// If true, data will be streamed from disk. This is useful when a sound is longer than just a couple of seconds. If data is loaded from memory, this property has no effect.
    /// </summary>
    /// <value></value>
    public bool StreamFromDisk
    {
        get => streamFromDisk;
    }

    /// <summary>
    /// If the constructor with 'byte[] data' overload is used this will contain a pointer to the allocated memory of the data. Do not manually free!
    /// </summary>
    /// <value></value>
    public IntPtr Handle
    {
        get => handle;
    }

    /// <summary>
    /// Gets the hash code used to identify the data of this AudioClip. Only applicable if the 'byte[] data' overload is used.
    /// </summary>
    /// <value></value>
    public UInt64 Hash
    {
        get => hashCode;
    }

    /// <summary>
    /// If the constructor with 'byte[] data' overload is used this will contain the size of the data in number of bytes.
    /// </summary>
    /// <value></value>
    public UInt64 DataSize
    {
        get
        {
            if(handle != IntPtr.Zero)
            {
                return dataSize;
            }
            return 0;
        }
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

        if(isUnique)
            this.hashCode = (UInt64)data.GetHashCode();
        else
            this.hashCode = GetHashCode(data, data.Length);

        if(AudioContext.GetAudioClipHandle(hashCode, out IntPtr existingHandle))
        {
            handle = existingHandle;
        }
        else
        {
            handle = Marshal.AllocHGlobal(data.Length);

            if(handle != IntPtr.Zero)
            {            
                Marshal.Copy(data, 0, handle, data.Length);
                AudioContext.Add(this);
            }
        }
    }

    public override void OnDispose()
    {
        AudioContext.Remove(this);
    }

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

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        // Restore the name
        clipName = value["Name"].StringValue;

        bool isFileBased = value["IsFileBased"].BoolValue;

        if (isFileBased)
        {
            // Reconstruct file-based clip
            filePath = value["FilePath"].StringValue;
            streamFromDisk = value["StreamFromDisk"].BoolValue;
            handle = IntPtr.Zero;
            hashCode = 0;
            dataSize = 0;

            // Verify the file still exists
            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"AudioClip deserialization warning: File not found: {filePath}");
            }
        }
        else
        {
            // Reconstruct in-memory clip
            long storedDataSize = value["DataSize"].LongValue;

            if (storedDataSize > 0)
            {
                filePath = string.Empty;
                streamFromDisk = false;

                byte[] audioData = value["AudioData"].ByteArrayValue;
                dataSize = (UInt64)storedDataSize;
                hashCode = (UInt64)value["HashCode"].LongValue;

                // Check if we can reuse existing memory
                if (AudioContext.GetAudioClipHandle(hashCode, out IntPtr existingHandle))
                {
                    handle = existingHandle;
                }
                else
                {
                    // Allocate new memory and copy the data
                    handle = Marshal.AllocHGlobal(audioData.Length);

                    if (handle != IntPtr.Zero)
                    {
                        Marshal.Copy(audioData, 0, handle, audioData.Length);
                        AudioContext.Add(this);
                    }
                }
            }
            else
            {
                // Invalid state - no data
                filePath = string.Empty;
                streamFromDisk = false;
                handle = IntPtr.Zero;
                hashCode = 0;
                dataSize = 0;
            }
        }
    }
}
