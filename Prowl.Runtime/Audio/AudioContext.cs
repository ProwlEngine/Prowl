// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Audio
{
    public delegate void DeviceDataEvent(NativeArray<float> data, UInt32 frameCount);

    /// <summary>
    /// This class is responsible for managing the audio context.
    /// </summary>
    public static class AudioContext
    {
        private static IntPtr audioContext;
        private static unsafe delegate* unmanaged[Cdecl]<ma_device_ptr, IntPtr, IntPtr, uint, void> deviceDataProc;
        private static Dictionary<UInt64, IntPtr> audioClipHandles = new Dictionary<UInt64, IntPtr>();
        private static AudioBuffer outputBuffer = new AudioBuffer(8192);

        private static UInt32 sampleRate = 44100;
        private static UInt32 channels = 2;
        private static DateTime lastUpdateTime;
        private static float deltaTime;

        public static event DeviceDataEvent DataProcess;

        internal static IntPtr NativeContext
        {
            get
            {
                return audioContext;
            }
        }

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
                return MiniAudioExNative.ma_ex_context_get_master_volume(audioContext);
            }
            set
            {
                MiniAudioExNative.ma_ex_context_set_master_volume(audioContext, value);
            }
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

            ma_ex_context_config contextConfig = MiniAudioExNative.ma_ex_context_config_init(sampleRate, (byte)channels, periodSizeInFrames, ref pDeviceInfo);

            unsafe
            {
                deviceDataProc = &OnDeviceDataProc;
            }

            audioContext = MiniAudioExNative.ma_ex_context_init(ref contextConfig);

            if (audioContext == IntPtr.Zero)
            {
                Console.WriteLine("Failed to initialize MiniAudioEx");
            }

            lastUpdateTime = DateTime.Now;
        }

        /// <summary>
        /// Deinitializes MiniAudioEx. Call this before closing the application.
        /// </summary>
        public static void Deinitialize()
        {
            if (audioContext == IntPtr.Zero)
                return;

            foreach (var audioClipHandle in audioClipHandles.Values)
            {
                if (audioClipHandle != IntPtr.Zero)
                    Marshal.FreeHGlobal(audioClipHandle);
            }

            audioClipHandles.Clear();

            MiniAudioExNative.ma_ex_context_uninit(audioContext);
            audioContext = IntPtr.Zero;
        }

        /// <summary>
        /// Used to calculate delta time and move messages from the audio thread to the main thread. Call this method from within your main thread loop.
        /// </summary>
        public static void Update()
        {
            if (audioContext == IntPtr.Zero)
                return;

            DateTime currentTime = DateTime.Now;
            TimeSpan dt = currentTime - lastUpdateTime;
            deltaTime = (float)dt.TotalSeconds;

            lastUpdateTime = currentTime;
        }

        /// <summary>
        /// Gets an array of available playback devices. Retrieving devices is a relatively slow operation, so don't call it continuously.
        /// </summary>
        /// <returns>An array with playback devices</returns>
        public static DeviceInfo[] GetDevices()
        {
            IntPtr pDevices = MiniAudioExNative.ma_ex_playback_devices_get(out UInt32 count);

            if (pDevices == IntPtr.Zero)
                return null;

            if (count == 0)
            {
                MiniAudioExNative.ma_ex_playback_devices_free(pDevices, count);
                return null;
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

        internal static void Add(AudioClip clip)
        {
            if (clip.Hash == 0)
                return;

            if (clip.Handle == IntPtr.Zero)
                return;

            if (audioClipHandles.ContainsKey(clip.Hash))
                return;

            audioClipHandles.Add(clip.Hash, clip.Handle);
        }

        internal static void Remove(AudioClip clip)
        {
            if (clip.Hash == 0)
                return;

            if (audioClipHandles.ContainsKey(clip.Hash))
            {
                IntPtr handle = audioClipHandles[clip.Hash];
                if (handle != IntPtr.Zero)
                    Marshal.FreeHGlobal(handle);
                audioClipHandles.Remove(clip.Hash);
            }
        }

        internal static bool GetAudioClipHandle(UInt64 hashcode, out IntPtr handle)
        {
            handle = IntPtr.Zero;

            if (audioClipHandles.ContainsKey(hashcode))
            {
                handle = audioClipHandles[hashcode];
                return true;
            }

            return false;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
        private static void OnDeviceDataProc(ma_device_ptr pDevice, IntPtr pOutput, IntPtr pInput, UInt32 frameCount)
        {
            IntPtr pEngine = MiniAudioExNative.ma_ex_device_get_user_data(pDevice.pointer);
            MiniAudioExNative.ma_engine_read_pcm_frames(pEngine, pOutput, frameCount, out _);

            NativeArray<float> buffer = new NativeArray<float>(pOutput, (Int32)(frameCount * channels));

            if (DataProcess != null)
            {
                DataProcess.Invoke(buffer, frameCount);
            }

            outputBuffer.Write(buffer);
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

    public struct DeviceDataFormat
    {
        public ma_format format;       /* Sample format. If set to ma_format_unknown, all sample formats are supported. */
        public UInt32 channels;     /* If set to 0, all channels are supported. */
        public UInt32 sampleRate;   /* If set to 0, all sample rates are supported. */
        public UInt32 flags;        /* A combination of MA_DATA_FORMAT_FLAG_* flags. */
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
                    formats[i].format = f.format;
                    formats[i].sampleRate = f.sampleRate;
                }
            }
        }
    }

    public sealed class ConcurrentList<T>
    {
        private readonly List<T> items;
        private readonly object syncRoot = new object();

        public int Count
        {
            get
            {
                lock (syncRoot)
                {
                    return items.Count;
                }
            }
        }

        public T this[int index]
        {
            get
            {
                lock (syncRoot)
                {
                    return items[index];
                }
            }
            set
            {
                lock (syncRoot)
                {
                    items[index] = value;
                }
            }
        }

        public ConcurrentList()
        {
            this.items = new List<T>();
        }

        public void Clear()
        {
            lock (syncRoot)
            {
                items.Clear();
            }
        }

        public void Add(T item)
        {
            lock (syncRoot)
            {
                items.Add(item);
            }
        }

        public void Remove(T item)
        {
            lock (syncRoot)
            {
                items.Remove(item);
            }
        }

        public void Remove(List<T> items)
        {
            lock (syncRoot)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    this.items.Remove(items[i]);
                }
            }
        }

        public void RemoveAt(int index)
        {
            lock (syncRoot)
            {
                items.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// A thread safe class storing audio data.
    /// </summary>
    public sealed class AudioBuffer
    {
        private readonly float[] buffer;
        private readonly object sync = new();
        private int currentLength = 0;

        public AudioBuffer(int capacityPowerOfTwo)
        {
            if (capacityPowerOfTwo <= 0 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
                throw new ArgumentException("capacityPowerOfTwo must be power of two");
            int capacity = capacityPowerOfTwo;
            buffer = new float[capacity];
        }

        public int Write(NativeArray<float> src)
        {
            lock (sync)
            {
                unsafe
                {
                    fixed (float* pBuffer = &buffer[0])
                    {
                        NativeArray<float> b = new NativeArray<float>(pBuffer, src.Length);
                        src.CopyTo(b);
                        currentLength = src.Length;
                    }

                }
                return src.Length;
            }
        }

        public int Read(ref float[] output)
        {
            lock (sync)
            {
                unsafe
                {
                    if (output?.Length < buffer.Length)
                        output = new float[buffer.Length];

                    fixed (float* pSrc = &buffer[0], pDst = &output[0])
                    {
                        NativeArray<float> src = new NativeArray<float>(pSrc, buffer.Length);
                        NativeArray<float> dst = new NativeArray<float>(pDst, buffer.Length);
                        src.CopyTo(dst);
                        return currentLength;
                    }
                }
            }
        }
    }
}
