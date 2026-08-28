// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Audio;

public delegate void DeviceDataEvent(NativeArray<float> data, UInt32 frameCount);

/// <summary>
/// This class is responsible for managing the audio context.
/// </summary>
public static class AudioContext
{
    private static IntPtr audioContext;
    private static unsafe delegate* unmanaged[Cdecl]<ma_device_ptr, IntPtr, IntPtr, uint, void> deviceDataProc;
    private static Dictionary<UInt64, IntPtr> audioClipHandles = new Dictionary<UInt64, IntPtr>();
    // Ref-count per shared handle: clips with identical data share one native allocation, so it must
    // only be freed once the last clip using it is disposed (otherwise: double-free / use-after-free).
    private static Dictionary<UInt64, int> audioClipRefCounts = new Dictionary<UInt64, int>();
    // Guards the two dictionaries above. Add/AddRef/Remove are called from the main thread during
    // normal loading, but AudioClip getting a GC finalizer means Remove can now also run on the
    // dedicated finalizer thread concurrently - a plain Dictionary would corrupt under that race.
    private static readonly object clipTableLock = new();
    private static AudioBuffer outputBuffer = new AudioBuffer(8192);

    private static UInt32 sampleRate = 44100;
    private static UInt32 channels = 2;
    private static long lastUpdateTime;
    private static float deltaTime;
    private static float masterVolume = 1.0f;
    private static UInt32 periodSizeInFrames = 2048;
    private static Int32 deviceIndex = -1;
    private static string deviceName = string.Empty;
    private static bool deviceProcessFailed;
    private static bool suspendedByGame;
    private static bool suspendedByPause;
    private static bool suspensionApplied;
    private static bool deviceLossReported;

    public static event DeviceDataEvent DataProcess;

    /// <summary>
    /// Raised immediately before the device is closed, while everything built from it is still valid.
    /// Release native objects here rather than after the fact: once the context is gone, so is the node
    /// graph they are attached to, and uninitializing them then is a use after free.
    /// </summary>
    public static event Action DeviceClosing;

    internal static IntPtr NativeContext
    {
        get
        {
            return audioContext;
        }
    }

    /// <summary>
    /// Converts a world position, direction or velocity into the space the audio engine works in.
    /// </summary>
    /// <remarks>
    /// Prowl is left handed with +Z forward, the audio engine is right handed with -Z forward, so the
    /// two disagree by a mirror on one axis. Mirroring the vectors that cross the boundary is what
    /// gets all three axes right. Mirroring one of the listener's basis vectors instead (negating its
    /// world up, say) also lines left and right back up, but it does it by flipping the handedness of
    /// the basis, which leaves vertical inverted.
    /// </remarks>
    public static Float3 ToAudioSpace(Float3 vector) => new Float3(vector.X, vector.Y, -vector.Z);

    /// <summary>
    /// True once a device is up. False before <see cref="Initialize"/>, after
    /// <see cref="Deinitialize"/>, if the device failed to open, and for the whole of a headless run.
    /// Nothing may be handed to the native layer while this is false.
    /// </summary>
    public static bool IsInitialized => audioContext != IntPtr.Zero;

    /// <summary>
    /// Bumped every time a device is opened or closed. Anything caching a native object built against
    /// the device compares this to know whether its handle is still the current one or a dangling
    /// pointer from a previous device.
    /// </summary>
    public static int DeviceGeneration { get; private set; }

    /// <summary>
    /// Gets the chosen sample rate.
    /// </summary>
    /// <value></value>
    public static Int32 SampleRate
    {
        get
        {
            return (int)sampleRate;
        }
    }

    public static Int32 Channels
    {
        get
        {
            return (int)channels;
        }
    }

    /// <summary>
    /// Controls the master volume.
    /// </summary>
    /// <value></value>
    public static float MasterVolume
    {
        get
        {
            if (!IsInitialized)
                return masterVolume;
            return MiniAudioExNative.ma_ex_context_get_master_volume(audioContext);
        }
        set
        {
            // Remembered either way, so a volume set before the device opens (settings load) is not
            // lost, and so the getter still answers in a headless run.
            masterVolume = value;
            if (IsInitialized)
                MiniAudioExNative.ma_ex_context_set_master_volume(audioContext, value);
        }
    }

    /// <summary>
    /// Stops the device without closing it, freezing everything that is playing exactly where it is.
    /// Set it for a pause menu, clear it to pick every voice up where it left off.
    /// </summary>
    /// <remarks>
    /// Silencing the master volume instead would leave the mix running, so a game coming back from a
    /// pause would find its music part way past where it stopped.
    ///
    /// Play mode being paused suspends the device too, and the two are tracked apart, so the editor
    /// resuming cannot clear a suspension the game asked for or the other way round.
    /// </remarks>
    public static bool Suspended
    {
        get => suspendedByGame;
        set
        {
            if (suspendedByGame == value)
                return;

            suspendedByGame = value;
            ApplySuspension();
        }
    }

    /// <summary>
    /// Suspends the device for as long as play mode is paused. Driven by the game loop, which is why
    /// this is not the one game code sets: that is <see cref="Suspended"/>.
    /// </summary>
    internal static bool SuspendedByPause
    {
        get => suspendedByPause;
        set
        {
            if (suspendedByPause == value)
                return;

            suspendedByPause = value;
            ApplySuspension();
        }
    }

    /// <summary>True while the device is stopped, for either reason.</summary>
    public static bool IsSuspended => suspendedByGame || suspendedByPause;

    /// <summary>
    /// Brings the device into line with whether anything is asking for it to be suspended. Remembered
    /// either way, so a suspension asked for before the device opened still takes effect when it does.
    /// </summary>
    private static void ApplySuspension()
    {
        if (!IsInitialized)
            return;

        bool suspend = IsSuspended;

        if (suspend == suspensionApplied)
            return;

        var engine = new ma_engine_ptr(MiniAudioExNative.ma_ex_context_get_engine(audioContext));

        ma_result result = suspend
            ? MiniAudioNative.ma_engine_stop(engine)
            : MiniAudioNative.ma_engine_start(engine);

        if (result != ma_result.success)
        {
            Debug.LogWarning($"The audio device could not be {(suspend ? "stopped" : "started")} ({result}).");
            return;
        }

        suspensionApplied = suspend;
    }

    /// <summary>
    /// The elapsed time since last call to 'Update'.
    /// </summary>
    /// <value></value>
    public static float DeltaTime
    {
        get
        {
            return deltaTime;
        }
    }

    /// <summary>
    /// Initializes MiniAudioEx. Call this once at the start of your application.
    /// </summary>
    /// <param name="sampleRate">The sample rate to use. Typical sampling rates are 44100 and 48000.</param>
    /// <param name="channels">The number of channels to use. For most purposes 2 is the best choice (stereo audio).</param>
    /// <param name="periodSizeInFrames">Buffer size for audio processing. This value is a 'hint' so in practice it may be different than what you passed.</param>
    /// <param name="deviceInfo">If left null, a default device is used.</param>
    public static void Initialize(UInt32 sampleRate, UInt32 channels, UInt32 periodSizeInFrames = 2048, DeviceInfo deviceInfo = null)
    {
        if (audioContext != IntPtr.Zero)
            return;

        ma_ex_device_info pDeviceInfo = new ma_ex_device_info();
        pDeviceInfo.index = deviceInfo == null ? -1 : deviceInfo.Index;
        pDeviceInfo.pName = IntPtr.Zero;
        pDeviceInfo.nativeDataFormatCount = 0;
        pDeviceInfo.nativeDataFormats = IntPtr.Zero;

        AudioContext.sampleRate = sampleRate;
        AudioContext.channels = channels;
        AudioContext.periodSizeInFrames = periodSizeInFrames;
        AudioContext.deviceIndex = deviceInfo == null ? -1 : deviceInfo.Index;

        // Kept alongside the index because the index is only meaningful against the enumeration it
        // came from, and reopening this device later happens against a fresh one.
        AudioContext.deviceName = deviceInfo == null ? string.Empty : deviceInfo.Name;

        ma_ex_context_config contextConfig = MiniAudioExNative.ma_ex_context_config_init(sampleRate, (byte)channels, periodSizeInFrames, ref pDeviceInfo);

        unsafe
        {
            deviceDataProc = &OnDeviceDataProc;
        }

        audioContext = MiniAudioExNative.ma_ex_context_init(ref contextConfig);

        if (audioContext == IntPtr.Zero)
        {
            Debug.LogError($"Failed to open an audio device at {sampleRate} Hz, {channels} channels. " +
                           "Audio is disabled for this session.");
            return;
        }

        DeviceGeneration++;
        MiniAudioExNative.ma_ex_context_set_master_volume(audioContext, masterVolume);

        // A device opens running, so one opened while something is asking for silence has to be told.
        suspensionApplied = false;
        ApplySuspension();

        lastUpdateTime = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Reopens a running device with a different format or on a different output. Does nothing if the
    /// settings already match, and nothing at all if no device is open. Opening the first one is
    /// <see cref="Initialize"/>'s job.
    /// </summary>
    /// <remarks>
    /// Everything the old device owned is invalid afterwards, which is what
    /// <see cref="DeviceGeneration"/> exists to tell the rest of the engine. Decoded clip data is not
    /// device owned and survives untouched.
    ///
    /// The no device case is not an oversight. Project settings are applied from paths that never
    /// wanted audio, headless builds and dedicated servers among them, and reopening had no way to
    /// tell those apart from a real format change.
    /// </remarks>
    public static void Restart(UInt32 sampleRate, UInt32 channels, UInt32 periodSizeInFrames = 2048, DeviceInfo deviceInfo = null)
    {
        if (!IsInitialized)
            return;

        int requestedDevice = deviceInfo == null ? -1 : deviceInfo.Index;
        string requestedName = deviceInfo == null ? string.Empty : deviceInfo.Name;

        // The name is compared as well, so asking for the same output after the list has been
        // renumbered is not read as a request to move to whatever now sits at that index.
        if (AudioContext.sampleRate == sampleRate &&
            AudioContext.channels == channels &&
            AudioContext.periodSizeInFrames == periodSizeInFrames &&
            AudioContext.deviceIndex == requestedDevice &&
            AudioContext.deviceName == requestedName)
            return;

        Deinitialize();
        Initialize(sampleRate, channels, periodSizeInFrames, deviceInfo);
    }

    /// <summary>
    /// Closes the device and opens it again on the same settings. For picking a device back up after
    /// it has stopped underneath the engine, which <see cref="Restart"/> will not do because nothing
    /// about the settings has changed.
    /// </summary>
    public static void Reopen()
    {
        if (!IsInitialized)
            return;

        UInt32 rate = sampleRate;
        UInt32 channelCount = channels;
        UInt32 period = periodSizeInFrames;
        DeviceInfo device = deviceIndex >= 0 ? FindDevice(deviceName, deviceIndex) : null;

        Deinitialize();
        Initialize(rate, channelCount, period, device);
    }

    /// <summary>
    /// Finds a playback device by the name it reports, falling back to a position in the list. Null
    /// if neither finds one.
    /// </summary>
    /// <remarks>
    /// Names are what survive between two runs. An index is a position in whatever the backend
    /// enumerated this time, so plugging in a headset renumbers the devices after it and a setting
    /// saved as an index quietly comes back pointing at a different output.
    /// </remarks>
    public static DeviceInfo FindDevice(string name, int index = -1)
    {
        DeviceInfo[] devices = GetDevices();

        if (!string.IsNullOrEmpty(name))
        {
            foreach (DeviceInfo device in devices)
            {
                if (device.Name == name)
                    return device;
            }
        }

        foreach (DeviceInfo device in devices)
        {
            if (index >= 0 && device.Index == index)
                return device;
        }

        return null;
    }

    /// <summary>
    /// Notices a device that has stopped without being asked to and opens it again.
    /// </summary>
    /// <remarks>
    /// A device can go on its own: the output it was using is unplugged, the driver resets, the
    /// system moves the default elsewhere. miniaudio follows the system default on its own for a
    /// device opened without naming one, so this is the backstop for the cases where it cannot, and
    /// those used to be the rest of the session in silence with nothing logged.
    ///
    /// One attempt, then it stays quiet. A machine with no working output would otherwise reopen a
    /// device it cannot have once a frame, and every failure logs.
    /// </remarks>
    private static void RecoverFromDeviceLoss()
    {
        if (IsSuspended)
            return;

        var engine = new ma_engine_ptr(MiniAudioExNative.ma_ex_context_get_engine(audioContext));
        ma_device_ptr device = MiniAudioNative.ma_engine_get_device(engine);

        if (device.pointer == IntPtr.Zero)
            return;

        ma_device_state state = MiniAudioNative.ma_device_get_state(device);

        // Anything mid transition is on its way somewhere on its own account, so it is left alone.
        if (state != ma_device_state.stopped && state != ma_device_state.uninitialized)
        {
            deviceLossReported = false;
            return;
        }

        if (deviceLossReported)
            return;

        deviceLossReported = true;

        Debug.LogWarning("The audio device stopped on its own, which usually means the output it was " +
                         "using went away. Opening it again.");

        Reopen();
    }

    /// <summary>The buffer size the device was opened with, in frames.</summary>
    public static Int32 PeriodSizeInFrames => (int)periodSizeInFrames;

    /// <summary>Index of the device in use, or -1 for the system default.</summary>
    public static Int32 DeviceIndex => deviceIndex;

    /// <summary>
    /// Name of the device in use, empty when the system default was taken. This is what to persist
    /// for a "remember my output" setting, since <see cref="DeviceIndex"/> only means anything
    /// against the enumeration it came from.
    /// </summary>
    public static string DeviceName => deviceName;

    /// <summary>
    /// Closes the device. Clip data outlives it: those are plain allocations the clips themselves own
    /// by reference count, and freeing them here left every live AudioClip holding a dangling pointer,
    /// which is also what made reopening the device impossible.
    /// </summary>
    public static void Deinitialize()
    {
        if (audioContext == IntPtr.Zero)
            return;

        // Last moment anything built from this context can be touched, so holders get to let go while
        // their calls still mean something. After the uninit below, every one of those handles points
        // into a graph that no longer exists.
        try
        {
            DeviceClosing?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError($"A handler threw while the audio device was closing: {ex}");
        }

        MiniAudioExNative.ma_ex_context_uninit(audioContext);
        audioContext = IntPtr.Zero;
        DeviceGeneration++;

        // The next device opens running, whatever this one was left doing.
        suspensionApplied = false;
    }

    /// <summary>
    /// Used to calculate delta time and move messages from the audio thread to the main thread. Call this method from within your main thread loop.
    /// </summary>
    public static void Update()
    {
        if (audioContext == IntPtr.Zero)
            return;

        // Monotonic. DateTime.Now is local time, so a daylight saving shift or a clock correction
        // produced a negative or hour long delta, and this is the only clock doppler is measured on.
        long currentTime = System.Diagnostics.Stopwatch.GetTimestamp();
        deltaTime = (float)((currentTime - lastUpdateTime) / (double)System.Diagnostics.Stopwatch.Frequency);

        lastUpdateTime = currentTime;

        RecoverFromDeviceLoss();
    }

    /// <summary>
    /// The playback devices the system reports, empty if it reports none. Walking them asks the
    /// backend to enumerate the system's hardware and allocates an object per device and per format
    /// each one supports, so this is not something to call every frame.
    /// </summary>
    public static DeviceInfo[] GetDevices()
    {
        IntPtr pDevices = MiniAudioExNative.ma_ex_playback_devices_get(out UInt32 count);

        // Empty rather than null. There is nothing a caller does differently for "the backend would
        // not answer" than for "there are none", and every one of them had to guard against null.
        if (pDevices == IntPtr.Zero)
            return [];

        if (count == 0)
        {
            MiniAudioExNative.ma_ex_playback_devices_free(pDevices, count);
            return [];
        }

        DeviceInfo[] devices = new DeviceInfo[count];

        for (UInt32 i = 0; i < count; i++)
        {
            IntPtr elementPtr = IntPtr.Add(pDevices, (int)i * Marshal.SizeOf<ma_ex_device_info>());
            ma_ex_device_info deviceInfo = Marshal.PtrToStructure<ma_ex_device_info>(elementPtr);
            devices[i] = new DeviceInfo(deviceInfo.pName, deviceInfo.index, deviceInfo.isDefault > 0 ? true : false, deviceInfo.nativeDataFormats, deviceInfo.nativeDataFormatCount);
        }

        MiniAudioExNative.ma_ex_playback_devices_free(pDevices, count);

        return devices;
    }

    /// <summary>
    /// A key with this bit set belongs to a clip that asked for a buffer of its own. Content hashes
    /// give the bit up so the two spaces cannot meet, and a clip that asked not to share cannot be
    /// handed someone else's buffer because a hash happened to equal a counter.
    /// </summary>
    internal const UInt64 UniqueKeyFlag = 1UL << 63;

    /// <summary>Masks a content hash down to the space shared buffers are keyed in.</summary>
    internal const UInt64 ContentKeyMask = ~UniqueKeyFlag;

    private static UInt64 uniqueKeyCounter;

    /// <summary>
    /// Allocates a buffer that is never shared and returns the key it is held under, or IntPtr.Zero
    /// if the allocation failed. Released the same way a shared one is.
    /// </summary>
    internal static IntPtr AcquireUniqueClipHandle(byte[] data, int length, out UInt64 key)
    {
        key = 0;

        if (data == null || length <= 0)
            return IntPtr.Zero;

        lock (clipTableLock)
        {
            IntPtr handle = Marshal.AllocHGlobal(length);

            if (handle == IntPtr.Zero)
                return IntPtr.Zero;

            Marshal.Copy(data, 0, handle, length);

            // A counter rather than a hash of the data, which is the whole point: two clips holding
            // the same bytes have to come out with different keys here.
            key = UniqueKeyFlag | ++uniqueKeyCounter;
            audioClipHandles[key] = handle;
            audioClipRefCounts[key] = 1;
            return handle;
        }
    }

    /// <summary>
    /// Takes a reference on the shared native buffer for <paramref name="hash"/>, allocating and
    /// filling it from <paramref name="data"/> if this is the first caller to ask for it. Release it
    /// with <see cref="ReleaseClipHandle"/>. Returns IntPtr.Zero if the allocation failed.
    /// </summary>
    /// <remarks>
    /// Lookup and insert are one operation on purpose. Splitting them let two clips with identical
    /// data both miss the lookup and both allocate, and it let a caller adopt an existing buffer while
    /// forgetting to count the reference, which freed the buffer out from under the other holder.
    /// </remarks>
    internal static IntPtr AcquireClipHandle(UInt64 hash, byte[] data, int length)
    {
        if (hash == 0 || data == null || length <= 0)
            return IntPtr.Zero;

        lock (clipTableLock)
        {
            if (audioClipHandles.TryGetValue(hash, out IntPtr existing) && existing != IntPtr.Zero)
            {
                audioClipRefCounts[hash] = audioClipRefCounts.TryGetValue(hash, out int count) ? count + 1 : 1;
                return existing;
            }

            IntPtr handle = Marshal.AllocHGlobal(length);

            if (handle == IntPtr.Zero)
                return IntPtr.Zero;

            Marshal.Copy(data, 0, handle, length);
            audioClipHandles[hash] = handle;
            audioClipRefCounts[hash] = 1;
            return handle;
        }
    }

    /// <summary>
    /// Takes another reference on a buffer that is already cached, without needing the bytes it was
    /// built from. Returns false when nothing is cached under <paramref name="hash"/>.
    /// </summary>
    /// <remarks>
    /// This is what lets a buffer outlive the <see cref="AudioClip"/> that loaded it. Playback hands
    /// the raw pointer to the native decoder, which reads it on the audio thread for as long as the
    /// voice sounds, and the clip is free to be disposed in the meantime: an asset reimport does
    /// exactly that, and so does a clip nothing kept a reference to being collected. Freeing the
    /// buffer while a voice is reading it is a use after free with no useful stack.
    /// </remarks>
    internal static bool RetainClipHandle(UInt64 hash)
    {
        if (hash == 0)
            return false;

        lock (clipTableLock)
        {
            if (!audioClipHandles.TryGetValue(hash, out IntPtr handle) || handle == IntPtr.Zero)
                return false;

            audioClipRefCounts[hash] = audioClipRefCounts.TryGetValue(hash, out int count) ? count + 1 : 1;
            return true;
        }
    }

    /// <summary>How many clips currently hold the shared buffer for this hash, 0 if it is not cached.</summary>
    internal static int GetClipRefCount(UInt64 hash)
    {
        lock (clipTableLock)
        {
            return audioClipRefCounts.TryGetValue(hash, out int count) ? count : 0;
        }
    }

    /// <summary>Drops one reference on a shared buffer, freeing it once the last holder lets go.</summary>
    internal static void ReleaseClipHandle(UInt64 hash)
    {
        if (hash == 0)
            return;

        lock (clipTableLock)
        {
            if (!audioClipRefCounts.TryGetValue(hash, out int count))
                return;

            if (count > 1)
            {
                audioClipRefCounts[hash] = count - 1;
                return;
            }

            if (audioClipHandles.TryGetValue(hash, out IntPtr handle) && handle != IntPtr.Zero)
                Marshal.FreeHGlobal(handle);

            audioClipHandles.Remove(hash);
            audioClipRefCounts.Remove(hash);
        }
    }

    // The device thread calls this directly, so an exception leaving it is undefined behaviour rather
    // than a stack trace. A subscriber that throws must not be able to take the process with it.
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnDeviceDataProc(ma_device_ptr pDevice, IntPtr pOutput, IntPtr pInput, UInt32 frameCount)
    {
        IntPtr pEngine = MiniAudioExNative.ma_ex_device_get_user_data(pDevice.pointer);
        MiniAudioExNative.ma_engine_read_pcm_frames(pEngine, pOutput, frameCount, out _);

        try
        {
            NativeArray<float> buffer = new NativeArray<float>(pOutput, (Int32)(frameCount * channels));

            if (DataProcess != null)
            {
                DataProcess.Invoke(buffer, frameCount);
            }

            outputBuffer.Write(buffer);
        }
        catch (Exception ex)
        {
            // Latched: building this message every block would allocate on the device thread forever.
            if (!deviceProcessFailed)
            {
                deviceProcessFailed = true;
                Debug.LogError($"An audio device data handler threw, further failures are not reported: {ex}");
            }
        }
    }

    public static bool GetOutputBuffer(ref float[] buffer, out int length)
    {
        length = outputBuffer.Read(ref buffer);
        return length > 0;
    }
}

public enum AttenuationModel
{
    None,
    Inverse,
    Linear,
    Exponential
}

public enum PanMode
{
    Balance,
    Pan
}

/// <summary>How samples are laid out in a device's native format.</summary>
public enum AudioSampleFormat
{
    /// <summary>The device did not report a format, meaning it accepts any of them.</summary>
    Unknown = 0,
    UInt8 = 1,
    Int16 = 2,
    /// <summary>Tightly packed, three bytes per sample.</summary>
    Int24 = 3,
    Int32 = 4,
    Float32 = 5,
}

/// <summary>One format a playback device reports it can run at natively.</summary>
public struct DeviceDataFormat
{
    /// <summary>Sample format, or Unknown when every format is supported.</summary>
    public AudioSampleFormat format;
    /// <summary>Channel count, or 0 when every count is supported.</summary>
    public UInt32 channels;
    /// <summary>Sample rate, or 0 when every rate is supported.</summary>
    public UInt32 sampleRate;
    /// <summary>Device specific flags reported alongside the format.</summary>
    public UInt32 flags;
}

public sealed class DeviceInfo
{
    private string name;
    private Int32 index;
    private bool isDefault;
    private DeviceDataFormat[] formats;

    public string Name
    {
        get => name;
    }

    public Int32 Index
    {
        get => index;
    }

    public bool IsDefault
    {
        get => isDefault;
    }

    public DeviceDataFormat[] Formats
    {
        get => formats;
    }

    public DeviceInfo(IntPtr pName, Int32 index, bool isDefault, IntPtr pFormats, UInt32 formatCount)
    {
        if (pName != IntPtr.Zero)
            name = Marshal.PtrToStringAnsi(pName);
        else
            name = string.Empty;

        this.index = index;
        this.isDefault = isDefault;

        formats = (formatCount > 0 && pFormats != IntPtr.Zero) ? new DeviceDataFormat[formatCount] : null;

        if (formats != null)
        {
            for (int i = 0; i < formats.Length; i++)
            {
                IntPtr elementPtr = IntPtr.Add(pFormats, i * Marshal.SizeOf<ma_ex_native_data_format>());
                ma_ex_native_data_format f = Marshal.PtrToStructure<ma_ex_native_data_format>(elementPtr);
                formats[i] = new DeviceDataFormat();
                formats[i].channels = f.channels;
                formats[i].flags = f.flags;
                formats[i].format = (AudioSampleFormat)f.format;
                formats[i].sampleRate = f.sampleRate;
            }
        }
    }
}


/// <summary>
/// Hands the most recent block of audio from the audio thread to the game thread, for meters, scopes
/// and spectrum displays.
/// </summary>
/// <remarks>
/// One writer and one reader, and the writer never waits. Both used to take the same lock, which meant
/// the audio thread could block behind the game thread copying the whole buffer out: the game thread
/// stalling is a slow frame, the audio thread stalling is an audible dropout.
///
/// Instead the writer bumps a counter either side of the copy, odd while it is mid-write. The reader
/// takes the counter, copies, and takes it again: unchanged and even means it saw a whole block, and
/// anything else means the writer overtook it, so it retries a few times and then reports nothing new.
/// Missing an occasional block is invisible in a visualiser, which is all this feeds.
/// </remarks>
public sealed class AudioBuffer
{
    private const int ReadAttempts = 4;

    private readonly float[] buffer;
    private volatile int sequence;
    private volatile int currentLength;
    private bool truncationReported;

    public AudioBuffer(int capacityPowerOfTwo)
    {
        if (capacityPowerOfTwo <= 0 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
            throw new ArgumentException("capacityPowerOfTwo must be power of two");
        int capacity = capacityPowerOfTwo;
        buffer = new float[capacity];
    }

    /// <summary>
    /// Copies as much of <paramref name="src"/> as fits and returns how much that was. Anything past
    /// the capacity is dropped rather than written past the end of the array, which is what happened
    /// when the destination was sized from the source and so bounds-checked itself.
    /// </summary>
    public int Write(NativeArray<float> src)
    {
        int count = Math.Min(src.Length, buffer.Length);

        // Latched: this runs on the audio thread, where formatting a message per block is worse
        // than the truncation it is reporting.
        if (src.Length > buffer.Length && !truncationReported)
        {
            truncationReported = true;
            Debug.LogWarning($"AudioBuffer was handed {src.Length} samples but holds {buffer.Length}. The remainder is dropped.");
        }

        // Odd for the duration of the copy, so a reader can tell it caught a half written block.
        sequence++;

        unsafe
        {
            fixed (float* pBuffer = &buffer[0])
            {
                NativeArray<float> source = new NativeArray<float>(src.Pointer, count);
                NativeArray<float> destination = new NativeArray<float>(pBuffer, buffer.Length);
                source.CopyTo(destination);
            }
        }

        currentLength = count;
        sequence++;
        return count;
    }

    /// <summary>
    /// Copies the samples written by the last <see cref="Write"/> into <paramref name="output"/>,
    /// growing it to hold them if it is too small, and returns how many are valid. Returns 0 when the
    /// writer kept overtaking the read, meaning there is nothing consistent to show.
    /// </summary>
    public int Read(ref float[] output)
    {
        for (int attempt = 0; attempt < ReadAttempts; attempt++)
        {
            int before = sequence;

            // Odd means a write is in progress, so there is no point copying yet.
            if ((before & 1) != 0)
                continue;

            int length = currentLength;

            if (length > 0)
            {
                if (output == null || output.Length < length)
                    output = new float[length];

                unsafe
                {
                    // Only the written span, not the whole capacity. Everything past it is stale and
                    // the caller is told not to read it by the return value.
                    fixed (float* pSrc = &buffer[0], pDst = &output[0])
                    {
                        NativeArray<float> src = new NativeArray<float>(pSrc, length);
                        NativeArray<float> dst = new NativeArray<float>(pDst, output.Length);
                        src.CopyTo(dst);
                    }
                }
            }

            // Unchanged means no write started or finished while the copy was happening, so what was
            // copied is one whole block rather than two halves of different ones.
            if (sequence == before)
                return length;
        }

        return 0;
    }
}
