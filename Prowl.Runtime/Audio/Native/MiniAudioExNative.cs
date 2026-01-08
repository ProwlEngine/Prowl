// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Runtime.Audio.Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ma_ex_native_data_format
    {
        public ma_format format; /* Sample format. If set to ma_format_unknown, all sample formats are supported. */
        public UInt32 channels; /* If set to 0, all channels are supported. */
        public UInt32 sampleRate; /* If set to 0, all sample rates are supported. */
        public UInt32 flags; /* A combination of MA_DATA_FORMAT_FLAG_* flags. */
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ma_ex_device_info
    {
        public IntPtr pName;
        public Int32 index;
        public Int32 isDefault;
        public UInt32 nativeDataFormatCount;
        public IntPtr nativeDataFormats;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ma_ex_context_config
    {
        public ma_ex_device_info deviceInfo;
        public UInt32 sampleRate;
        public byte channels;
        public UInt32 periodSizeInFrames;
        public unsafe delegate* unmanaged[Cdecl]<ma_device_ptr, IntPtr, IntPtr, uint, void> deviceDataProc;
    }

    public static partial class MiniAudioExNative
    {
        private const string LIB_MINIAUDIO_EX = "miniaudioex";

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_free")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_free(IntPtr pointer);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_playback_devices_get")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_playback_devices_get(out UInt32 count);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_playback_devices_free")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_playback_devices_free(IntPtr pDeviceInfo, UInt32 count);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_context_config_init")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial ma_ex_context_config ma_ex_context_config_init(UInt32 sampleRate, byte channels, UInt32 periodSizeInFrames, ref ma_ex_device_info pDeviceInfo);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_context_init")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_context_init(ref ma_ex_context_config config);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_context_uninit")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_context_uninit(IntPtr context);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_context_set_master_volume")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_context_set_master_volume(IntPtr context, float volume);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_context_get_master_volume")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial float ma_ex_context_get_master_volume(IntPtr context);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_context_get_engine")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_context_get_engine(IntPtr context);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_device_get_user_data")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_device_get_user_data(IntPtr pDevice);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_init")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_audio_source_init(IntPtr context);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_uninit")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_uninit(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_play_from_callback")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial ma_result ma_ex_audio_source_play_from_callback(IntPtr source, IntPtr callback, IntPtr pUserData);

        public static ma_result ma_ex_audio_source_play_from_callback(IntPtr source, ma_procedural_data_source_proc callback, IntPtr pUserData)
        {
            return ma_ex_audio_source_play_from_callback(source, MarshalHelper.GetFunctionPointerForDelegate(callback), pUserData);
        }

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_play_from_file", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial ma_result ma_ex_audio_source_play_from_file(IntPtr source, string filePath, UInt32 streamFromDisk);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_play_from_memory")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial ma_result ma_ex_audio_source_play_from_memory(IntPtr source, IntPtr data, UInt64 dataSize);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_stop")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_stop(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_apply_settings")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_apply_settings(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_volume")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_volume(IntPtr source, float value);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_volume")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial float ma_ex_audio_source_get_volume(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_pitch")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_pitch(IntPtr source, float value);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_pitch")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial float ma_ex_audio_source_get_pitch(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_pan")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_pan(IntPtr source, float value);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_pan")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial float ma_ex_audio_source_get_pan(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_pan_mode")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_pan_mode(IntPtr source, ma_pan_mode mode);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_pan_mode")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial ma_pan_mode ma_ex_audio_source_get_pan_mode(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_pcm_position")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_pcm_position(IntPtr source, UInt64 position);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_pcm_position")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial UInt64 ma_ex_audio_source_get_pcm_position(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_pcm_length")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial UInt64 ma_ex_audio_source_get_pcm_length(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_loop")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_loop(IntPtr source, UInt32 loop);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_loop")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial UInt32 ma_ex_audio_source_get_loop(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_position")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_position(IntPtr source, float x, float y, float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_position")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_get_position(IntPtr source, out float x, out float y, out float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_direction")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_direction(IntPtr source, float x, float y, float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_direction")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_get_direction(IntPtr source, out float x, out float y, out float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_velocity")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_velocity(IntPtr source, float x, float y, float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_velocity")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_get_velocity(IntPtr source, out float x, out float y, out float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_spatialization")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_spatialization(IntPtr source, UInt32 enabled);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_spatialization")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial UInt32 ma_ex_audio_source_get_spatialization(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_attenuation_model")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_attenuation_model(IntPtr source, ma_attenuation_model model);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_attenuation_model")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial ma_attenuation_model ma_ex_audio_source_get_attenuation_model(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_doppler_factor")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_doppler_factor(IntPtr source, float factor);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_doppler_factor")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial float ma_ex_audio_source_get_doppler_factor(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_min_distance")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_min_distance(IntPtr source, float distance);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_min_distance")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial float ma_ex_audio_source_get_min_distance(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_max_distance")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_source_set_max_distance(IntPtr source, float distance);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_max_distance")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial float ma_ex_audio_source_get_max_distance(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_is_playing")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial UInt32 ma_ex_audio_source_get_is_playing(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_is_at_end")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial UInt32 ma_ex_audio_source_get_is_at_end(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_set_group")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial ma_result ma_ex_audio_source_set_group(IntPtr source, IntPtr soundGroup);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_source_get_group")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_audio_source_get_group(IntPtr source);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_init")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_audio_listener_init(IntPtr context);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_uninit")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_uninit(IntPtr listener);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_set_spatialization")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_set_spatialization(IntPtr listener, UInt32 enabled);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_get_spatialization")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial UInt32 ma_ex_audio_listener_get_spatialization(IntPtr listener);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_set_position")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_set_position(IntPtr listener, float x, float y, float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_get_position")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_get_position(IntPtr listener, out float x, out float y, out float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_set_direction")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_set_direction(IntPtr listener, float x, float y, float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_get_direction")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_get_direction(IntPtr listener, out float x, out float y, out float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_set_velocity")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_set_velocity(IntPtr listener, float x, float y, float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_get_velocity")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_get_velocity(IntPtr listener, out float x, out float y, out float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_set_world_up")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_set_world_up(IntPtr listener, float x, float y, float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_get_world_up")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_get_world_up(IntPtr listener, out float x, out float y, out float z);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_set_cone")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_set_cone(IntPtr listener, float innerAngleInRadians, float outerAngleInRadians, float outerGain);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_audio_listener_get_cone")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_audio_listener_get_cone(IntPtr listener, out float innerAngleInRadians, out float outerAngleInRadians, out float outerGain);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_decode_file", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_decode_file(string pFilePath, out UInt64 dataLength, out UInt32 channels, out UInt32 sampleRate, UInt32 desiredChannels, UInt32 desiredSampleRate);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_decode_memory")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_decode_memory(IntPtr pData, UInt64 size, out UInt64 dataLength, out UInt32 channels, out UInt32 sampleRate, UInt32 desiredChannels, UInt32 desiredSampleRate);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_read_pcm_frames")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial ma_result ma_engine_read_pcm_frames(IntPtr pEngine, IntPtr pFramesOut, UInt64 frameCount, out UInt64 pFramesRead);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_sound_group_init")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial IntPtr ma_ex_sound_group_init(IntPtr context);

        [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_ex_sound_group_uninit")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        public static partial void ma_ex_sound_group_uninit(IntPtr soundGroup);
    }
}
