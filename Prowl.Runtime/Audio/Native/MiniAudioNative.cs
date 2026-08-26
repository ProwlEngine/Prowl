// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Runtime.Audio.Native;

// ma_typedefs
using ma_channel = Byte;
using ma_bool8 = Byte;
using ma_bool32 = UInt32;
using ma_uint8 = Byte;
using ma_uint16 = UInt16;
using ma_int32 = UInt32;
using ma_uint32 = UInt32;
using ma_int64 = Int64;
using ma_uint64 = UInt64;
using ma_handle = IntPtr;
using ma_vfs_file = IntPtr;
using ma_spinlock = UInt32;

// ma_callbacks
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_sound_end_proc(IntPtr pUserData, ma_sound_ptr pSound);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_procedural_data_source_proc(IntPtr pUserData, IntPtr pFramesOut, ma_uint64 frameCount, ma_uint32 channels);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_device_data_proc(ma_device_ptr pDevice, IntPtr pOutput, IntPtr pInput, ma_uint32 frameCount);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_device_notification_proc(ma_device_notification_ptr pNotification);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_stop_proc(ma_device_ptr pDevice);  /* DEPRECATED. Use ma_device_notification_proc instead. */

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_bool32 ma_enum_devices_callback_proc(ma_context_ptr pContext, ma_device_type deviceType, ma_device_info pInfo, IntPtr pUserData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_engine_process_proc(IntPtr pUserData, IntPtr pFramesOut, ma_uint64 frameCount);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_decoder_read_proc(ma_decoder_ptr pDecoder, IntPtr pBufferOut, size_t bytesToRead, out size_t pBytesRead);         /* Returns the number of bytes read. */

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_decoder_seek_proc(ma_decoder_ptr pDecoder, ma_int64 byteOffset, ma_seek_origin origin);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_decoder_tell_proc(ma_decoder_ptr pDecoder, ref ma_int64 pCursor);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_data_source_ptr ma_data_source_get_next_proc(ma_data_source_ptr pDataSource);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_log_callback_proc(IntPtr pUserData, ma_uint32 level, IntPtr pMessage);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_node_vtable_process_proc(ma_node_ptr pNode, IntPtr ppFramesIn, IntPtr pFrameCountIn, IntPtr ppFramesOut, IntPtr pFrameCountOut);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_node_vtable_get_required_input_frame_count_proc(ma_node_ptr pNode, ma_uint32 outputFrameCount, IntPtr pInputFrameCount);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr ma_allocation_callbacks_malloc_proc(size_t sz, IntPtr pUserData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr ma_allocation_callbacks_realloc_proc(IntPtr p, size_t sz, IntPtr pUserData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_allocation_callbacks_free_proc(IntPtr p, IntPtr pUserData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_context_init_proc(ma_context_ptr pContext, ref ma_context_config pConfig, ref ma_backend_callbacks pCallbacks);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_context_uninit_proc(ma_context_ptr pContext);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_context_enumerate_devices_proc(ma_context_ptr pContext, ma_enum_devices_callback_proc callback, IntPtr pUserData);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_context_get_device_info_proc(ma_context_ptr pContext, ma_device_type deviceType, ma_device_id_ptr pDeviceID, ma_device_info_ptr pDeviceInfo);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_init_proc(ma_device_ptr pDevice, ref ma_device_config pConfig, ma_device_descriptor_ptr pDescriptorPlayback, ma_device_descriptor_ptr pDescriptorCapture);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_uninit_proc(ma_device_ptr pDevice);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_start_proc(ma_device_ptr pDevice);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_stop_proc(ma_device_ptr pDevice);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_read_proc(ma_device_ptr pDevice, IntPtr pFrames, ma_uint32 frameCount, IntPtr pFramesRead);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_write_proc(ma_device_ptr pDevice, IntPtr pFrames, ma_uint32 frameCount, IntPtr pFramesWritten);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_dataloop_proc(ma_device_ptr pDevice);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_dataloop_wakeup_proc(ma_device_ptr pDevice);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_backend_device_get_info_proc(ma_device_ptr pDevice, ma_device_type type, ma_device_info_ptr pDeviceInfo);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_data_source_vtable_read_proc(ma_data_source_ptr pDataSource, IntPtr pFramesOut, ma_uint64 frameCount, out UInt64 pFramesRead);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_data_source_vtable_seek_proc(ma_data_source_ptr pDataSource, ma_uint64 frameIndex);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_data_source_vtable_get_data_format_proc(ma_data_source_ptr pDataSource, out ma_format pFormat, out ma_uint32 pChannels, out ma_uint32 pSampleRate, ma_channel_ptr pChannelMap, size_t channelMapCap);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_data_source_vtable_get_cursor_proc(ma_data_source_ptr pDataSource, out UInt64 pCursor);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_data_source_vtable_get_length_proc(ma_data_source_ptr pDataSource, IntPtr pLength);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate ma_result ma_data_source_vtable_set_looping_proc(ma_data_source_ptr pDataSource, ma_bool32 isLooping);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ma_effect_node_process_proc(ma_node_ptr pNode, IntPtr ppFramesIn, IntPtr pFrameCountIn, IntPtr ppFramesOut, IntPtr pFrameCountOut);

// ma_enums
internal enum ma_result
{
    success = 0,
    error = -1,  /* A generic error. */
    invalid_args = -2,
    invalid_operation = -3,
    out_of_memory = -4,
    out_of_range = -5,
    access_denied = -6,
    does_not_exist = -7,
    already_exists = -8,
    too_many_open_files = -9,
    invalid_file = -10,
    too_big = -11,
    path_too_long = -12,
    name_too_long = -13,
    not_directory = -14,
    is_directory = -15,
    directory_not_empty = -16,
    at_end = -17,
    no_space = -18,
    busy = -19,
    io_error = -20,
    interrupt = -21,
    unavailable = -22,
    already_in_use = -23,
    bad_address = -24,
    bad_seek = -25,
    bad_pipe = -26,
    deadlock = -27,
    too_many_links = -28,
    not_implemented = -29,
    no_message = -30,
    bad_message = -31,
    no_data_available = -32,
    invalid_data = -33,
    timeout = -34,
    no_network = -35,
    not_unique = -36,
    not_socket = -37,
    no_address = -38,
    bad_protocol = -39,
    protocol_unavailable = -40,
    protocol_not_supported = -41,
    protocol_family_not_supported = -42,
    address_family_not_supported = -43,
    socket_not_supported = -44,
    connection_reset = -45,
    already_connected = -46,
    not_connected = -47,
    connection_refused = -48,
    no_host = -49,
    in_progress = -50,
    cancelled = -51,
    memory_already_mapped = -52,

    /* General non-standard errors. */
    crc_mismatch = -100,

    /* General miniaudio-specific errors. */
    format_not_supported = -200,
    device_type_not_supported = -201,
    share_mode_not_supported = -202,
    no_backend = -203,
    no_device = -204,
    api_not_found = -205,
    invalid_device_config = -206,
    loop = -207,
    backend_not_enabled = -208,

    /* State errors. */
    device_not_initialized = -300,
    device_already_initialized = -301,
    device_not_started = -302,
    device_not_stopped = -303,

    /* Operation errors. */
    failed_to_init_backend = -400,
    failed_to_open_backend_device = -401,
    failed_to_start_backend_device = -402,
    failed_to_stop_backend_device = -403
}

internal enum ma_standard_channel_map
{
    microsoft,
    alsa,
    rfc3551,   /* Based off AIFF. */
    flac,
    vorbis,
    sound4,    /* FreeBSD's sound(4). */
    sndio,     /* www.sndio.org/tips.html */
    webaudio = flac, /* https://webaudio.github.io/web-audio-api/#ChannelOrdering. Only 1, 2, 4 and 6 channels are defined, but can fill in the gaps with logical assumptions. */
    standard = microsoft
}

internal enum ma_device_notification_type
{
    started,
    stopped,
    rerouted,
    interruption_began,
    interruption_ended,
    unlocked
}

internal enum ma_resource_manager_data_supply_type
{
    unknown = 0,   /* Used for determining whether or the data supply has been initialized. */
    encoded,       /* Data supply is an encoded buffer. Connector is ma_decoder. */
    decoded,       /* Data supply is a decoded buffer. Connector is ma_audio_buffer. */
    decoded_paged  /* Data supply is a linked list of decoded buffers. Connector is ma_paged_audio_buffer. */
}

internal enum ma_seek_origin
{
    start,
    current,
    end  /* Not used by decoders. */
}

internal enum ma_performance_profile
{
    low_latency = 0,
    conservative
}

internal enum ma_channel_mix_mode
{
    rectangular = 0,   /* Simple averaging based on the plane(s) the channel is sitting on. */
    simple,            /* Drop excess channels; zeroed out extra channels. */
    custom_weights,    /* Use custom weights specified in ma_channel_converter_config. */
    standard = rectangular // Actually called 'ma_channel_mix_mode_default' but 'default' is a reserved keyword in C#
}

internal enum ma_wasapi_usage
{
    standard = 0, // Actually called 'ma_wasapi_usage_default' but 'default' is a reserved keyword in C#
    games,
    pro_audio,
}

internal enum ma_opensl_stream_type
{
    standard = 0,              /* Leaves the stream type unset. */
    voice,                    /* SL_ANDROID_STREAM_VOICE */
    system,                   /* SL_ANDROID_STREAM_SYSTEM */
    ring,                     /* SL_ANDROID_STREAM_RING */
    media,                    /* SL_ANDROID_STREAM_MEDIA */
    alarm,                    /* SL_ANDROID_STREAM_ALARM */
    notification              /* SL_ANDROID_STREAM_NOTIFICATION */
}

internal enum ma_opensl_recording_preset
{
    standard = 0,         /* Leaves the input preset unset. */
    generic,             /* SL_ANDROID_RECORDING_PRESET_GENERIC */
    camcorder,           /* SL_ANDROID_RECORDING_PRESET_CAMCORDER */
    voice_recognition,   /* SL_ANDROID_RECORDING_PRESET_VOICE_RECOGNITION */
    voice_communication, /* SL_ANDROID_RECORDING_PRESET_VOICE_COMMUNICATION */
    voice_unprocessed    /* SL_ANDROID_RECORDING_PRESET_UNPROCESSED */
}

internal enum ma_aaudio_usage
{
    standard = 0,                    /* Leaves the usage type unset. */
    media,                          /* AAUDIO_USAGE_MEDIA */
    voice_communication,            /* AAUDIO_USAGE_VOICE_COMMUNICATION */
    voice_communication_signalling, /* AAUDIO_USAGE_VOICE_COMMUNICATION_SIGNALLING */
    alarm,                          /* AAUDIO_USAGE_ALARM */
    notification,                   /* AAUDIO_USAGE_NOTIFICATION */
    notification_ringtone,          /* AAUDIO_USAGE_NOTIFICATION_RINGTONE */
    notification_event,             /* AAUDIO_USAGE_NOTIFICATION_EVENT */
    assistance_accessibility,       /* AAUDIO_USAGE_ASSISTANCE_ACCESSIBILITY */
    assistance_navigation_guidance, /* AAUDIO_USAGE_ASSISTANCE_NAVIGATION_GUIDANCE */
    assistance_sonification,        /* AAUDIO_USAGE_ASSISTANCE_SONIFICATION */
    game,                           /* AAUDIO_USAGE_GAME */
    assitant,                       /* AAUDIO_USAGE_ASSISTANT */
    emergency,                      /* AAUDIO_SYSTEM_USAGE_EMERGENCY */
    safety,                         /* AAUDIO_SYSTEM_USAGE_SAFETY */
    vehicle_status,                 /* AAUDIO_SYSTEM_USAGE_VEHICLE_STATUS */
    announcement                    /* AAUDIO_SYSTEM_USAGE_ANNOUNCEMENT */
}

internal enum ma_aaudio_content_type
{
    standard = 0,             /* Leaves the content type unset. */
    speech,                  /* AAUDIO_CONTENT_TYPE_SPEECH */
    music,                   /* AAUDIO_CONTENT_TYPE_MUSIC */
    movie,                   /* AAUDIO_CONTENT_TYPE_MOVIE */
    sonification             /* AAUDIO_CONTENT_TYPE_SONIFICATION */
}

internal enum ma_aaudio_input_preset
{
    standard = 0,             /* Leaves the input preset unset. */
    generic,                 /* AAUDIO_INPUT_PRESET_GENERIC */
    camcorder,               /* AAUDIO_INPUT_PRESET_CAMCORDER */
    voice_recognition,       /* AAUDIO_INPUT_PRESET_VOICE_RECOGNITION */
    voice_communication,     /* AAUDIO_INPUT_PRESET_VOICE_COMMUNICATION */
    unprocessed,             /* AAUDIO_INPUT_PRESET_UNPROCESSED */
    voice_performance        /* AAUDIO_INPUT_PRESET_VOICE_PERFORMANCE */
}

internal enum ma_aaudio_allowed_capture_policy
{
    standard = 0,            /* Leaves the allowed capture policy unset. */
    by_all,                 /* AAUDIO_ALLOW_CAPTURE_BY_ALL */
    by_system,              /* AAUDIO_ALLOW_CAPTURE_BY_SYSTEM */
    by_none                 /* AAUDIO_ALLOW_CAPTURE_BY_NONE */
}

internal enum ma_resample_algorithm
{
    linear = 0,    /* Fastest, lowest quality. Optional low-pass filtering. Default. */
    custom,
}

internal enum ma_share_mode
{
    shared = 0,
    exclusive
}

internal enum ma_attenuation_model
{
    none,          /* No distance attenuation and no spatialization. */
    inverse,       /* Equivalent to OpenAL's AL_INVERSE_DISTANCE_CLAMPED. */
    linear,        /* Linear attenuation. Equivalent to OpenAL's AL_LINEAR_DISTANCE_CLAMPED. */
    exponential    /* Exponential attenuation. Equivalent to OpenAL's AL_EXPONENT_DISTANCE_CLAMPED. */
}

/* Backend enums must be in priority order. */
internal enum ma_backend
{
    wasapi,
    dsound,
    winmm,
    coreaudio,
    sndio,
    audio4,
    oss,
    pulseaudio,
    alsa,
    jack,
    aaudio,
    opensl,
    webaudio,
    custom,  /* <-- Custom backend, with callbacks defined by the context config. */
    nill     /* <-- Must always be the last item. Lowest priority, and used as the terminator for backend enumeration. */
}

internal enum ma_format
{
    /*
    I like to keep these explicitly defined because they're used as a key into a lookup table. When items are
    added to this, make sure there are no gaps and that they're added to the lookup table in ma_get_bytes_per_sample().
    */
    unknown = 0,     /* Mainly used for indicating an error, but also used as the default for the output format for decoders. */
    u8 = 1,
    s16 = 2,     /* Seems to be the most widely supported format. */
    s24 = 3,     /* Tightly packed. 3 bytes per sample. */
    s32 = 4,
    f32 = 5,
    count
}

internal enum ma_pan_mode
{
    balance = 0,    /* Does not blend one side with the other. Technically just a balance. Compatible with other popular audio engines and therefore the default. */
    pan             /* A true pan. The sound from one side will "move" to the other side and blend with it. */
}

internal enum ma_positioning
{
    absolute,
    relative
}

internal enum ma_handedness
{
    right,
    left
}

internal enum ma_allocation_type
{
    async_notification,
    biquad_coefficient,
    channel,
    context,
    data_source,
    data_source_node,
    data_source_vtable,
    decoder,
    decoding_backend_vtable,
    device,
    device_id,
    device_notification,
    device_descriptor,
    device_info,
    effect_node,
    engine,
    fader,
    fence,
    gainer,
    log,
    lpf1,
    lpf2,
    node,
    node_base,
    node_graph,
    node_input_bus,
    node_output_bus,
    node_vtable,
    panner,
    procedural_data_source,
    resampling_backend_vtable,
    resource_manager,
    resource_manager_data_source,
    sound,
    sound_inlined,
    sound_group,
    spatializer,
    spatializer_listener,
    stack,
    vfs
}

internal enum ma_device_type
{
    playback = 1,
    capture = 2,
    duplex = playback | capture, /* 3 */
    loopback = 4
}

internal enum ma_mono_expansion_mode
{
    duplicate = 0,   /* The default. */
    average,         /* Average the mono channel across all channels. */
    stereo_only,     /* Duplicate to the left and right channels only and ignore the others. */
    standard = duplicate
}

internal enum ma_thread_priority
{
    idle = -5,
    lowest = -4,
    low = -3,
    normal = -2,
    high = -1,
    highest = 0,
    realtime = 1,
    standard = 0
}

internal enum ma_ios_session_category
{
    standard = 0,        /* AVAudioSessionCategoryPlayAndRecord. */
    none,               /* Leave the session category unchanged. */
    ambient,            /* AVAudioSessionCategoryAmbient */
    solo_ambient,       /* AVAudioSessionCategorySoloAmbient */
    playback,           /* AVAudioSessionCategoryPlayback */
    record,             /* AVAudioSessionCategoryRecord */
    play_and_record,    /* AVAudioSessionCategoryPlayAndRecord */
    multi_route         /* AVAudioSessionCategoryMultiRoute */
}

internal enum ma_dither_mode
{
    none = 0,
    rectangle,
    triangle
}

internal enum ma_encoding_format
{
    unknown = 0,
    wav,
    flac,
    mp3,
    vorbis
}

internal enum ma_data_converter_execution_path
{
    passthrough,       /* No conversion. */
    format_only,       /* Only format conversion. */
    channels_only,     /* Only channel conversion. */
    resample_only,     /* Only resampling. */
    resample_first,    /* All conversions, but resample as the first step. */
    channels_first     /* All conversions, but channels as the first step. */
}

internal enum ma_channel_conversion_path
{
    unknown,
    passthrough,
    mono_out,    /* Converting to mono. */
    mono_in,     /* Converting from mono. */
    shuffle,     /* Simple shuffle. Will use this when all channels are present in both input and output channel maps, but just in a different order. */
    weights      /* Blended based on weights. */
}

internal enum ma_device_state
{
    uninitialized = 0,
    stopped = 1,  /* The device's default state after initialization. */
    started = 2,  /* The device is started and is requesting and/or delivering audio data. */
    starting = 3,  /* Transitioning from a stopped state to started. */
    stopping = 4   /* Transitioning from a started state to stopped. */
}

[Flags]
internal enum ma_sound_flags
{
    stream = 0x00000001,   /* MA_RESOURCE_MANAGER_DATA_SOURCE_FLAG_STREAM */
    decode = 0x00000002,   /* MA_RESOURCE_MANAGER_DATA_SOURCE_FLAG_DECODE */
    asynchronous = 0x00000004,   /* MA_RESOURCE_MANAGER_DATA_SOURCE_FLAG_ASYNC */
    wait_init = 0x00000008,   /* MA_RESOURCE_MANAGER_DATA_SOURCE_FLAG_WAIT_INIT */
    unknown_length = 0x00000010,   /* MA_RESOURCE_MANAGER_DATA_SOURCE_FLAG_UNKNOWN_LENGTH */
    looping = 0x00000020,   /* MA_RESOURCE_MANAGER_DATA_SOURCE_FLAG_LOOPING */

    /* ma_sound specific flags. */
    no_default_attachment = 0x00001000,   /* Do not attach to the endpoint by default. Useful for when setting up nodes in a complex graph system. */
    no_pitch = 0x00002000,   /* Disable pitch shifting with ma_sound_set_pitch() and ma_sound_group_set_pitch(). This is an optimization. */
    no_spatialization = 0x00004000    /* Disable spatialization. */
}

internal enum ma_node_state
{
    started = 0,
    stopped = 1
}

[Flags]
internal enum ma_node_flags
{
    passthrough = 0x00000001,
    continuous_processing = 0x00000002,
    allow_null_input = 0x00000004,
    different_processing_rates = 0x00000008,
    silent_output = 0x00000010
}

// ma_pointer_types
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_uint32_ptr
{
    internal IntPtr pointer;
    public ma_uint32_ptr() { }
    internal ma_uint32_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_uint32_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate(Marshal.SizeOf<UInt32>());
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_uint32* Get()
		{
			return (ma_uint32*)pointer;
		}
	}


[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_async_notification_ptr
	{
		internal IntPtr pointer;
		public ma_async_notification_ptr() { }
		internal ma_async_notification_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_async_notification_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.async_notification);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_biquad_coefficient_ptr
	{
		internal IntPtr pointer;
		public ma_biquad_coefficient_ptr() { }
		internal ma_biquad_coefficient_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_biquad_coefficient_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.biquad_coefficient);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_biquad_coefficient* Get()
		{
			return (ma_biquad_coefficient*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_channel_ptr
	{
		internal IntPtr pointer;
		public ma_channel_ptr() { }
		internal ma_channel_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_channel_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.channel);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_context_ptr
	{
		internal IntPtr pointer;
		public ma_context_ptr() { }
		internal ma_context_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_context_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.context);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_data_source_ptr
	{
		internal IntPtr pointer;
		public ma_data_source_ptr() { }
		internal ma_data_source_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_data_source_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.data_source);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_data_source_node_ptr
{
    internal IntPtr pointer;
    public ma_data_source_node_ptr() { }
    internal ma_data_source_node_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_data_source_node_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.data_source_node);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_data_source_node* Get()
    {
        return (ma_data_source_node*)pointer;
    }
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_data_source_vtable_ptr
{
    internal IntPtr pointer;
    public ma_data_source_vtable_ptr() { }
    internal ma_data_source_vtable_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_data_source_vtable_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.data_source_vtable);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_data_source_vtable* Get()
    {
        return (ma_data_source_vtable*)pointer;
    }
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_decoder_ptr
	{
		internal IntPtr pointer;
		public ma_decoder_ptr() { }
		internal ma_decoder_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_decoder_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.decoder);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_decoder* Get()
		{
        return (ma_decoder*)pointer;
		}
	}

//
	internal unsafe struct ma_decoding_backend_vtable_ptr
	{
		internal IntPtr pointer;
		public ma_decoding_backend_vtable_ptr() { }
		internal ma_decoding_backend_vtable_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_decoding_backend_vtable_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.decoding_backend_vtable);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_decoding_backend_vtable* Get()
		{
        return (ma_decoding_backend_vtable*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_device_ptr
	{
		internal IntPtr pointer;
		public ma_device_ptr() { }
		internal ma_device_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_device_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.device);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_device* Get()
		{
			return (ma_device*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_device_id_ptr
	{
		internal IntPtr pointer;
		public ma_device_id_ptr() { }
		internal ma_device_id_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_device_id_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.device_id);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_device_id* Get()
		{
			return (ma_device_id*)pointer;
		}
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device_notification_ptr
{
    internal IntPtr pointer;
    public ma_device_notification_ptr() { }
    internal ma_device_notification_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_device_notification_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.device_notification);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device_descriptor_ptr
{
    internal IntPtr pointer;
    public ma_device_descriptor_ptr() { }
    internal ma_device_descriptor_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_device_descriptor_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.device_descriptor);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_device_descriptor* Get()
    {
        return (ma_device_descriptor*)pointer;
    }
	}

[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_device_info_ptr
	{
		internal IntPtr pointer;
		public ma_device_info_ptr() { }
		internal ma_device_info_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_device_info_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.device_info);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_device_info* Get()
    {
        return (ma_device_info*)pointer;
    }
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_effect_node_ptr
{
    internal IntPtr pointer;
    public ma_effect_node_ptr() { }
    internal ma_effect_node_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_effect_node_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.effect_node);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_effect_node* Get()
    {
        return (ma_effect_node*)pointer;
    }
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_engine_ptr
{
    internal IntPtr pointer;
    public ma_engine_ptr() { }
    internal ma_engine_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_engine_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.engine);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_engine* Get()
    {
        return (ma_engine*)pointer;
    }
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_fader_ptr
{
    internal IntPtr pointer;
    public ma_fader_ptr() { }
    internal ma_fader_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_fader_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.fader);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_fader* Get()
    {
        return (ma_fader*)pointer;
    }
	}

[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_fence_ptr
	{
		internal IntPtr pointer;
		public ma_fence_ptr() { }
		internal ma_fence_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_fence_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.fence);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_gainer_ptr
{
    internal IntPtr pointer;
    public ma_gainer_ptr() { }
    internal ma_gainer_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_gainer_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.gainer);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_gainer* Get()
    {
        return (ma_gainer*)pointer;
    }
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_log_ptr
	{
		internal IntPtr pointer;
		public ma_log_ptr() { }
		internal ma_log_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_log_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.log);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_log* Get()
    {
        return (ma_log*)pointer;
    }
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_lpf1_ptr
	{
		internal IntPtr pointer;
		public ma_lpf1_ptr() { }
		internal ma_lpf1_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_lpf1_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.lpf1);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_lpf1* Get()
		{
			return (ma_lpf1*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_lpf2_ptr
	{
		internal IntPtr pointer;
		public ma_lpf2_ptr() { }
		internal ma_lpf2_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_lpf2_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.lpf2);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_lpf2* Get()
		{
			return (ma_lpf2*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_node_ptr
	{
		internal IntPtr pointer;
		public ma_node_ptr() { }
		internal ma_node_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_node_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.node);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_node_base_ptr
	{
		internal IntPtr pointer;
		public ma_node_base_ptr() { }
		internal ma_node_base_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_node_base_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.node_base);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_node_base* Get()
		{
			return (ma_node_base*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_node_graph_ptr
	{
		internal IntPtr pointer;
		public ma_node_graph_ptr() { }
		internal ma_node_graph_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_node_graph_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.node_graph);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_node_graph* Get()
		{
			return (ma_node_graph*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_node_input_bus_ptr
	{
		internal IntPtr pointer;
		public ma_node_input_bus_ptr() { }
		internal ma_node_input_bus_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_node_input_bus_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.node_input_bus);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_node_input_bus* Get()
		{
			return (ma_node_input_bus*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_node_output_bus_ptr
	{
		internal IntPtr pointer;
		public ma_node_output_bus_ptr() { }
		internal ma_node_output_bus_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_node_output_bus_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.node_output_bus);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_node_output_bus* Get()
		{
			return (ma_node_output_bus*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_node_vtable_ptr
	{
		internal IntPtr pointer;
		public ma_node_vtable_ptr() { }
		internal ma_node_vtable_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_node_vtable_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.node_vtable);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_node_vtable* Get()
		{
			return (ma_node_vtable*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_panner_ptr
	{
		internal IntPtr pointer;
		public ma_panner_ptr() { }
		internal ma_panner_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_panner_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.panner);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_panner* Get()
		{
			return (ma_panner*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_procedural_data_source_ptr
	{
		internal IntPtr pointer;
		public ma_procedural_data_source_ptr() { }
		internal ma_procedural_data_source_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_procedural_data_source_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.procedural_data_source);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_procedural_data_source* Get()
		{
			return (ma_procedural_data_source*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_resampling_backend_vtable_ptr
	{
		internal IntPtr pointer;
		public ma_resampling_backend_vtable_ptr() { }
		internal ma_resampling_backend_vtable_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_resampling_backend_vtable_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.resampling_backend_vtable);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_resource_manager_ptr
	{
		internal IntPtr pointer;
		public ma_resource_manager_ptr() { }
		internal ma_resource_manager_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_resource_manager_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.resource_manager);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_resource_manager_data_source_ptr
	{
		internal IntPtr pointer;
		public ma_resource_manager_data_source_ptr() { }
		internal ma_resource_manager_data_source_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_resource_manager_data_source_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.resource_manager_data_source);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_sound_ptr
{
    internal IntPtr pointer;
    public ma_sound_ptr() { }
    internal ma_sound_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_sound_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.sound);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_sound* Get()
		{
			return (ma_sound*)pointer;
		}
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_sound_inlined_ptr
{
    internal IntPtr pointer;
    public ma_sound_inlined_ptr() { }
    internal ma_sound_inlined_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_sound_inlined_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.sound_inlined);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_sound_inlined* Get()
		{
			return (ma_sound_inlined*)pointer;
		}
	}

[StructLayout(LayoutKind.Sequential)]
// ma_sound_group is an alias for ma_sound
internal unsafe struct ma_sound_group_ptr
{
    internal IntPtr pointer;
    public ma_sound_group_ptr() { }
    internal ma_sound_group_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_sound_group_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.sound_group);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_sound* Get()
    {
        return (ma_sound*)pointer;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_spatializer_ptr
{
    internal IntPtr pointer;
    public ma_spatializer_ptr() { }
    internal ma_spatializer_ptr(IntPtr handle)
    {
        pointer = handle;
    }
    internal ma_spatializer_ptr(bool allocate)
    {
        if (allocate)
            Allocate();
    }
    internal bool Allocate()
    {
        pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.spatializer);
        return pointer != IntPtr.Zero;
    }
    internal void Free()
    {
        if (pointer != IntPtr.Zero)
        {
            MiniAudioNative.ma_deallocate_type(pointer);
            pointer = IntPtr.Zero;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_spatializer* Get()
    {
        return (ma_spatializer*)pointer;
    }
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_spatializer_listener_ptr
	{
		internal IntPtr pointer;
		public ma_spatializer_listener_ptr() { }
		internal ma_spatializer_listener_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_spatializer_listener_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.spatializer_listener);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_spatializer_listener* Get()
		{
			return (ma_spatializer_listener*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_stack_ptr
	{
		internal IntPtr pointer;
		public ma_stack_ptr() { }
		internal ma_stack_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_stack_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.stack);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ma_stack* Get()
		{
			return (ma_stack*)pointer;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct ma_vfs_ptr
	{
		internal IntPtr pointer;
		public ma_vfs_ptr() { }
		internal ma_vfs_ptr(IntPtr handle)
		{
			pointer = handle;
		}
		internal ma_vfs_ptr(bool allocate)
		{
			if (allocate)
				Allocate();
		}
		internal bool Allocate()
		{
			pointer = MiniAudioNative.ma_allocate_type(ma_allocation_type.vfs);
			return pointer != IntPtr.Zero;
		}
		internal void Free()
		{
			if (pointer != IntPtr.Zero)
			{
				MiniAudioNative.ma_deallocate_type(pointer);
				pointer = IntPtr.Zero;
			}
		}
	}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct size_t
{
    private UIntPtr value;

    internal size_t(UIntPtr value)
    {
        this.value = value;
    }

    public static implicit operator size_t(UIntPtr value)
    {
        return new size_t(value);
    }

    public static implicit operator UIntPtr(size_t size)
    {
        return size.value;
    }

    public static implicit operator size_t(int value)
    {
        return new size_t((UIntPtr)(uint)value);
    }

    public static implicit operator size_t(uint value)
    {
        return new size_t((UIntPtr)value);
    }

    public static implicit operator size_t(long value)
    {
        return new size_t((UIntPtr)(ulong)value);
    }

    public static implicit operator size_t(ulong value)
    {
        return new size_t((UIntPtr)value);
    }

    public static size_t operator +(size_t a, size_t b)
    {
        return new size_t((UIntPtr)(a.ToUInt64() + b.ToUInt64()));
    }

    public static size_t operator -(size_t a, size_t b)
    {
        return new size_t((UIntPtr)(a.ToUInt64() - b.ToUInt64()));
    }

    public static size_t operator *(size_t a, size_t b)
    {
        return new size_t((UIntPtr)(a.ToUInt64() * b.ToUInt64()));
    }

    public static size_t operator /(size_t a, size_t b)
    {
        if (b.value == UIntPtr.Zero)
            throw new DivideByZeroException();
        return new size_t((UIntPtr)(a.ToUInt64() / b.ToUInt64()));
    }

    public static bool operator ==(size_t a, size_t b)
    {
        return a.value == b.value;
    }

    public static bool operator !=(size_t a, size_t b)
    {
        return a.value != b.value;
    }

    public static bool operator <(size_t a, size_t b)
    {
        return a.ToUInt64() < b.ToUInt64();
    }

    public static bool operator >(size_t a, size_t b)
    {
        return a.ToUInt64() > b.ToUInt64();
    }

    public static bool operator <=(size_t a, size_t b)
    {
        return a.ToUInt64() <= b.ToUInt64();
    }

    public static bool operator >=(size_t a, size_t b)
    {
        return a.ToUInt64() >= b.ToUInt64();
    }

    public static size_t operator +(size_t a, ulong b)
    {
        return new size_t((UIntPtr)(a.value.ToUInt64() + b));
    }

    public static size_t operator -(size_t a, ulong b)
    {
        return new size_t((UIntPtr)(a.value.ToUInt64() - b));
    }

    public static size_t operator *(size_t a, ulong b)
    {
        return new size_t((UIntPtr)(a.ToUInt64() * b));
    }

    public static size_t operator /(size_t a, ulong b)
    {
        if (b == 0)
            throw new DivideByZeroException();
        return new size_t((UIntPtr)(a.ToUInt64() / b));
    }

    public static size_t operator +(ulong a, size_t b)
    {
        return new size_t((UIntPtr)(a + b.ToUInt64()));
    }

    public static size_t operator -(ulong a, size_t b)
    {
        return new size_t((UIntPtr)(a - b.ToUInt64()));
    }

    public static size_t operator *(ulong a, size_t b)
    {
        return new size_t((UIntPtr)(a * b.ToUInt64()));
    }

    public static size_t operator /(ulong a, size_t b)
    {
        if (b.value == UIntPtr.Zero)
            throw new DivideByZeroException();
        return new size_t((UIntPtr)(a / b.ToUInt64()));
    }

    public static size_t operator +(size_t a, uint b)
    {
        return new size_t((UIntPtr)(a.value.ToUInt64() + b));
    }

    public static size_t operator -(size_t a, uint b)
    {
        return new size_t((UIntPtr)(a.value.ToUInt64() - b));
    }

    public static size_t operator *(size_t a, uint b)
    {
        return new size_t((UIntPtr)(a.ToUInt64() * b));
    }

    public static size_t operator /(size_t a, uint b)
    {
        if (b == 0)
            throw new DivideByZeroException();
        return new size_t((UIntPtr)(a.ToUInt64() / b));
    }

    public static size_t operator +(uint a, size_t b)
    {
        return new size_t((UIntPtr)(a + b.ToUInt64()));
    }

    public static size_t operator -(uint a, size_t b)
    {
        return new size_t((UIntPtr)(a - b.ToUInt64()));
    }

    public static size_t operator *(uint a, size_t b)
    {
        return new size_t((UIntPtr)(a * b.ToUInt64()));
    }

    public static size_t operator /(uint a, size_t b)
    {
        if (b.value == UIntPtr.Zero)
            throw new DivideByZeroException();
        return new size_t((UIntPtr)(a / b.ToUInt64()));
    }

    internal ulong ToUInt64()
    {
        return value.ToUInt64();
    }

    internal uint ToUInt32()
    {
        return value.ToUInt32();
    }

    public override bool Equals(object? obj)
    {
        if (obj is size_t)
        {
            return this == (size_t)obj;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return value.GetHashCode();
    }
}

// ma_structures
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_allocation_callbacks
{
    internal IntPtr pUserData;
    internal IntPtr onMalloc;
    internal IntPtr onRealloc;
    internal IntPtr onFree;

    internal void SetMallocProc(ma_allocation_callbacks_malloc_proc callback)
    {
        onMalloc = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetReallocProc(ma_allocation_callbacks_realloc_proc callback)
    {
        onRealloc = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetFreeProc(ma_allocation_callbacks_free_proc callback)
    {
        onFree = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device_descriptor
{
    internal ma_device_id_ptr pDeviceID;
    internal ma_share_mode shareMode;
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_uint32 sampleRate;
    private fixed ma_channel channelMap[MiniAudioNative.MA_MAX_CHANNELS];
    internal ma_uint32 periodSizeInFrames;
    internal ma_uint32 periodSizeInMilliseconds;
    internal ma_uint32 periodCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_vec3f
{
    internal float x;
    internal float y;
    internal float z;

    internal ma_vec3f(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public override string ToString()
    {
        return "(" + x + ", " +  y + ", " + z + ")";
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_atomic_vec3f
{
    internal ma_vec3f v;
    internal ma_spinlock lck;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_panner_config
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_pan_mode mode;
    internal float pan;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_panner
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_pan_mode mode;
    internal float pan;  /* -1..1 where 0 is no pan, -1 is left side, +1 is right side. Defaults to 0. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_engine_node
{
    internal ma_node_base baseNode;                              /* Must be the first member for compatibility with the ma_node API. */
    internal ma_engine_ptr pEngine;                                 /* A pointer to the engine. Set based on the value from the config. */
    internal ma_uint32 sampleRate;                               /* The sample rate of the input data. For sounds backed by a data source, this will be the data source's sample rate. Otherwise it'll be the engine's sample rate. */
    internal ma_uint32 volumeSmoothTimeInPCMFrames;
    internal ma_mono_expansion_mode monoExpansionMode;
    internal ma_fader fader;
    internal ma_linear_resampler resampler;                      /* For pitch shift. */
    internal ma_spatializer spatializer;
    internal ma_panner panner;
    internal ma_gainer volumeGainer;                             /* This will only be used if volumeSmoothTimeInPCMFrames is > 0. */
    internal float volume;                             /* Defaults to 1. */
    internal float pitch;
    internal float oldPitch;                                     /* For determining whether or not the resampler needs to be updated to reflect the new pitch. The resampler will be updated on the mixing thread. */
    internal float oldDopplerPitch;                              /* For determining whether or not the resampler needs to be updated to take a new doppler pitch into account. */
    internal ma_bool32 isPitchDisabled;            /* When set to true, pitching will be disabled which will allow the resampler to be bypassed to save some computation. */
    internal ma_bool32 isSpatializationDisabled;   /* Set to false by default. When set to false, will not have spatialisation applied. */
    internal ma_uint32 pinnedListenerIndex;        /* The index of the listener this node should always use for spatialization. If set to MA_LISTENER_INDEX_CLOSEST the engine will use the closest listener. */
    /* When setting a fade, it's not done immediately in ma_sound_set_fade(). It's deferred to the audio thread which means we need to store the settings here. */
    internal fade_settings fadeSettings;
    /* Memory management. */
    internal ma_bool8 _ownsHeap;
    internal IntPtr _pHeap;

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct fade_settings
    {
        internal float volumeBeg;
        internal float volumeEnd;
        internal ma_uint64 fadeLengthInFrames;            /* <-- Defaults to (~(ma_uint64)0) which is used to indicate that no fade should be applied. */
        internal ma_uint64 absoluteGlobalTimeInFrames;    /* <-- The time to start the fade. */
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_engine_config
{
    internal ma_resource_manager_ptr pResourceManager;          /* Can be null in which case a resource manager will be created for you. */
    internal ma_context_ptr pContext;
    internal ma_device_ptr pDevice;                             /* If set, the caller is responsible for calling ma_engine_data_callback() in the device's data callback. */
    internal ma_device_id_ptr pPlaybackDeviceID;                /* The ID of the playback device to use with the default listener. */
    internal IntPtr dataCallback;               /* Can be null. Can be used to provide a custom device data callback. */
    internal IntPtr notificationCallback;
    internal ma_log_ptr pLog;                                   /* When set to NULL, will use the context's log. */
    internal ma_uint32 listenerCount;                        /* Must be between 1 and MA_ENGINE_MAX_LISTENERS. */
    internal ma_uint32 channels;                             /* The number of channels to use when mixing and spatializing. When set to 0, will use the native channel count of the device. */
    internal ma_uint32 sampleRate;                           /* The sample rate. When set to 0 will use the native sample rate of the device. */
    internal ma_uint32 periodSizeInFrames;                   /* If set to something other than 0, updates will always be exactly this size. The underlying device may be a different size, but from the perspective of the mixer that won't matter.*/
    internal ma_uint32 periodSizeInMilliseconds;             /* Used if periodSizeInFrames is unset. */
    internal ma_uint32 gainSmoothTimeInFrames;               /* The number of frames to interpolate the gain of spatialized sounds across. If set to 0, will use gainSmoothTimeInMilliseconds. */
    internal ma_uint32 gainSmoothTimeInMilliseconds;         /* When set to 0, gainSmoothTimeInFrames will be used. If both are set to 0, a default value will be used. */
    internal ma_uint32 defaultVolumeSmoothTimeInPCMFrames;   /* Defaults to 0. Controls the default amount of smoothing to apply to volume changes to sounds. High values means more smoothing at the expense of high latency (will take longer to reach the new volume). */
    internal ma_uint32 preMixStackSizeInBytes;               /* A stack is used for internal processing in the node graph. This allows you to configure the size of this stack. Smaller values will reduce the maximum depth of your node graph. You should rarely need to modify this. */
    internal ma_allocation_callbacks allocationCallbacks;
    internal ma_bool32 noAutoStart;                          /* When set to true, requires an explicit call to ma_engine_start(). This is false by default, meaning the engine will be started automatically in ma_engine_init(). */
    internal ma_bool32 noDevice;                             /* When set to true, don't create a default device. ma_engine_read_pcm_frames() can be called manually to read data. */
    internal ma_mono_expansion_mode monoExpansionMode;       /* Controls how the mono channel should be expanded to other channels when spatialization is disabled on a sound. */
    internal ma_vfs_ptr pResourceManagerVFS;                    /* A pointer to a pre-allocated VFS object to use with the resource manager. This is ignored if pResourceManager is not NULL. */
    internal IntPtr onProcess;               /* Fired at the end of each call to ma_engine_read_pcm_frames(). For engine's that manage their own internal device (the default configuration), this will be fired from the audio thread, and you do not need to call ma_engine_read_pcm_frames() manually in order to trigger this. */
    internal IntPtr pProcessUserData;                         /* User data that's passed into onProcess. */

    internal void SetDataProc(ma_device_data_proc callback)
    {
        dataCallback = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetNotificationProc(ma_device_notification_proc callback)
    {
        notificationCallback = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetEngineProcessProc(ma_engine_process_proc callback)
    {
        onProcess = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_engine
{
    internal ma_node_graph nodeGraph;                        /* An engine is a node graph. It should be able to be plugged into any ma_node_graph API (with a cast) which means this must be the first member of this struct. */
    internal ma_resource_manager_ptr pResourceManager;
    internal ma_device_ptr pDevice;                             /* Optionally set via the config, otherwise allocated by the engine in ma_engine_init(). */
    internal ma_log_ptr pLog;
    internal ma_uint32 sampleRate;
    internal ma_uint32 listenerCount;
    internal ma_spatializer_listener_array listeners;
    internal ma_allocation_callbacks allocationCallbacks;
    internal ma_bool8 ownsResourceManager;
    internal ma_bool8 ownsDevice;
    internal ma_spinlock inlinedSoundLock;                   /* For synchronizing access to the inlined sound list. */
    internal ma_sound_inlined_ptr pInlinedSoundHead;            /* The first inlined sound. Inlined sounds are tracked in a linked list. */
    internal UInt32 inlinedSoundCount;      /* The total number of allocated inlined sound objects. Used for debugging. */
    internal ma_uint32 gainSmoothTimeInFrames;               /* The number of frames to interpolate the gain of spatialized sounds across. */
    internal ma_uint32 defaultVolumeSmoothTimeInPCMFrames;
    internal ma_mono_expansion_mode monoExpansionMode;
    internal IntPtr onProcess;
    internal IntPtr pProcessUserData;

    internal void SetProcessProc(ma_engine_process_proc callback)
    {
        onProcess = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ma_spatializer_listener_array
    {
        internal ma_spatializer_listener l0;
        internal ma_spatializer_listener l1;
        internal ma_spatializer_listener l2;
        internal ma_spatializer_listener l3;
        internal ref ma_spatializer_listener this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index >= MiniAudioNative.MA_ENGINE_MAX_LISTENERS)
                {
                    throw new IndexOutOfRangeException("Index must be between 0 and 3.");
                }
                fixed (ma_spatializer_listener* p = &l0)
                {
                    return ref p[index];
                }
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_procedural_data_source_config
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_uint32 sampleRate;
    internal IntPtr callback;
    internal IntPtr pUserData;

    internal void SetCallback(ma_procedural_data_source_proc callback)
    {
        this.callback = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_procedural_data_source
{
    internal ma_data_source_base ds;
    internal ma_procedural_data_source_config config;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_fader_config
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_uint32 sampleRate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_fader
{
    internal ma_fader_config config;
    internal float volumeBeg;            /* If volumeBeg and volumeEnd is equal to 1, no fading happens (ma_fader_process_pcm_frames() will run as a passthrough). */
    internal float volumeEnd;
    internal ma_uint64 lengthInFrames;   /* The total length of the fade. */
    internal ma_int64 cursorInFrames;   /* The current time in frames. Incremented by ma_fader_process_pcm_frames(). Signed because it'll be offset by startOffsetInFrames in set_fade_ex(). */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_log_callback
{
    internal IntPtr onLog;
    internal IntPtr pUserData;
    internal void SetLogCallback(ma_log_callback_proc callback)
    {
        onLog = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_log
{
    internal ma_log_callback_array callbacks;
    internal ma_uint32 callbackCount;
    internal ma_allocation_callbacks allocationCallbacks; /* Need to store these persistently because ma_log_postv() might need to allocate a buffer on the heap. */
    //There is a mutex here but the size depends on platform

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ma_log_callback_array
    {
        internal ma_log_callback cb0;
        internal ma_log_callback cb1;
        internal ma_log_callback cb2;
        internal ma_log_callback cb3;
        internal ref ma_log_callback this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index >= MiniAudioNative.MA_MAX_LOG_CALLBACKS)
                {
                    throw new IndexOutOfRangeException("Index must be between 0 and 3.");
                }
                fixed (ma_log_callback* p = &cb0)
                {
                    return ref p[index];
                }
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_context_config
{
    internal ma_log_ptr pLog;
    internal ma_thread_priority threadPriority;
    internal size_t threadStackSize;
    internal IntPtr pUserData;
    internal ma_allocation_callbacks allocationCallbacks;
    internal dsound_info dsound;
    internal alsa_info alsa;
    internal pulse_info pulse;
    internal coreaudio_info coreaudio;
    internal jack_info jack;
    internal ma_backend_callbacks custom;

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct dsound_info
    {
        internal ma_handle hWnd; /* HWND. Optional window handle to pass into SetCooperativeLevel(). Will default to the foreground window, and if that fails, the desktop window. */
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct alsa_info
    {
        internal ma_bool32 useVerboseDeviceEnumeration;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct pulse_info
    {
        internal IntPtr pApplicationName;
        internal IntPtr pServerName;
        internal ma_bool32 tryAutoSpawn; /* Enables autospawning of the PulseAudio daemon if necessary. */
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct coreaudio_info
    {
        internal ma_ios_session_category sessionCategory;
        internal ma_uint32 sessionCategoryOptions;
        internal ma_bool32 noAudioSessionActivate;   /* iOS only. When set to true, does not perform an explicit [[AVAudioSession sharedInstace] setActive:true] on initialization. */
        internal ma_bool32 noAudioSessionDeactivate; /* iOS only. When set to true, does not perform an explicit [[AVAudioSession sharedInstace] setActive:false] on uninitialization. */
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct jack_info
    {
        internal IntPtr pClientName;
        internal ma_bool32 tryStartServer;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_resource_manager_pipeline_stage_notification
{
    internal ma_async_notification_ptr pNotification;
    internal ma_fence_ptr pFence;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_resource_manager_pipeline_notifications
{
    internal ma_resource_manager_pipeline_stage_notification init;    /* Initialization of the decoder. */
    internal ma_resource_manager_pipeline_stage_notification done;    /* Decoding fully completed. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_backend_callbacks
{
    internal IntPtr onContextInit;
    internal IntPtr onContextUninit;
    internal IntPtr onContextEnumerateDevices;
    internal IntPtr onContextGetDeviceInfo;
    internal IntPtr onDeviceInit;
    internal IntPtr onDeviceUninit;
    internal IntPtr onDeviceStart;
    internal IntPtr onDeviceStop;
    internal IntPtr onDeviceRead;
    internal IntPtr onDeviceWrite;
    internal IntPtr onDeviceDataLoop;
    internal IntPtr onDeviceDataLoopWakeup;
    internal IntPtr onDeviceGetInfo;

    internal void Set(ma_backend_context_init_proc callback)
    {
        onContextInit = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_context_uninit_proc callback)
    {
        onContextUninit = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_context_enumerate_devices_proc callback)
    {
        onContextEnumerateDevices = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_context_get_device_info_proc callback)
    {
        onContextGetDeviceInfo = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_init_proc callback)
    {
        onDeviceInit = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_uninit_proc callback)
    {
        onDeviceUninit = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_start_proc callback)
    {
        onDeviceStart = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_stop_proc callback)
    {
        onDeviceStop = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_read_proc callback)
    {
        onDeviceRead = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_write_proc callback)
    {
        onDeviceWrite = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_dataloop_proc callback)
    {
        onDeviceDataLoop = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_dataloop_wakeup_proc callback)
    {
        onDeviceDataLoopWakeup = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void Set(ma_backend_device_get_info_proc callback)
    {
        onDeviceGetInfo = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_sound_config
{
    internal IntPtr pFilePath;                      /* Set this to load from the resource manager. */
    internal IntPtr pFilePathW;                  /* Set this to load from the resource manager. */
    internal ma_data_source_ptr pDataSource;                /* Set this to load from an existing data source. */
    internal ma_node_ptr pInitialAttachment;                /* If set, the sound will be attached to an input of this node. This can be set to a ma_sound. If set to NULL, the sound will be attached directly to the endpoint unless MA_SOUND_FLAG_NO_DEFAULT_ATTACHMENT is set in `flags`. */
    internal ma_uint32 initialAttachmentInputBusIndex;   /* The index of the input bus of pInitialAttachment to attach the sound to. */
    internal ma_uint32 channelsIn;                       /* Ignored if using a data source as input (the data source's channel count will be used always). Otherwise, setting to 0 will cause the engine's channel count to be used. */
    internal ma_uint32 channelsOut;                      /* Set this to 0 (default) to use the engine's channel count. Set to MA_SOUND_SOURCE_CHANNEL_COUNT to use the data source's channel count (only used if using a data source as input). */
    internal ma_mono_expansion_mode monoExpansionMode;   /* Controls how the mono channel should be expanded to other channels when spatialization is disabled on a sound. */
    internal ma_uint32 flags;                            /* A combination of MA_SOUND_FLAG_* flags. */
    internal ma_uint32 volumeSmoothTimeInPCMFrames;      /* The number of frames to smooth over volume changes. Defaults to 0 in which case no smoothing is used. */
    internal ma_uint64 initialSeekPointInPCMFrames;      /* Initializes the sound such that it's seeked to this location by default. */
    internal ma_uint64 rangeBegInPCMFrames;
    internal ma_uint64 rangeEndInPCMFrames;
    internal ma_uint64 loopPointBegInPCMFrames;
    internal ma_uint64 loopPointEndInPCMFrames;
    internal IntPtr endCallback;
    internal IntPtr pEndCallbackUserData;
    internal ma_resource_manager_pipeline_notifications initNotifications;
    internal ma_fence_ptr pDoneFence;                       /* Deprecated. Use initNotifications instead. Released when the resource manager has finished decoding the entire sound. Not used with streams. */
    internal ma_bool32 isLooping;                        /* Deprecated. Use the MA_SOUND_FLAG_LOOPING flag in `flags` instead. */

    internal void SetEndCallback(ma_sound_end_proc callback)
    {
        endCallback = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_sound
{
    internal ma_engine_node engineNode;          /* Must be the first member for compatibility with the ma_node API. */
    internal ma_data_source_ptr pDataSource;
    internal ma_uint64 seekTarget; /* The PCM frame index to seek to in the mixing thread. Set to (~(ma_uint64)0) to not perform any seeking. */
    internal ma_bool32 atEnd;
    internal IntPtr endCallback;
    internal IntPtr pEndCallbackUserData;
    internal ma_bool8 ownsDataSource;

    /*
    We're declaring a resource manager data source object here to save us a malloc when loading a
    sound via the resource manager, which I *think* will be the most common scenario.
    */
    internal ma_resource_manager_data_source_ptr* pResourceManagerDataSource;

    internal void SetEndCallback(ma_sound_end_proc callback)
    {
        endCallback = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_sound_inlined
{
    internal ma_sound sound;
    internal ma_sound_inlined_ptr pNext;
    internal ma_sound_inlined_ptr pPrev;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_sound_group_config
{
    internal IntPtr pFilePath;                      /* Set this to load from the resource manager. */
    internal IntPtr pFilePathW;                  /* Set this to load from the resource manager. */
    internal ma_data_source_ptr pDataSource;                /* Set this to load from an existing data source. */
    internal ma_node_ptr pInitialAttachment;                /* If set, the sound will be attached to an input of this node. This can be set to a ma_sound. If set to NULL, the sound will be attached directly to the endpoint unless MA_SOUND_FLAG_NO_DEFAULT_ATTACHMENT is set in `flags`. */
    internal ma_uint32 initialAttachmentInputBusIndex;   /* The index of the input bus of pInitialAttachment to attach the sound to. */
    internal ma_uint32 channelsIn;                       /* Ignored if using a data source as input (the data source's channel count will be used always). Otherwise, setting to 0 will cause the engine's channel count to be used. */
    internal ma_uint32 channelsOut;                      /* Set this to 0 (default) to use the engine's channel count. Set to MA_SOUND_SOURCE_CHANNEL_COUNT to use the data source's channel count (only used if using a data source as input). */
    internal ma_mono_expansion_mode monoExpansionMode;   /* Controls how the mono channel should be expanded to other channels when spatialization is disabled on a sound. */
    internal ma_uint32 flags;                            /* A combination of MA_SOUND_FLAG_* flags. */
    internal ma_uint32 volumeSmoothTimeInPCMFrames;      /* The number of frames to smooth over volume changes. Defaults to 0 in which case no smoothing is used. */
    internal ma_uint64 initialSeekPointInPCMFrames;      /* Initializes the sound such that it's seeked to this location by default. */
    internal ma_uint64 rangeBegInPCMFrames;
    internal ma_uint64 rangeEndInPCMFrames;
    internal ma_uint64 loopPointBegInPCMFrames;
    internal ma_uint64 loopPointEndInPCMFrames;
    internal IntPtr endCallback;
    internal IntPtr pEndCallbackUserData;
    internal ma_resource_manager_pipeline_notifications initNotifications;
    internal ma_fence_ptr pDoneFence;                       /* Deprecated. Use initNotifications instead. Released when the resource manager has finished decoding the entire sound. Not used with streams. */
    internal ma_bool32 isLooping;                        /* Deprecated. Use the MA_SOUND_FLAG_LOOPING flag in `flags` instead. */

    internal void SetEndCallback(ma_sound_end_proc callback)
    {
        endCallback = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_native_data_format
{
    internal ma_uint32 format; // Assuming ma_format is a uint. Adjust as necessary.
    internal ma_uint32 channels; // If set to 0, all channels are supported.
    internal ma_uint32 sampleRate; // If set to 0, all sample rates are supported.
    internal ma_uint32 flags; // A combination of MA_DATA_FORMAT_FLAG_* flags.
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device_info
{
    /* Basic info. This is the only information guaranteed to be filled in during device enumeration. */
    internal ma_device_id id;
    internal fixed byte name[MiniAudioNative.MA_MAX_DEVICE_NAME_LENGTH + 1];
    internal ma_bool32 isDefault;
    internal ma_uint32 nativeDataFormatCount;
    internal ma_native_data_format_array nativeDataFormats;

    internal string GetName()
    {
        unsafe
        {
            fixed (byte* pName = name)
            {
                // Find length up to null terminator
                int len = 0;
                while (len < MiniAudioNative.MA_MAX_DEVICE_NAME_LENGTH && pName[len] != 0) len++;

                if (len == 0) return string.Empty;

                // Assume UTF-8 encoding for device names
                return System.Text.Encoding.UTF8.GetString(pName, len);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ma_native_data_format_array
    {
        internal ma_native_data_format ndf0;
        internal ma_native_data_format ndf1;
        internal ma_native_data_format ndf2;
        internal ma_native_data_format ndf3;
        internal ma_native_data_format ndf4;
        internal ma_native_data_format ndf5;
        internal ma_native_data_format ndf6;
        internal ma_native_data_format ndf7;
        internal ma_native_data_format ndf8;
        internal ma_native_data_format ndf9;
        internal ma_native_data_format ndf10;
        internal ma_native_data_format ndf11;
        internal ma_native_data_format ndf12;
        internal ma_native_data_format ndf13;
        internal ma_native_data_format ndf14;
        internal ma_native_data_format ndf15;
        internal ma_native_data_format ndf16;
        internal ma_native_data_format ndf17;
        internal ma_native_data_format ndf18;
        internal ma_native_data_format ndf19;
        internal ma_native_data_format ndf20;
        internal ma_native_data_format ndf21;
        internal ma_native_data_format ndf22;
        internal ma_native_data_format ndf23;
        internal ma_native_data_format ndf24;
        internal ma_native_data_format ndf25;
        internal ma_native_data_format ndf26;
        internal ma_native_data_format ndf27;
        internal ma_native_data_format ndf28;
        internal ma_native_data_format ndf29;
        internal ma_native_data_format ndf30;
        internal ma_native_data_format ndf31;
        internal ma_native_data_format ndf32;
        internal ma_native_data_format ndf33;
        internal ma_native_data_format ndf34;
        internal ma_native_data_format ndf35;
        internal ma_native_data_format ndf36;
        internal ma_native_data_format ndf37;
        internal ma_native_data_format ndf38;
        internal ma_native_data_format ndf39;
        internal ma_native_data_format ndf40;
        internal ma_native_data_format ndf41;
        internal ma_native_data_format ndf42;
        internal ma_native_data_format ndf43;
        internal ma_native_data_format ndf44;
        internal ma_native_data_format ndf45;
        internal ma_native_data_format ndf46;
        internal ma_native_data_format ndf47;
        internal ma_native_data_format ndf48;
        internal ma_native_data_format ndf49;
        internal ma_native_data_format ndf50;
        internal ma_native_data_format ndf51;
        internal ma_native_data_format ndf52;
        internal ma_native_data_format ndf53;
        internal ma_native_data_format ndf54;
        internal ma_native_data_format ndf55;
        internal ma_native_data_format ndf56;
        internal ma_native_data_format ndf57;
        internal ma_native_data_format ndf58;
        internal ma_native_data_format ndf59;
        internal ma_native_data_format ndf60;
        internal ma_native_data_format ndf61;
        internal ma_native_data_format ndf62;
        internal ma_native_data_format ndf63;
        internal ref ma_native_data_format this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index >= 64)
                {
                    throw new IndexOutOfRangeException("Index must be between 0 and 63.");
                }
                fixed (ma_native_data_format* p = &ndf0)
                {
                    return ref p[index];
                }
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device_info_ex
{
    internal ma_device_info deviceInfo;
    internal ma_device_id_ptr pDeviceId;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_resampler_config
{
    internal ma_format format;   /* Must be either ma_format_f32 or ma_format_s16. */
    internal ma_uint32 channels;
    internal ma_uint32 sampleRateIn;
    internal ma_uint32 sampleRateOut;
    internal ma_resample_algorithm algorithm;    /* When set to ma_resample_algorithm_custom, pBackendVTable will be used. */
    internal ma_resampling_backend_vtable_ptr pBackendVTable;
    internal IntPtr pBackendUserData;
    internal linear_info linear;
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct linear_info
    {
        internal ma_uint32 lpfOrder;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device_config
{
    internal ma_device_type deviceType;
    internal ma_uint32 sampleRate;
    internal ma_uint32 periodSizeInFrames;
    internal ma_uint32 periodSizeInMilliseconds;
    internal ma_uint32 periods;
    internal ma_performance_profile performanceProfile;
    internal ma_bool8 noPreSilencedOutputBuffer; /* When set to true, the contents of the output buffer passed into the data callback will be left undefined rather than initialized to silence. */
    internal ma_bool8 noClip;                    /* When set to true, the contents of the output buffer passed into the data callback will not be clipped after returning. Only applies when the playback sample format is f32. */
    internal ma_bool8 noDisableDenormals;        /* Do not disable denormals when firing the data callback. */
    internal ma_bool8 noFixedSizedCallback;      /* Disables strict fixed-sized data callbacks. Setting this to true will result in the period size being treated only as a hint to the backend. This is an optimization for those who don't need fixed sized callbacks. */
    internal IntPtr dataCallback;
    internal IntPtr notificationCallback;
    internal IntPtr stopCallback;
    internal IntPtr pUserData;
    internal ma_resampler_config resampling;
    internal playback_info playback;
    internal capture_info capture;
    internal wasapi_info wasapi;
    internal alsa_info alsa;
    internal pulse_info pulse;
    internal coreaudio_info coreaudio;
    internal opensl_info opensl;
    internal aaudio_info aaudio;

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct playback_info
    {
        internal ma_device_id_ptr pDeviceID;
        internal ma_format format;
        internal ma_uint32 channels;
        internal ma_channel_ptr pChannelMap;
        internal ma_channel_mix_mode channelMixMode;
        internal ma_bool32 calculateLFEFromSpatialChannels;  /* When an output LFE channel is present, but no input LFE, set to true to set the output LFE to the average of all spatial channels (LR, FR, etc.). Ignored when an input LFE is present. */
        internal ma_share_mode shareMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct capture_info
    {
        internal ma_device_id_ptr pDeviceID;
        internal ma_format format;
        internal ma_uint32 channels;
        internal ma_channel_ptr pChannelMap;
        internal ma_channel_mix_mode channelMixMode;
        internal ma_bool32 calculateLFEFromSpatialChannels;  /* When an output LFE channel is present, but no input LFE, set to true to set the output LFE to the average of all spatial channels (LR, FR, etc.). Ignored when an input LFE is present. */
        internal ma_share_mode shareMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct wasapi_info
    {
        internal ma_wasapi_usage usage;              /* When configured, uses Avrt APIs to set the thread characteristics. */
        internal ma_bool8 noAutoConvertSRC;          /* When set to true, disables the use of AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM. */
        internal ma_bool8 noDefaultQualitySRC;       /* When set to true, disables the use of AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY. */
        internal ma_bool8 noAutoStreamRouting;       /* Disables automatic stream routing. */
        internal ma_bool8 noHardwareOffloading;      /* Disables WASAPI's hardware offloading feature. */
        internal ma_uint32 loopbackProcessID;        /* The process ID to include or exclude for loopback mode. Set to 0 to capture audio from all processes. Ignored when an explicit device ID is specified. */
        internal ma_bool8 loopbackProcessExclude;    /* When set to true, excludes the process specified by loopbackProcessID. By default, the process will be included. */
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct alsa_info
    {
        internal ma_bool32 noMMap;           /* Disables MMap mode. */
        internal ma_bool32 noAutoFormat;     /* Opens the ALSA device with SND_PCM_NO_AUTO_FORMAT. */
        internal ma_bool32 noAutoChannels;   /* Opens the ALSA device with SND_PCM_NO_AUTO_CHANNELS. */
        internal ma_bool32 noAutoResample;   /* Opens the ALSA device with SND_PCM_NO_AUTO_RESAMPLE. */
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct pulse_info
    {
        internal IntPtr pStreamNamePlayback;
        internal IntPtr pStreamNameCapture;
        internal int channelMap;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct coreaudio_info
    {
        internal ma_bool32 allowNominalSampleRateChange; /* Desktop only. When enabled, allows changing of the sample rate at the operating system level. */
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct opensl_info
    {
        internal ma_opensl_stream_type streamType;
        internal ma_opensl_recording_preset recordingPreset;
        internal ma_bool32 enableCompatibilityWorkarounds;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct aaudio_info
    {
        internal ma_aaudio_usage usage;
        internal ma_aaudio_content_type contentType;
        internal ma_aaudio_input_preset inputPreset;
        internal ma_aaudio_allowed_capture_policy allowedCapturePolicy;
        internal ma_bool32 noAutoStartAfterReroute;
        internal ma_bool32 enableCompatibilityWorkarounds;
        internal ma_bool32 allowSetBufferCapacity;
    }

    internal void SetDataCallback(ma_device_data_proc dataCallback)
    {
        this.dataCallback = MarshalHelper.GetFunctionPointerForDelegate(dataCallback);
    }

    internal void SetNotificationCallback(ma_device_notification_proc notificationCallback)
    {
        this.notificationCallback = MarshalHelper.GetFunctionPointerForDelegate(notificationCallback);
    }

    internal void SetStopCallback(ma_stop_proc stopCallback)
    {
        this.stopCallback = MarshalHelper.GetFunctionPointerForDelegate(stopCallback);
    }
}

[StructLayout(LayoutKind.Explicit, Size = 256)] // largest member size determines union size
internal unsafe struct ma_device_id
{
    [FieldOffset(0)]
    internal fixed ma_uint16 wasapi[64];
    [FieldOffset(0)]
    internal fixed byte dsound[16];
    [FieldOffset(0)]
    internal ma_uint32 winmm;
    [FieldOffset(0)]
    internal fixed byte alsa[256];
    [FieldOffset(0)]
    internal fixed byte pulse[256];
    [FieldOffset(0)]
    internal int jack;
    [FieldOffset(0)]
    internal fixed byte coreaudio[256];
    [FieldOffset(0)]
    internal fixed byte sndio[256];
    [FieldOffset(0)]
    internal fixed byte audio4[256];
    [FieldOffset(0)]
    internal fixed byte oss[64];
    [FieldOffset(0)]
    internal int aaudio;
    [FieldOffset(0)]
    internal uint opensl;
    [FieldOffset(0)]
    internal fixed byte webaudio[32];
    [FieldOffset(0)]
    internal int custom_i;
    [FieldOffset(0)]
    internal fixed byte custom_s[256];
    [FieldOffset(0)]
    internal IntPtr custom_p;
    [FieldOffset(0)]
    internal int nullbackend;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_device
{
    internal ma_context_ptr pContext;
    internal ma_device_type type;
    internal ma_uint32 sampleRate;
    internal ma_device_state state;                      /* The state of the device is variable and can change at any time on any thread. Must be used atomically. */
    internal IntPtr onData;                 /* Set once at initialization time and should not be changed after. */
    internal IntPtr onNotification; /* Set once at initialization time and should not be changed after. */
    internal IntPtr onStop;                        /* DEPRECATED. Use the notification callback instead. Set once at initialization time and should not be changed after. */
    internal IntPtr pUserData;                            /* Application defined data. */
    //There are a lot more fields down here but they are not needed as long other ma_types only use a pointer to ma_device

    internal void SetDataProc(ma_device_data_proc onData)
    {
        this.onData = MarshalHelper.GetFunctionPointerForDelegate(onData);
    }

    internal void SetNotificationProc(ma_device_notification_proc onNotification)
    {
        this.onNotification = MarshalHelper.GetFunctionPointerForDelegate(onNotification);
    }

    internal void SetStopProc(ma_stop_proc onStop)
    {
        this.onStop = MarshalHelper.GetFunctionPointerForDelegate(onStop);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_decoder_config
{
    internal ma_format format;      /* Set to 0 or ma_format_unknown to use the stream's internal format. */
    internal ma_uint32 channels;    /* Set to 0 to use the stream's internal channels. */
    internal ma_uint32 sampleRate;  /* Set to 0 to use the stream's internal sample rate. */
    internal ma_channel_ptr pChannelMap;
    internal ma_channel_mix_mode channelMixMode;
    internal ma_dither_mode ditherMode;
    internal ma_resampler_config resampling;
    internal ma_allocation_callbacks allocationCallbacks;
    internal ma_encoding_format encodingFormat;
    internal ma_uint32 seekPointCount;   /* When set to > 0, specifies the number of seek points to use for the generation of a seek table. Not all decoding backends support this. */
    internal IntPtr ppCustomBackendVTables;
    internal ma_uint32 customBackendCount;
    internal IntPtr pCustomBackendUserData;

    /// <summary>
    /// Sets the ppCustomBackendVTables and customBackendCount fields. The caller is responsible for cleaning up memory by calling FreeCustomBackendVTables().
    /// </summary>
    /// <param name="customDecodingBackends"></param>
    internal void SetCustomBackendVTables(ma_decoding_backend_vtable_ptr[] customDecodingBackends)
    {
        int count = 0;

        for (int i = 0; i < customDecodingBackends.Length; i++)
        {
            if (customDecodingBackends[i].pointer != IntPtr.Zero)
                count++;
        }

        IntPtr vtableMemory = IntPtr.Zero;

        if (count > 0)
        {
            vtableMemory = Marshal.AllocHGlobal(sizeof(IntPtr) * count);

            ma_decoding_backend_vtable** pCustomBackendVTables = (ma_decoding_backend_vtable**)vtableMemory;

            int index = 0;

            for (int i = 0; i < customDecodingBackends.Length; i++)
            {
                if (customDecodingBackends[i].pointer != IntPtr.Zero)
                    pCustomBackendVTables[index++] = (ma_decoding_backend_vtable*)customDecodingBackends[i].pointer;
            }

        }

        ppCustomBackendVTables = vtableMemory;
        customBackendCount = (UInt32)count;
    }

    internal void FreeCustomBackendVTables()
    {
        if (ppCustomBackendVTables != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ppCustomBackendVTables);
            ppCustomBackendVTables = IntPtr.Zero;
            customBackendCount = 0;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_decoder
{
    internal ma_data_source_base ds;
    internal ma_data_source_ptr pBackend;                   /* The decoding backend we'll be pulling data from. */
    internal IntPtr pBackendVTable; /* The vtable for the decoding backend. This needs to be stored so we can access the onUninit() callback. */
    internal IntPtr pBackendUserData;
    internal IntPtr onRead;
    internal IntPtr onSeek;
    internal IntPtr onTell;
    internal IntPtr pUserData;
    internal ma_uint64 readPointerInPCMFrames;      /* In output sample rate. Used for keeping track of how many frames are available for decoding. */
    internal ma_format outputFormat;
    internal ma_uint32 outputChannels;
    internal ma_uint32 outputSampleRate;
    internal ma_data_converter converter;    /* Data conversion is achieved by running frames through this. */
    internal IntPtr pInputCache;              /* In input format. Can be null if it's not needed. */
    internal ma_uint64 inputCacheCap;        /* The capacity of the input cache. */
    internal ma_uint64 inputCacheConsumed;   /* The number of frames that have been consumed in the cache. Used for determining the next valid frame. */
    internal ma_uint64 inputCacheRemaining;  /* The number of valid frames remaining in the cache. */
    internal ma_allocation_callbacks allocationCallbacks;
    internal ma_decoder_data_union data;

    internal void SetReadProc(ma_decoder_read_proc callback)
    {
        onRead = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
    internal void SetSeekProc(ma_decoder_seek_proc callback)
    {
        onSeek = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
    internal void SetTellProc(ma_decoder_tell_proc callback)
    {
        onTell = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ma_decoder_data_vfs
    {
        internal ma_vfs_ptr pVFS;
        internal ma_vfs_file file;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ma_decoder_data_memory
    {
        internal IntPtr pData; // const ma_uint8*
        internal size_t dataSize;
        internal size_t currentReadPos;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct ma_decoder_data_union
    {
        [FieldOffset(0)]
        internal ma_decoder_data_vfs vfs;

        [FieldOffset(0)]
        internal ma_decoder_data_memory memory;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_data_source_vtable
{
    internal IntPtr onRead;
    internal IntPtr onSeek;
    internal IntPtr onGetDataFormat;
    internal IntPtr onGetCursor;
    internal IntPtr onGetLength;
    internal IntPtr onSetLooping;
    internal ma_uint32 flags;

    internal void SetReadProc(ma_data_source_vtable_read_proc callback)
    {
        onRead = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetSeekProc(ma_data_source_vtable_seek_proc callback)
    {
        onSeek = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetGetDataFormatProc(ma_data_source_vtable_get_data_format_proc callback)
    {
        onGetDataFormat = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetGetCursorProc(ma_data_source_vtable_get_cursor_proc callback)
    {
        onGetCursor = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetGetLengthProc(ma_data_source_vtable_get_length_proc callback)
    {
        onGetLength = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetSetLoopingProc(ma_data_source_vtable_set_looping_proc callback)
    {
        onSetLooping = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_data_source_config
{
    internal ma_data_source_vtable_ptr vtable;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_data_source_node_config
{
    internal ma_node_config nodeConfig;
    internal ma_data_source_ptr pDataSource;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_data_source_node
{
    internal ma_node_base baseNode;
    internal ma_data_source_ptr pDataSource;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_data_source_base
{
    internal IntPtr vtable;
    internal ma_uint64 rangeBegInFrames;
    internal ma_uint64 rangeEndInFrames;             /* Set to -1 for unranged (default). */
    internal ma_uint64 loopBegInFrames;              /* Relative to rangeBegInFrames. */
    internal ma_uint64 loopEndInFrames;              /* Relative to rangeBegInFrames. Set to -1 for the end of the range. */
    internal ma_data_source_ptr pCurrent;               /* When non-NULL, the data source being initialized will act as a proxy and will route all operations to pCurrent. Used in conjunction with pNext/onGetNext for seamless chaining. */
    internal ma_data_source_ptr pNext;                  /* When set to NULL, onGetNext will be used. */
    internal IntPtr onGetNext; /* Will be used when pNext is NULL. If both are NULL, no next will be used. */
    internal ma_bool32 isLooping;

    internal void SetNextProc(ma_data_source_get_next_proc callback)
    {
        onGetNext = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_channel_converter
{
    internal ma_format format;
    internal ma_uint32 channelsIn;
    internal ma_uint32 channelsOut;
    internal ma_channel_mix_mode mixingMode;
    internal ma_channel_conversion_path conversionPath;
    internal ma_channel_ptr pChannelMapIn;
    internal ma_channel_ptr pChannelMapOut;
    internal IntPtr pShuffleTable;    /* Indexed by output channel index. */
    internal ma_channel_converter_weights weights;  /* [in][out] */
    /* Memory management. */
    internal IntPtr _pHeap;
    internal ma_bool32 _ownsHeap;

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct ma_channel_converter_weights
    {
        [FieldOffset(0)]
        internal IntPtr f32;  // float**
        [FieldOffset(0)]
        internal IntPtr s16;  // ma_int32**
    }
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct ma_biquad_coefficient
{
    [FieldOffset(0)]
    internal float f32;
    [FieldOffset(0)]
    internal ma_int32 s32;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_biquad_config
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal double b0;
    internal double b1;
    internal double b2;
    internal double a0;
    internal double a1;
    internal double a2;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_biquad
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_biquad_coefficient b0;
    internal ma_biquad_coefficient b1;
    internal ma_biquad_coefficient b2;
    internal ma_biquad_coefficient a1;
    internal ma_biquad_coefficient a2;
    internal ma_biquad_coefficient_ptr pR1;
    internal ma_biquad_coefficient_ptr pR2;
    /* Memory management. */
    internal IntPtr _pHeap;
    internal ma_bool32 _ownsHeap;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_lpf1_config
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_uint32 sampleRate;
    internal double cutoffFrequency;
    internal double q;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_lpf2_config
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_uint32 sampleRate;
    internal double cutoffFrequency;
    internal double q;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_lpf1
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_biquad_coefficient a;
    internal ma_biquad_coefficient_ptr pR1;
    /* Memory management. */
    internal IntPtr _pHeap;
    internal ma_bool32 _ownsHeap;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_lpf2
{
    internal ma_biquad bq;   /* The second order low-pass filter is implemented as a biquad filter. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_lpf
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_uint32 sampleRate;
    internal ma_uint32 lpf1Count;
    internal ma_uint32 lpf2Count;
    internal ma_lpf1_ptr pLPF1;
    internal ma_lpf2_ptr pLPF2;
    /* Memory management. */
    internal IntPtr _pHeap;
    internal ma_bool32 _ownsHeap;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_linear_resampler_config
{
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_uint32 sampleRateIn;
    internal ma_uint32 sampleRateOut;
    internal ma_uint32 lpfOrder;         /* The low-pass filter order. Setting this to 0 will disable low-pass filtering. */
    internal double    lpfNyquistFactor; /* 0..1. Defaults to 1. 1 = Half the sampling frequency (Nyquist Frequency), 0.5 = Quarter the sampling frequency (half Nyquest Frequency), etc. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_linear_resampler
{
    internal ma_linear_resampler_config config;
    internal ma_uint32 inAdvanceInt;
    internal ma_uint32 inAdvanceFrac;
    internal ma_uint32 inTimeInt;
    internal ma_uint32 inTimeFrac;
    internal ma_linear_resampler_data x0; /* The previous input frame. */
    internal ma_linear_resampler_data x1; /* The next input frame. */
    internal ma_lpf lpf;
    /* Memory management. */
    internal IntPtr _pHeap;
    internal ma_bool32 _ownsHeap;

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct ma_linear_resampler_data
    {
        [FieldOffset(0)]
        internal IntPtr f32;
        [FieldOffset(0)]
        internal IntPtr s16;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_resampler
{
    internal IntPtr pBackend;
    internal IntPtr pBackendVTable;
    internal IntPtr pBackendUserData;
    internal ma_format format;
    internal ma_uint32 channels;
    internal ma_uint32 sampleRateIn;
    internal ma_uint32 sampleRateOut;
    internal ma_resampler_state state;    /* State for stock resamplers so we can avoid a malloc. For stock resamplers, pBackend will point here. */
    /* Memory management. */
    internal IntPtr _pHeap;
    internal ma_bool32 _ownsHeap;

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct ma_resampler_state
    {
        [FieldOffset(0)]
        internal ma_linear_resampler linear;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_data_converter
{
    internal ma_format formatIn;
    internal ma_format formatOut;
    internal ma_uint32 channelsIn;
    internal ma_uint32 channelsOut;
    internal ma_uint32 sampleRateIn;
    internal ma_uint32 sampleRateOut;
    internal ma_dither_mode ditherMode;
    internal ma_data_converter_execution_path executionPath; /* The execution path the data converter will follow when processing. */
    internal ma_channel_converter channelConverter;
    internal ma_resampler resampler;
    internal ma_bool8 hasPreFormatConversion;
    internal ma_bool8 hasPostFormatConversion;
    internal ma_bool8 hasChannelConverter;
    internal ma_bool8 hasResampler;
    internal ma_bool8 isPassthrough;
    /* Memory management. */
    internal ma_bool8 _ownsHeap;
    internal IntPtr _pHeap;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_resource_manager_config
{
    internal ma_allocation_callbacks allocationCallbacks;
    internal ma_log_ptr pLog;
    internal ma_format decodedFormat;        /* The decoded format to use. Set to ma_format_unknown (default) to use the file's native format. */
    internal ma_uint32 decodedChannels;      /* The decoded channel count to use. Set to 0 (default) to use the file's native channel count. */
    internal ma_uint32 decodedSampleRate;    /* the decoded sample rate to use. Set to 0 (default) to use the file's native sample rate. */
    internal ma_uint32 jobThreadCount;       /* Set to 0 if you want to self-manage your job threads. Defaults to 1. */
    internal size_t jobThreadStackSize;
    internal ma_uint32 jobQueueCapacity;     /* The maximum number of jobs that can fit in the queue at a time. Defaults to MA_JOB_TYPE_RESOURCE_MANAGER_QUEUE_CAPACITY. Cannot be zero. */
    internal ma_uint32 flags;
    internal ma_vfs_ptr pVFS;                   /* Can be NULL in which case defaults will be used. */
    internal IntPtr ppCustomDecodingBackendVTables;
    internal ma_uint32 customDecodingBackendCount;
    internal IntPtr pCustomDecodingBackendUserData;

    /// <summary>
    /// Sets the ppCustomDecodingBackendVTables and customDecodingBackendCount fields. The caller is responsible for cleaning up memory by calling FreeCustomDecodingBackendVTables().
    /// </summary>
    /// <param name="customDecodingBackends"></param>
    internal void SetCustomDecodingBackendVTables(ma_decoding_backend_vtable_ptr[] customDecodingBackends)
    {
        int count = 0;

        for (int i = 0; i < customDecodingBackends.Length; i++)
        {
            if (customDecodingBackends[i].pointer != IntPtr.Zero)
                count++;
        }

        IntPtr vtableMemory = IntPtr.Zero;

        if (count > 0)
        {
            vtableMemory = Marshal.AllocHGlobal(sizeof(IntPtr) * count);

            ma_decoding_backend_vtable** pCustomBackendVTables = (ma_decoding_backend_vtable**)vtableMemory;

            int index = 0;

            for (int i = 0; i < customDecodingBackends.Length; i++)
            {
                if (customDecodingBackends[i].pointer != IntPtr.Zero)
                    pCustomBackendVTables[index++] = (ma_decoding_backend_vtable*)customDecodingBackends[i].pointer;
            }

        }

        ppCustomDecodingBackendVTables = vtableMemory;
        customDecodingBackendCount = (UInt32)count;
    }

    internal void FreeCustomDecodingBackendVTables()
    {
        if (ppCustomDecodingBackendVTables != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ppCustomDecodingBackendVTables);
            ppCustomDecodingBackendVTables = IntPtr.Zero;
            customDecodingBackendCount = 0;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_decoding_backend_vtable
{
    internal IntPtr onInit;
    internal IntPtr onInitFile;
    internal IntPtr onInitFileW;
    internal IntPtr onInitMemory;
    internal IntPtr onUninit;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_stack
{
    internal size_t offset;
    internal size_t sizeInBytes;
    internal fixed byte _data[1];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_node_config
{
    internal ma_node_vtable_ptr vtable;          /* Should never be null. Initialization of the node will fail if so. */
    internal ma_node_state initialState;         /* Defaults to ma_node_state_started. */
    internal ma_uint32 inputBusCount;            /* Only used if the vtable specifies an input bus count of `MA_NODE_BUS_COUNT_UNKNOWN`, otherwise must be set to `MA_NODE_BUS_COUNT_UNKNOWN` (default). */
    internal ma_uint32 outputBusCount;           /* Only used if the vtable specifies an output bus count of `MA_NODE_BUS_COUNT_UNKNOWN`, otherwise  be set to `MA_NODE_BUS_COUNT_UNKNOWN` (default). */
    internal IntPtr pInputChannels;          /* The number of elements are determined by the input bus count as determined by the vtable, or `inputBusCount` if the vtable specifies `MA_NODE_BUS_COUNT_UNKNOWN`. */
    internal IntPtr pOutputChannels;         /* The number of elements are determined by the output bus count as determined by the vtable, or `outputBusCount` if the vtable specifies `MA_NODE_BUS_COUNT_UNKNOWN`. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_node_vtable
{
    /*
    Extended processing callback. This callback is used for effects that process input and output
    at different rates (i.e. they perform resampling). This is similar to the simple version, only
    they take two separate frame counts: one for input, and one for output.

    On input, `pFrameCountOut` is equal to the capacity of the output buffer for each bus, whereas
    `pFrameCountIn` will be equal to the number of PCM frames in each of the buffers in `ppFramesIn`.

    On output, set `pFrameCountOut` to the number of PCM frames that were actually output and set
    `pFrameCountIn` to the number of input frames that were consumed.
    */
    internal IntPtr onProcess;

    /*
    A callback for retrieving the number of input frames that are required to output the
    specified number of output frames. You would only want to implement this when the node performs
    resampling. This is optional, even for nodes that perform resampling, but it does offer a
    small reduction in latency as it allows miniaudio to calculate the exact number of input frames
    to read at a time instead of having to estimate.
    */
    internal IntPtr onGetRequiredInputFrameCount;

    /*
    The number of input buses. This is how many sub-buffers will be contained in the `ppFramesIn`
    parameters of the callbacks above.
    */
    internal ma_uint8 inputBusCount;

    /*
    The number of output buses. This is how many sub-buffers will be contained in the `ppFramesOut`
    parameters of the callbacks above.
    */
    internal ma_uint8 outputBusCount;

    /*
    Flags describing characteristics of the node. This is currently just a placeholder for some
    ideas for later on.
    */
    internal ma_node_flags flags;

    internal void SetOnProcess(ma_node_vtable_process_proc callback)
    {
        onProcess = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }

    internal void SetOnGetRequiredInputFrameCount(ma_node_vtable_get_required_input_frame_count_proc callback)
    {
        onGetRequiredInputFrameCount = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_node_output_bus
{
    /* Immutable. */
    internal ma_node_ptr pNode;                                         /* The node that owns this output bus. The input node. Will be null for dummy head and tail nodes. */
    internal ma_uint8 outputBusIndex;                                /* The index of the output bus on pNode that this output bus represents. */
    internal ma_uint8 channels;                                      /* The number of channels in the audio stream for this bus. */

    /* Mutable via multiple threads. Must be used atomically. The weird ordering here is for packing reasons. */
    internal ma_uint8 inputNodeInputBusIndex;                        /* The index of the input bus on the input. Required for detaching. Will only be used within the spinlock so does not need to be atomic. */
    internal ma_uint32 flags;                          /* Some state flags for tracking the read state of the output buffer. A combination of MA_NODE_OUTPUT_BUS_FLAG_*. */
    internal ma_uint32 refCount;                       /* Reference count for some thread-safety when detaching. */
    internal ma_bool32 isAttached;                     /* This is used to prevent iteration of nodes that are in the middle of being detached. Used for thread safety. */
    internal ma_spinlock lck;                         /* Unfortunate lock, but significantly simplifies the implementation. Required for thread-safe attaching and detaching. */
    internal float volume;                             /* Linear. */
    internal ma_node_output_bus_ptr pNext;    /* If null, it's the tail node or detached. */
    internal ma_node_output_bus_ptr pPrev;    /* If null, it's the head node or detached. */
    internal ma_node_ptr pInputNode;          /* The node that this output bus is attached to. Required for detaching. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_node_input_bus
{
    /* Mutable via multiple threads. */
    internal ma_node_output_bus head;                /* Dummy head node for simplifying some lock-free thread-safety stuff. */
    internal ma_uint32 nextCounter;    /* This is used to determine whether or not the input bus is finding the next node in the list. Used for thread safety when detaching output buses. */
    internal ma_spinlock lck;         /* Unfortunate lock, but significantly simplifies the implementation. Required for thread-safe attaching and detaching. */
    /* Set once at startup. */
    internal ma_uint8 channels;                      /* The number of channels in the audio stream for this bus. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_node_base
{
    /* These variables are set once at startup. */
    internal ma_node_graph_ptr pNodeGraph;                  /* The graph this node belongs to. */
    internal ma_node_vtable_ptr vtable;
    internal ma_uint32 inputBusCount;
    internal ma_uint32 outputBusCount;
    internal ma_node_input_bus_ptr pInputBuses;
    internal ma_node_output_bus_ptr pOutputBuses;
    internal IntPtr pCachedData;                         /* Allocated on the heap. Fixed size. Needs to be stored on the heap because reading from output buses is done in separate function calls. */
    internal ma_uint16 cachedDataCapInFramesPerBus;      /* The capacity of the input data cache in frames, per bus. */

    /* These variables are read and written only from the audio thread. */
    internal ma_uint16 cachedFrameCountOut;
    internal ma_uint16 cachedFrameCountIn;
    internal ma_uint16 consumedFrameCountIn;

    /* These variables are read and written between different threads. */
    internal ma_node_state state;          /* When set to stopped, nothing will be read, regardless of the times in stateTimes. */
    internal fixed ma_uint64 stateTimes[2];      /* Indexed by ma_node_state. Specifies the time based on the global clock that a node should be considered to be in the relevant state. */
    internal ma_uint64 localTime;          /* The node's local clock. This is just a running sum of the number of output frames that have been processed. Can be modified by any thread with `ma_node_set_time()`. */

    /* Memory management. */
    internal ma_node_input_bus_array _inputBuses;
    internal ma_node_output_bus_array _outputBuses;
    internal IntPtr _pHeap;   /* A heap allocation for internal use only. pInputBuses and/or pOutputBuses will point to this if the bus count exceeds MA_MAX_NODE_LOCAL_BUS_COUNT. */
    internal ma_bool32 _ownsHeap;    /* If set to true, the node owns the heap allocation and _pHeap will be freed in ma_node_uninit(). */

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ma_node_input_bus_array
    {
        internal ma_node_input_bus b0;
        internal ma_node_input_bus b1;
        internal ref ma_node_input_bus this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index >= MiniAudioNative.MA_MAX_NODE_LOCAL_BUS_COUNT)
                {
                    throw new IndexOutOfRangeException("Index must be between 0 and 1.");
                }
                fixed (ma_node_input_bus* p = &b0)
                {
                    return ref p[index];
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ma_node_output_bus_array
    {
        internal ma_node_output_bus b0;
        internal ma_node_output_bus b1;
        internal ref ma_node_output_bus this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index >= MiniAudioNative.MA_MAX_NODE_LOCAL_BUS_COUNT)
                {
                    throw new IndexOutOfRangeException("Index must be between 0 and 1.");
                }
                fixed (ma_node_output_bus* p = &b0)
                {
                    return ref p[index];
                }
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_node_graph_config
{
    internal ma_uint32 channels;
    internal ma_uint32 processingSizeInFrames;   /* This is the preferred processing size for node processing callbacks unless overridden by a node itself. Can be 0 in which case it will be based on the frame count passed into ma_node_graph_read_pcm_frames(), but will not be well defined. */
    internal size_t preMixStackSizeInBytes;      /* Defaults to 512KB per channel. Reducing this will save memory, but the depth of your node graph will be more restricted. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_node_graph
{
    /* Immutable. */
    internal ma_node_base baseNode;                  /* The node graph itself is a node so it can be connected as an input to different node graph. This has zero inputs and calls ma_node_graph_read_pcm_frames() to generate it's output. */
    internal ma_node_base endpoint;              /* Special node that all nodes eventually connect to. Data is read from this node in ma_node_graph_read_pcm_frames(). */
    internal IntPtr pProcessingCache;            /* This will be allocated when processingSizeInFrames is non-zero. This is needed because ma_node_graph_read_pcm_frames() can be called with a variable number of frames, and we may need to do some buffering in situations where the caller requests a frame count that's not a multiple of processingSizeInFrames. */
    internal ma_uint32 processingCacheFramesRemaining;
    internal ma_uint32 processingSizeInFrames;
    /* Read and written by multiple threads. */
    internal ma_bool32 isReading;
    /* Modified only by the audio thread. */
    internal ma_stack_ptr pPreMixStack;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_effect_node_config
{
    internal ma_uint32 sampleRate;
    internal ma_uint32 channels;
    internal IntPtr onProcess;
    internal IntPtr pUserData;

    internal void SetOnProcess(ma_effect_node_process_proc callback)
    {
        onProcess = MarshalHelper.GetFunctionPointerForDelegate(callback);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct ma_effect_node
{
    internal ma_node_base baseNode;
    internal ma_effect_node_config config;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_gainer_config
{
    internal ma_uint32 channels;
    internal ma_uint32 smoothTimeInFrames;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_gainer
{
    internal ma_gainer_config config;
    internal ma_uint32 t;
    internal float masterVolume;
    internal IntPtr pOldGains;
    internal IntPtr pNewGains;
    /* Memory management. */
    internal IntPtr _pHeap;
    internal ma_bool32 _ownsHeap;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_spatializer_config
{
    internal ma_uint32 channelsIn;
    internal ma_uint32 channelsOut;
    internal ma_channel_ptr pChannelMapIn;
    internal ma_attenuation_model attenuationModel;
    internal ma_positioning positioning;
    internal ma_handedness handedness;           /* Defaults to right. Forward is -1 on the Z axis. In a left handed system, forward is +1 on the Z axis. */
    internal float minGain;
    internal float maxGain;
    internal float minDistance;
    internal float maxDistance;
    internal float rolloff;
    internal float coneInnerAngleInRadians;
    internal float coneOuterAngleInRadians;
    internal float coneOuterGain;
    internal float dopplerFactor;                /* Set to 0 to disable doppler effect. */
    internal float directionalAttenuationFactor; /* Set to 0 to disable directional attenuation. */
    internal float minSpatializationChannelGain; /* The minimal scaling factor to apply to channel gains when accounting for the direction of the sound relative to the listener. Must be in the range of 0..1. Smaller values means more aggressive directional panning, larger values means more subtle directional panning. */
    internal ma_uint32 gainSmoothTimeInFrames;   /* When the gain of a channel changes during spatialization, the transition will be linearly interpolated over this number of frames. */
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_spatializer
{
    internal ma_uint32 channelsIn;
    internal ma_uint32 channelsOut;
    internal ma_channel_ptr pChannelMapIn;
    internal ma_attenuation_model attenuationModel;
    internal ma_positioning positioning;
    internal ma_handedness handedness;           /* Defaults to right. Forward is -1 on the Z axis. In a left handed system, forward is +1 on the Z axis. */
    internal float minGain;
    internal float maxGain;
    internal float minDistance;
    internal float maxDistance;
    internal float rolloff;
    internal float coneInnerAngleInRadians;
    internal float coneOuterAngleInRadians;
    internal float coneOuterGain;
    internal float dopplerFactor;                /* Set to 0 to disable doppler effect. */
    internal float directionalAttenuationFactor; /* Set to 0 to disable directional attenuation. */
    internal ma_uint32 gainSmoothTimeInFrames;   /* When the gain of a channel changes during spatialization, the transition will be linearly interpolated over this number of frames. */
    internal ma_atomic_vec3f position;
    internal ma_atomic_vec3f direction;
    internal ma_atomic_vec3f velocity;  /* For doppler effect. */
    internal float dopplerPitch; /* Will be updated by ma_spatializer_process_pcm_frames() and can be used by higher level functions to apply a pitch shift for doppler effect. */
    internal float minSpatializationChannelGain;
    internal ma_gainer gainer;   /* For smooth gain transitions. */
    internal IntPtr pNewChannelGainsOut; /* An offset of _pHeap. Used by ma_spatializer_process_pcm_frames() to store new channel gains. The number of elements in this array is equal to config.channelsOut. */
    /* Memory management. */
    internal IntPtr _pHeap;
    internal ma_bool32 _ownsHeap;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_spatializer_listener_config
{
    internal ma_uint32 channelsOut;
    internal ma_channel_ptr pChannelMapOut;
    internal ma_handedness handedness;   /* Defaults to right. Forward is -1 on the Z axis. In a left handed system, forward is +1 on the Z axis. */
    internal float coneInnerAngleInRadians;
    internal float coneOuterAngleInRadians;
    internal float coneOuterGain;
    internal float speedOfSound;
    internal ma_vec3f worldUp;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ma_spatializer_listener
{
    internal ma_spatializer_listener_config config;
    internal ma_atomic_vec3f position;  /* The absolute position of the listener. */
    internal ma_atomic_vec3f direction; /* The direction the listener is facing. The world up vector is config.worldUp. */
    internal ma_atomic_vec3f velocity;
    internal ma_bool32 isEnabled;
    /* Memory management. */
    internal ma_bool32 _ownsHeap;
    internal IntPtr _pHeap;
}

internal static partial class MiniAudioNative
{
    internal const int MA_MAX_CHANNELS = 254;
    internal const int MA_MAX_DEVICE_NAME_LENGTH = 255;
    internal const int MA_MAX_LOG_CALLBACKS = 4;
    internal const int MA_ENGINE_MAX_LISTENERS = 4;
    internal const int MA_MAX_NODE_LOCAL_BUS_COUNT = 2;
    internal const int MA_MAX_NODE_BUS_COUNT = 254;
    internal const int MA_NODE_BUS_COUNT_UNKNOWN = 255;

    private const string LIB_MINIAUDIO_EX = "miniaudioex";

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_allocate_type")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr ma_allocate_type(ma_allocation_type type);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_allocate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr ma_allocate(size_t size);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_deallocate_type")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_deallocate_type(IntPtr pData);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_get_size_of_type")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial size_t ma_get_size_of_type(ma_allocation_type type);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_engine_config ma_engine_config_init();

    // ma_device
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_device_config ma_device_config_init(ma_device_type deviceType);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_device_init(ma_context_ptr pContext, ref ma_device_config pConfig, ma_device_ptr pDevice);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_device_uninit(ma_device_ptr pDevice);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_get_context")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_context_ptr ma_device_get_context(ma_device_ptr pDevice);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_device_start(ma_device_ptr pDevice);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_device_stop(ma_device_ptr pDevice);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_is_started")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_device_is_started(ma_device_ptr pDevice);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_get_state")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_device_state ma_device_get_state(ma_device_ptr pDevice);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_set_master_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_device_set_master_volume(ma_device_ptr pDevice, float volume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_get_master_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_device_get_master_volume(ma_device_ptr pDevice, out float pVolume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_set_master_volume_db")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_device_set_master_volume_db(ma_device_ptr pDevice, float gainDB);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_device_get_master_volume_db")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_device_get_master_volume_db(ma_device_ptr pDevice, out float pGainDB);

    // ma_context
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_context_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_context_config ma_context_config_init();

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe ma_result ma_context_init(ma_backend* backends, ma_uint32 backendCount, ma_context_config* pConfig, ma_context_ptr pContext);

    internal static unsafe ma_result ma_context_init(ma_backend[] backends, ref ma_context_config config, ma_context_ptr pContext)
    {
        fixed (ma_context_config* pConfig = &config)
        {
            if (backends?.Length > 0)
            {
                fixed (ma_backend* pBackends = &backends[0])
                {
                    return ma_context_init(pBackends, (UInt32)backends.Length, pConfig, pContext);
                }
            }
            else
            {
                return ma_context_init(null, 0, pConfig, pContext);
            }
        }
    }

    internal static unsafe ma_result ma_context_init(ma_backend[] backends, ma_context_ptr pContext)
    {
        if (backends?.Length > 0)
        {
            fixed (ma_backend* pBackends = &backends[0])
            {
                return ma_context_init(pBackends, (UInt32)backends.Length, null, pContext);
            }
        }
        else
        {
            return ma_context_init(null, 0, null, pContext);
        }
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_context_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_context_uninit(ma_context_ptr pContext);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_context_sizeof")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial size_t ma_context_sizeof();

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_context_get_log")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_log_ptr ma_context_get_log(ma_context_ptr pContext);

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern ma_result ma_context_enumerate_devices(ma_context_ptr pContext, IntPtr callback, IntPtr pUserData);

    internal static ma_result ma_context_enumerate_devices(ma_context_ptr pContext, ma_enum_devices_callback_proc callback, IntPtr pUserData)
    {
        return ma_context_enumerate_devices(pContext, MarshalHelper.GetFunctionPointerForDelegate(callback), pUserData);
    }

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe ma_result ma_context_get_devices(ma_context_ptr pContext, ma_device_info** ppPlaybackDeviceInfos, ma_uint32* pPlaybackDeviceCount, ma_device_info** ppCaptureDeviceInfos, ma_uint32* pCaptureDeviceCount);

    internal static unsafe ma_result ma_context_get_devices(ma_context_ptr pContext, out ma_device_info_ex[] ppPlaybackDeviceInfos, out ma_device_info_ex[] ppCaptureDeviceInfos)
    {
        ppPlaybackDeviceInfos = null;
        ppCaptureDeviceInfos = null;
        ma_uint32 captureCount = 0;
        ma_uint32 playbackCount = 0;
        ma_device_info* pPlayback = null;
        ma_device_info* pCapture = null;

        ma_result result = ma_context_get_devices(pContext, &pPlayback, &playbackCount, &pCapture, &captureCount);

        if (result != ma_result.success)
            return result;

        if (pPlayback != null && playbackCount > 0)
        {
            ppPlaybackDeviceInfos = new ma_device_info_ex[playbackCount];

            for (int i = 0; i < playbackCount; i++)
            {
                ppPlaybackDeviceInfos[i].deviceInfo = pPlayback[i];
                ppPlaybackDeviceInfos[i].pDeviceId = new ma_device_id_ptr(new IntPtr(&pPlayback[i]));
            }
        }

        if (pCapture != null && captureCount > 0)
        {
            ppCaptureDeviceInfos = new ma_device_info_ex[captureCount];

            for (int i = 0; i < captureCount; i++)
            {
                ppCaptureDeviceInfos[i].deviceInfo = pCapture[i];
                ppCaptureDeviceInfos[i].pDeviceId = new ma_device_id_ptr(new IntPtr(&pCapture[i]));
            }
        }

        return result;
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_context_get_device_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_context_get_device_info(ma_context_ptr pContext, ma_device_type deviceType, ma_device_id_ptr pDeviceID, out ma_device_info pDeviceInfo);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_context_is_loopback_supported")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_context_is_loopback_supported(ma_context_ptr pContext);

    // ma_engine
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_engine_uninit(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_init(ref ma_engine_config pConfig, ma_engine_ptr pEngine);

    internal static ma_result ma_engine_init(ma_engine_ptr pEngine)
    {
        ma_engine_config config = ma_engine_config_init();
        return ma_engine_init(ref config, pEngine);
    }

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe ma_result ma_engine_read_pcm_frames(ma_engine_ptr pEngine, IntPtr pFramesOut, ma_uint64 frameCount, ma_uint64* pFramesRead);

    internal static unsafe ma_result ma_engine_read_pcm_frames(ma_engine_ptr pEngine, IntPtr pFramesOut, ma_uint64 frameCount, ref ma_uint64 framesRead)
    {
        fixed (ma_uint64* pFramesRead = &framesRead)
        {
            return ma_engine_read_pcm_frames(pEngine, pFramesOut, frameCount, pFramesRead);
        }
    }

    internal static unsafe ma_result ma_engine_read_pcm_frames(ma_engine_ptr pEngine, IntPtr pFramesOut, ma_uint64 frameCount)
    {
        return ma_engine_read_pcm_frames(pEngine, pFramesOut, frameCount, null);
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_node_graph")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_graph_ptr ma_engine_get_node_graph(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_resource_manager")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_resource_manager_ptr ma_engine_get_resource_manager(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_device")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_device_ptr ma_engine_get_device(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_log")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_log_ptr ma_engine_get_log(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_endpoint")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_ptr ma_engine_get_endpoint(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_time_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint64 ma_engine_get_time_in_pcm_frames(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_time_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint64 ma_engine_get_time_in_milliseconds(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_set_time_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_set_time_in_pcm_frames(ma_engine_ptr pEngine, ma_uint64 globalTime);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_set_time_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_set_time_in_milliseconds(ma_engine_ptr pEngine, ma_uint64 globalTime);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_channels")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_engine_get_channels(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_sample_rate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_engine_get_sample_rate(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_start(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_stop(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_set_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_set_volume(ma_engine_ptr pEngine, float volume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_engine_get_volume(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_set_gain_db")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_set_gain_db(ma_engine_ptr pEngine, float gainDB);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_gain_db")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_engine_get_gain_db(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_get_listener_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_engine_get_listener_count(ma_engine_ptr pEngine);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_find_closest_listener")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_engine_find_closest_listener(ma_engine_ptr pEngine, float absolutePosX, float absolutePosY, float absolutePosZ);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_set_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_engine_listener_set_position(ma_engine_ptr pEngine, ma_uint32 listenerIndex, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_get_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_engine_listener_get_position(ma_engine_ptr pEngine, ma_uint32 listenerIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_set_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_engine_listener_set_direction(ma_engine_ptr pEngine, ma_uint32 listenerIndex, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_get_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_engine_listener_get_direction(ma_engine_ptr pEngine, ma_uint32 listenerIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_set_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_engine_listener_set_velocity(ma_engine_ptr pEngine, ma_uint32 listenerIndex, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_get_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_engine_listener_get_velocity(ma_engine_ptr pEngine, ma_uint32 listenerIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_set_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_engine_listener_set_cone(ma_engine_ptr pEngine, ma_uint32 listenerIndex, float innerAngleInRadians, float outerAngleInRadians, float outerGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_get_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_engine_listener_get_cone(ma_engine_ptr pEngine, ma_uint32 listenerIndex, out float pInnerAngleInRadians, out float pOuterAngleInRadians, out float pOuterGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_set_world_up")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_engine_listener_set_world_up(ma_engine_ptr pEngine, ma_uint32 listenerIndex, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_get_world_up")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_engine_listener_get_world_up(ma_engine_ptr pEngine, ma_uint32 listenerIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_set_enabled")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_engine_listener_set_enabled(ma_engine_ptr pEngine, ma_uint32 listenerIndex, ma_bool32 isEnabled);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_listener_is_enabled")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_engine_listener_is_enabled(ma_engine_ptr pEngine, ma_uint32 listenerIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_play_sound_ex", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_play_sound_ex(ma_engine_ptr pEngine, string pFilePath, ma_node_ptr pNode, ma_uint32 nodeInputBusIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_engine_play_sound", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_engine_play_sound(ma_engine_ptr pEngine, string pFilePath, ma_sound_group_ptr pGroup);   /* Fire and forget. */

    // ma_sound
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_init_from_file", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_init_from_file(ma_engine_ptr pEngine, string pFilePath, ma_sound_flags flags, ma_sound_group_ptr pGroup, ma_fence_ptr pDoneFence, ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_init_from_file_w", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_init_from_file_w(ma_engine_ptr pEngine, string pFilePath, ma_sound_flags flags, ma_sound_group_ptr pGroup, ma_fence_ptr pDoneFence, ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_init_from_memory")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_init_from_memory(ma_engine_ptr pEngine, IntPtr pData, ma_uint64 dataSize, ma_sound_flags flags, ma_sound_group_ptr pGroup, ma_fence_ptr pDoneFence, ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_init_from_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_init_from_callback(ma_engine_ptr pEngine, ref ma_procedural_data_source_config pConfig, ma_sound_flags flags, ma_sound_group_ptr pGroup, ma_fence_ptr pDoneFence, ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_init_copy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_init_copy(ma_engine_ptr pEngine, ma_sound_ptr pExistingSound, ma_sound_flags flags, ma_sound_group_ptr pGroup, ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_init_from_data_source")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_init_from_data_source(ma_engine_ptr pEngine, ma_data_source_ptr pDataSource, ma_sound_flags flags, ma_sound_group_ptr pGroup, ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_init_ex")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_init_ex(ma_engine_ptr pEngine, ref ma_sound_config pConfig, ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_uninit(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_engine")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_engine_ptr ma_sound_get_engine(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_data_source")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_data_source_ptr ma_sound_get_data_source(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_start(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_stop(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_stop_with_fade_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_stop_with_fade_in_pcm_frames(ma_sound_ptr pSound, ma_uint64 fadeLengthInFrames);     /* Will overwrite any scheduled stop and fade. */

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_stop_with_fade_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_stop_with_fade_in_milliseconds(ma_sound_ptr pSound, ma_uint64 fadeLengthInFrames);   /* Will overwrite any scheduled stop and fade. */

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_volume(ma_sound_ptr pSound, float volume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_volume(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_pan")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_pan(ma_sound_ptr pSound, float pan);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_pan")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_pan(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_pan_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_pan_mode(ma_sound_ptr pSound, ma_pan_mode panMode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_pan_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_pan_mode ma_sound_get_pan_mode(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_pitch")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_pitch(ma_sound_ptr pSound, float pitch);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_pitch")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_pitch(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_spatialization_enabled")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_spatialization_enabled(ma_sound_ptr pSound, ma_bool32 enabled);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_is_spatialization_enabled")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_sound_is_spatialization_enabled(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_pinned_listener_index")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_pinned_listener_index(ma_sound_ptr pSound, ma_uint32 listenerIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_pinned_listener_index")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_sound_get_pinned_listener_index(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_listener_index")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_sound_get_listener_index(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_direction_to_listener")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_sound_get_direction_to_listener(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_position(ma_sound_ptr pSound, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_sound_get_position(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_direction(ma_sound_ptr pSound, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_sound_get_direction(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_velocity(ma_sound_ptr pSound, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_sound_get_velocity(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_attenuation_model")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_attenuation_model(ma_sound_ptr pSound, ma_attenuation_model attenuationModel);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_attenuation_model")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_attenuation_model ma_sound_get_attenuation_model(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_positioning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_positioning(ma_sound_ptr pSound, ma_positioning positioning);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_positioning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_positioning ma_sound_get_positioning(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_rolloff")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_rolloff(ma_sound_ptr pSound, float rolloff);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_rolloff")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_rolloff(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_min_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_min_gain(ma_sound_ptr pSound, float minGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_min_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_min_gain(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_max_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_max_gain(ma_sound_ptr pSound, float maxGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_max_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_max_gain(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_min_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_min_distance(ma_sound_ptr pSound, float minDistance);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_min_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_min_distance(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_max_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_max_distance(ma_sound_ptr pSound, float maxDistance);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_max_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_max_distance(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_cone(ma_sound_ptr pSound, float innerAngleInRadians, float outerAngleInRadians, float outerGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_get_cone(ma_sound_ptr pSound, out float pInnerAngleInRadians, out float pOuterAngleInRadians, out float pOuterGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_doppler_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_doppler_factor(ma_sound_ptr pSound, float dopplerFactor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_doppler_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_doppler_factor(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_directional_attenuation_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_directional_attenuation_factor(ma_sound_ptr pSound, float directionalAttenuationFactor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_directional_attenuation_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_directional_attenuation_factor(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_fade_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_fade_in_pcm_frames(ma_sound_ptr pSound, float volumeBeg, float volumeEnd, ma_uint64 fadeLengthInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_fade_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_fade_in_milliseconds(ma_sound_ptr pSound, float volumeBeg, float volumeEnd, ma_uint64 fadeLengthInMilliseconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_fade_start_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_fade_start_in_pcm_frames(ma_sound_ptr pSound, float volumeBeg, float volumeEnd, ma_uint64 fadeLengthInFrames, ma_uint64 absoluteGlobalTimeInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_fade_start_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_fade_start_in_milliseconds(ma_sound_ptr pSound, float volumeBeg, float volumeEnd, ma_uint64 fadeLengthInMilliseconds, ma_uint64 absoluteGlobalTimeInMilliseconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_current_fade_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_get_current_fade_volume(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_start_time_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_start_time_in_pcm_frames(ma_sound_ptr pSound, ma_uint64 absoluteGlobalTimeInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_start_time_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_start_time_in_milliseconds(ma_sound_ptr pSound, ma_uint64 absoluteGlobalTimeInMilliseconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_stop_time_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_stop_time_in_pcm_frames(ma_sound_ptr pSound, ma_uint64 absoluteGlobalTimeInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_stop_time_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_stop_time_in_milliseconds(ma_sound_ptr pSound, ma_uint64 absoluteGlobalTimeInMilliseconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_stop_time_with_fade_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_stop_time_with_fade_in_pcm_frames(ma_sound_ptr pSound, ma_uint64 stopAbsoluteGlobalTimeInFrames, ma_uint64 fadeLengthInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_stop_time_with_fade_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_stop_time_with_fade_in_milliseconds(ma_sound_ptr pSound, ma_uint64 stopAbsoluteGlobalTimeInMilliseconds, ma_uint64 fadeLengthInMilliseconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_is_playing")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_sound_is_playing(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_time_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint64 ma_sound_get_time_in_pcm_frames(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_time_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint64 ma_sound_get_time_in_milliseconds(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_looping")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_set_looping(ma_sound_ptr pSound, ma_bool32 isLooping);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_is_looping")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_sound_is_looping(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_at_end")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_sound_at_end(ma_sound_ptr pSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_seek_to_pcm_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_seek_to_pcm_frame(ma_sound_ptr pSound, ma_uint64 frameIndex); /* Just a wrapper around ma_data_source_seek_to_pcm_frame(). */

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_seek_to_second")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_seek_to_second(ma_sound_ptr pSound, float seekPointInSeconds); /* Abstraction to ma_sound_seek_to_pcm_frame() */

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_data_format")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_get_data_format(ma_sound_ptr pSound, out ma_format pFormat, out ma_uint32 pChannels, out ma_uint32 pSampleRate, Byte pChannelMap, size_t channelMapCap);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_cursor_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_get_cursor_in_pcm_frames(ma_sound_ptr pSound, out ma_uint64 pCursor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_length_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_get_length_in_pcm_frames(ma_sound_ptr pSound, out ma_uint64 pLength);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_cursor_in_seconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_get_cursor_in_seconds(ma_sound_ptr pSound, out float pCursor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_get_length_in_seconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_get_length_in_seconds(ma_sound_ptr pSound, out float pLength);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_set_end_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_set_end_callback(ma_sound_ptr pSound, IntPtr callback, IntPtr pUserData);

    internal static ma_result ma_sound_set_end_callback(ma_sound_ptr pSound, ma_sound_end_proc callback, IntPtr pUserData)
    {
        return ma_sound_set_end_callback(pSound, MarshalHelper.GetFunctionPointerForDelegate(callback), pUserData);
    }

    // ma_sound_group
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_group_init(ma_engine_ptr pEngine, ma_sound_flags flags, ma_sound_group_ptr pParentGroup, ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_init_ex")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_group_init_ex(ma_engine_ptr pEngine, ref ma_sound_group_config pConfig, ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_uninit(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_engine")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_engine_ptr ma_sound_group_get_engine(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_group_start(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_sound_group_stop(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_volume(ma_sound_group_ptr pGroup, float volume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_volume(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_pan")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_pan(ma_sound_group_ptr pGroup, float pan);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_pan")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_pan(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_pan_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_pan_mode(ma_sound_group_ptr pGroup, ma_pan_mode panMode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_pan_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_pan_mode ma_sound_group_get_pan_mode(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_pitch")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_pitch(ma_sound_group_ptr pGroup, float pitch);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_pitch")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_pitch(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_spatialization_enabled")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_spatialization_enabled(ma_sound_group_ptr pGroup, ma_bool32 enabled);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_is_spatialization_enabled")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_sound_group_is_spatialization_enabled(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_pinned_listener_index")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_pinned_listener_index(ma_sound_group_ptr pGroup, ma_uint32 listenerIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_pinned_listener_index")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_sound_group_get_pinned_listener_index(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_listener_index")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_sound_group_get_listener_index(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_direction_to_listener")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_sound_group_get_direction_to_listener(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_position(ma_sound_group_ptr pGroup, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_sound_group_get_position(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_direction(ma_sound_group_ptr pGroup, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_sound_group_get_direction(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_velocity(ma_sound_group_ptr pGroup, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_sound_group_get_velocity(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_attenuation_model")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_attenuation_model(ma_sound_group_ptr pGroup, ma_attenuation_model attenuationModel);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_attenuation_model")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_attenuation_model ma_sound_group_get_attenuation_model(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_positioning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_positioning(ma_sound_group_ptr pGroup, ma_positioning positioning);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_positioning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_positioning ma_sound_group_get_positioning(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_rolloff")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_rolloff(ma_sound_group_ptr pGroup, float rolloff);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_rolloff")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_rolloff(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_min_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_min_gain(ma_sound_group_ptr pGroup, float minGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_min_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_min_gain(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_max_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_max_gain(ma_sound_group_ptr pGroup, float maxGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_max_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_max_gain(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_min_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_min_distance(ma_sound_group_ptr pGroup, float minDistance);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_min_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_min_distance(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_max_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_max_distance(ma_sound_group_ptr pGroup, float maxDistance);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_max_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_max_distance(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_cone(ma_sound_group_ptr pGroup, float innerAngleInRadians, float outerAngleInRadians, float outerGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_get_cone(ma_sound_group_ptr pGroup, out float pInnerAngleInRadians, out float pOuterAngleInRadians, out float pOuterGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_doppler_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_doppler_factor(ma_sound_group_ptr pGroup, float dopplerFactor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_doppler_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_doppler_factor(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_directional_attenuation_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_directional_attenuation_factor(ma_sound_group_ptr pGroup, float directionalAttenuationFactor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_directional_attenuation_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_directional_attenuation_factor(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_fade_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_fade_in_pcm_frames(ma_sound_group_ptr pGroup, float volumeBeg, float volumeEnd, ma_uint64 fadeLengthInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_fade_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_fade_in_milliseconds(ma_sound_group_ptr pGroup, float volumeBeg, float volumeEnd, ma_uint64 fadeLengthInMilliseconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_current_fade_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_sound_group_get_current_fade_volume(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_start_time_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_start_time_in_pcm_frames(ma_sound_group_ptr pGroup, ma_uint64 absoluteGlobalTimeInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_start_time_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_start_time_in_milliseconds(ma_sound_group_ptr pGroup, ma_uint64 absoluteGlobalTimeInMilliseconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_stop_time_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_stop_time_in_pcm_frames(ma_sound_group_ptr pGroup, ma_uint64 absoluteGlobalTimeInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_set_stop_time_in_milliseconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_sound_group_set_stop_time_in_milliseconds(ma_sound_group_ptr pGroup, ma_uint64 absoluteGlobalTimeInMilliseconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_is_playing")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_sound_group_is_playing(ma_sound_group_ptr pGroup);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_sound_group_get_time_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint64 ma_sound_group_get_time_in_pcm_frames(ma_sound_group_ptr pGroup);

    // ma_procedural_data_source
    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern ma_procedural_data_source_config ma_procedural_data_source_config_init(ma_format format, ma_uint32 channels, ma_uint32 sampleRate, IntPtr pProceduralSoundProc, IntPtr pUserData);

    internal static ma_procedural_data_source_config ma_procedural_data_source_config_init(ma_format format, ma_uint32 channels, ma_uint32 sampleRate, ma_procedural_data_source_proc pProceduralSoundProc, IntPtr pUserData)
    {
        return ma_procedural_data_source_config_init(format, channels, sampleRate, MarshalHelper.GetFunctionPointerForDelegate(pProceduralSoundProc), pUserData);
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_procedural_data_source_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_procedural_data_source_init(ref ma_procedural_data_source_config pConfig, ma_procedural_data_source_ptr pProceduralSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_procedural_data_source_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_procedural_data_source_uninit(ma_procedural_data_source_ptr pProceduralSound);

    // ma_spatializer_listener
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_spatializer_listener_config ma_spatializer_listener_config_init(ma_uint32 channelsOut);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_get_heap_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_listener_get_heap_size(ref ma_spatializer_listener_config pConfig, out size_t pHeapSizeInBytes);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_init_preallocated")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_listener_init_preallocated(ref ma_spatializer_listener_config pConfig, IntPtr pHeap, ma_spatializer_listener_ptr pListener);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_listener_init(ref ma_spatializer_listener_config pConfig, IntPtr pAllocationCallbacks, ma_spatializer_listener_ptr pListener);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_uninit(ma_spatializer_listener_ptr pListener, IntPtr pAllocationCallbacks);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_get_channel_map")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_channel_ptr ma_spatializer_listener_get_channel_map(ma_spatializer_listener_ptr pListener);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_set_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_set_cone(ma_spatializer_listener_ptr pListener, float innerAngleInRadians, float outerAngleInRadians, float outerGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_get_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_get_cone(ma_spatializer_listener_ptr pListener, out float pInnerAngleInRadians, out float pOuterAngleInRadians, out float pOuterGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_set_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_set_position(ma_spatializer_listener_ptr pListener, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_get_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_spatializer_listener_get_position(ma_spatializer_listener_ptr pListener);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_set_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_set_direction(ma_spatializer_listener_ptr pListener, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_get_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_spatializer_listener_get_direction(ma_spatializer_listener_ptr pListener);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_set_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_set_velocity(ma_spatializer_listener_ptr pListener, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_get_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_spatializer_listener_get_velocity(ma_spatializer_listener_ptr pListener);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_set_speed_of_sound")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_set_speed_of_sound(ma_spatializer_listener_ptr pListener, float speedOfSound);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_get_speed_of_sound")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_spatializer_listener_get_speed_of_sound(ma_spatializer_listener_ptr pListener);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_set_world_up")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_set_world_up(ma_spatializer_listener_ptr pListener, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_get_world_up")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_spatializer_listener_get_world_up(ma_spatializer_listener_ptr pListener);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_set_enabled")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_listener_set_enabled(ma_spatializer_listener_ptr pListener, ma_bool32 isEnabled);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_listener_is_enabled")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_spatializer_listener_is_enabled(ma_spatializer_listener_ptr pListener);

    // ma_spatializer
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_spatializer_config ma_spatializer_config_init(ma_uint32 channelsIn, ma_uint32 channelsOut);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_heap_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_get_heap_size(ref ma_spatializer_config pConfig, out size_t pHeapSizeInBytes);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_init_preallocated")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_init_preallocated(ref ma_spatializer_config pConfig, IntPtr pHeap, ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_init(ref ma_spatializer_config pConfig, IntPtr pAllocationCallbacks, ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_uninit(ma_spatializer_ptr pSpatializer, IntPtr pAllocationCallbacks);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_process_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_process_pcm_frames(ma_spatializer_ptr pSpatializer, ma_spatializer_listener_ptr pListener, IntPtr pFramesOut, IntPtr pFramesIn, ma_uint64 frameCount);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_master_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_set_master_volume(ma_spatializer_ptr pSpatializer, float volume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_master_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_spatializer_get_master_volume(ma_spatializer_ptr pSpatializer, out float pVolume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_input_channels")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_spatializer_get_input_channels(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_output_channels")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_spatializer_get_output_channels(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_attenuation_model")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_attenuation_model(ma_spatializer_ptr pSpatializer, ma_attenuation_model attenuationModel);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_attenuation_model")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_attenuation_model ma_spatializer_get_attenuation_model(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_positioning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_positioning(ma_spatializer_ptr pSpatializer, ma_positioning positioning);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_positioning")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_positioning ma_spatializer_get_positioning(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_rolloff")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_rolloff(ma_spatializer_ptr pSpatializer, float rolloff);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_rolloff")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_spatializer_get_rolloff(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_min_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_min_gain(ma_spatializer_ptr pSpatializer, float minGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_min_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_spatializer_get_min_gain(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_max_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_max_gain(ma_spatializer_ptr pSpatializer, float maxGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_max_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_spatializer_get_max_gain(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_min_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_min_distance(ma_spatializer_ptr pSpatializer, float minDistance);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_min_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_spatializer_get_min_distance(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_max_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_max_distance(ma_spatializer_ptr pSpatializer, float maxDistance);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_max_distance")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_spatializer_get_max_distance(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_cone(ma_spatializer_ptr pSpatializer, float innerAngleInRadians, float outerAngleInRadians, float outerGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_cone")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_get_cone(ma_spatializer_ptr pSpatializer, out float pInnerAngleInRadians, out float pOuterAngleInRadians, out float pOuterGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_doppler_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_doppler_factor(ma_spatializer_ptr pSpatializer, float dopplerFactor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_doppler_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_spatializer_get_doppler_factor(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_directional_attenuation_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_directional_attenuation_factor(ma_spatializer_ptr pSpatializer, float directionalAttenuationFactor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_directional_attenuation_factor")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_spatializer_get_directional_attenuation_factor(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_position(ma_spatializer_ptr pSpatializer, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_spatializer_get_position(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_direction(ma_spatializer_ptr pSpatializer, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_spatializer_get_direction(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_set_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_set_velocity(ma_spatializer_ptr pSpatializer, float x, float y, float z);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_velocity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_vec3f ma_spatializer_get_velocity(ma_spatializer_ptr pSpatializer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_spatializer_get_relative_position_and_direction")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_spatializer_get_relative_position_and_direction(ma_spatializer_ptr pSpatializer, ma_spatializer_listener_ptr pListener, out ma_vec3f pRelativePos, out ma_vec3f pRelativeDir);

    // ma_decoder
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_decoder_config ma_decoder_config_init(ma_format outputFormat, ma_uint32 outputChannels, ma_uint32 outputSampleRate);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_config_init_default")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_decoder_config ma_decoder_config_init_default();

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern ma_result ma_decoder_init(IntPtr onRead, IntPtr onSeek, IntPtr pUserData, ref ma_decoder_config pConfig, ma_decoder_ptr pDecoder);

    internal static ma_result ma_decoder_init(ma_decoder_read_proc onRead, ma_decoder_seek_proc onSeek, IntPtr pUserData, ref ma_decoder_config pConfig, ma_decoder_ptr pDecoder)
    {
        return ma_decoder_init(MarshalHelper.GetFunctionPointerForDelegate(onRead), MarshalHelper.GetFunctionPointerForDelegate(onSeek), pUserData, ref pConfig, pDecoder);
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_init_memory")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_init_memory(IntPtr pData, size_t dataSize, ref ma_decoder_config pConfig, ma_decoder_ptr pDecoder);

    internal static ma_result ma_decoder_init_memory(IntPtr pData, size_t dataSize, ma_decoder_ptr pDecoder)
    {
        ma_decoder_config config = ma_decoder_config_init_default();
        return ma_decoder_init_memory(pData, dataSize, ref config, pDecoder);
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_init_vfs", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_init_vfs(ma_vfs_ptr pVFS, string pFilePath, ref ma_decoder_config pConfig, ma_decoder_ptr pDecoder);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_init_vfs_w", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_init_vfs_w(ma_vfs_ptr pVFS, string pFilePath, ref ma_decoder_config pConfig, ma_decoder_ptr pDecoder);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_init_file", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_init_file(string pFilePath, ref ma_decoder_config pConfig, ma_decoder_ptr pDecoder);

    internal static ma_result ma_decoder_init_file(string pFilePath, ma_decoder_ptr pDecoder)
    {
        ma_decoder_config config = ma_decoder_config_init_default();
        return ma_decoder_init_file(pFilePath, ref config, pDecoder);
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_init_file_w", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_init_file_w(string pFilePath, ref ma_decoder_config pConfig, ma_decoder_ptr pDecoder);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_uninit(ma_decoder_ptr pDecoder);

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe ma_result ma_decoder_read_pcm_frames(ma_decoder_ptr pDecoder, IntPtr pFramesOut, ma_uint64 frameCount, ma_uint64* pFramesRead);

    internal static ma_result ma_decoder_read_pcm_frames(ma_decoder_ptr pDecoder, IntPtr pFramesOut, ma_uint64 frameCount, IntPtr pFramesRead)
    {
        unsafe
        {
            if (pFramesRead == IntPtr.Zero)
            {
                return ma_decoder_read_pcm_frames(pDecoder, pFramesOut, frameCount, null);
            }
            else
            {
                ma_uint64* pointer = (ma_uint64*)pFramesOut.ToPointer();
                return ma_decoder_read_pcm_frames(pDecoder, pFramesOut, frameCount, pointer);
            }
        }
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_seek_to_pcm_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_seek_to_pcm_frame(ma_decoder_ptr pDecoder, ma_uint64 frameIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_get_data_format")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_get_data_format(ma_decoder_ptr pDecoder, out ma_format pFormat, out ma_uint32 pChannels, out ma_uint32 pSampleRate, ma_channel_ptr pChannelMap, size_t channelMapCap);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_get_cursor_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_get_cursor_in_pcm_frames(ma_decoder_ptr pDecoder, out ma_uint64 pCursor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_get_length_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_get_length_in_pcm_frames(ma_decoder_ptr pDecoder, out ma_uint64 pLength);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decoder_get_available_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decoder_get_available_frames(ma_decoder_ptr pDecoder, out ma_uint64 pAvailableFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decode_from_vfs", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decode_from_vfs(ma_vfs_ptr pVFS, string pFilePath, ref ma_decoder_config pConfig, ref ma_uint64 pFrameCountOut, IntPtr ppPCMFramesOut);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decode_file", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decode_file(string pFilePath, ref ma_decoder_config pConfig, ref ma_uint64 pFrameCountOut, IntPtr ppPCMFramesOut);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_decode_memory")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_decode_memory(IntPtr pData, size_t dataSize, ref ma_decoder_config pConfig, ref ma_uint64 pFrameCountOut, IntPtr ppPCMFramesOut);

    // ma_resource_manager
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_resource_manager_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_resource_manager_config ma_resource_manager_config_init();

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_resource_manager_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_resource_manager_init(ref ma_resource_manager_config pConfig, ma_resource_manager_ptr pResourceManager);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_resource_manager_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_resource_manager_uninit(ma_resource_manager_ptr pResourceManager);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_resource_manager_get_log")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_log_ptr ma_resource_manager_get_log(ma_resource_manager_ptr pResourceManager);

    // ma_gainer
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_gainer_config ma_gainer_config_init(ma_uint32 channels, ma_uint32 smoothTimeInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_get_heap_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_gainer_get_heap_size(ref ma_gainer_config pConfig, out size_t pHeapSizeInBytes);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_init_preallocated")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_gainer_init_preallocated(ref ma_gainer_config pConfig, IntPtr pHeap, ma_gainer_ptr pGainer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_gainer_init(ref ma_gainer_config pConfig, IntPtr pAllocationCallbacks, ma_gainer_ptr pGainer);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_gainer_uninit(ma_gainer_ptr pGainer, IntPtr pAllocationCallbacks);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_process_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_gainer_process_pcm_frames(ma_gainer_ptr pGainer, IntPtr pFramesOut, IntPtr pFramesIn, ma_uint64 frameCount);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_set_gain")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_gainer_set_gain(ma_gainer_ptr pGainer, float newGain);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_set_gains")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_gainer_set_gains(ma_gainer_ptr pGainer, out float pNewGains);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_set_master_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_gainer_set_master_volume(ma_gainer_ptr pGainer, float volume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_gainer_get_master_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_gainer_get_master_volume(ma_gainer_ptr pGainer, out float pVolume);

    // ma_libvorbis
    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    internal static unsafe extern ma_decoding_backend_vtable* ma_libvorbis_get_decoding_backend();

    internal static ma_decoding_backend_vtable_ptr ma_libvorbis_get_decoding_backend_ptr()
    {
        unsafe
        {
            return new ma_decoding_backend_vtable_ptr(new IntPtr(ma_libvorbis_get_decoding_backend()));
        }
    }

    // ma_log
    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern ma_log_callback ma_log_callback_init(IntPtr onLog, IntPtr pUserData);

    internal static ma_log_callback ma_log_callback_init(ma_log_callback_proc onLog, IntPtr pUserData)
    {
        return ma_log_callback_init(MarshalHelper.GetFunctionPointerForDelegate(onLog), pUserData);
    }

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe ma_result ma_log_init(ma_allocation_callbacks* pAllocationCallbacks, ma_log_ptr pLog);

    internal static ma_result ma_log_init(ma_log_ptr pLog)
    {
        unsafe
        {
            return ma_log_init(null, pLog);
        }
    }

    internal static ma_result ma_log_init(ref ma_allocation_callbacks pAllocationCallbacks, ma_log_ptr pLog)
    {
        unsafe
        {
            fixed (ma_allocation_callbacks* pCallbacks = &pAllocationCallbacks)
            {
                return ma_log_init(pCallbacks, pLog);
            }
        }
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_log_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_log_uninit(ma_log_ptr pLog);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_log_register_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_log_register_callback(ma_log_ptr pLog, ma_log_callback callback);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_log_unregister_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_log_unregister_callback(ma_log_ptr pLog, ma_log_callback callback);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_log_post", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_log_post(ma_log_ptr pLog, ma_uint32 level, string pMessage);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_log_level_to_string", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial string ma_log_level_to_string(ma_uint32 logLevel);

    // ma_node_graph
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_graph_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_graph_config ma_node_graph_config_init(ma_uint32 channels);

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe ma_result ma_node_graph_init(ref ma_node_graph_config pConfig, ma_allocation_callbacks* pAllocationCallbacks, ma_node_graph_ptr pNodeGraph);

    internal static ma_result ma_node_graph_init(ref ma_node_graph_config pConfig, ref ma_allocation_callbacks pAllocationCallbacks, ma_node_graph_ptr pNodeGraph)
    {
        unsafe
        {
            fixed (ma_allocation_callbacks* pCallbacks = &pAllocationCallbacks)
            {
                return ma_node_graph_init(ref pConfig, pCallbacks, pNodeGraph);
            }
        }
    }

    internal static ma_result ma_node_graph_init(ref ma_node_graph_config pConfig, ma_node_graph_ptr pNodeGraph)
    {
        unsafe
        {
            return ma_node_graph_init(ref pConfig, null, pNodeGraph);
        }
    }

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void ma_node_graph_uninit(ma_node_graph_ptr pNodeGraph, ma_allocation_callbacks* pAllocationCallbacks);

    internal static void ma_node_graph_uninit(ma_node_graph_ptr pNodeGraph, ref ma_allocation_callbacks pAllocationCallbacks)
    {
        unsafe
        {
            fixed (ma_allocation_callbacks* pCallbacks = &pAllocationCallbacks)
            {
                ma_node_graph_uninit(pNodeGraph, pCallbacks);
            }
        }
    }

    internal static void ma_node_graph_uninit(ma_node_graph_ptr pNodeGraph)
    {
        unsafe
        {
            ma_node_graph_uninit(pNodeGraph, null);
        }
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_graph_get_endpoint")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_ptr ma_node_graph_get_endpoint(ma_node_graph_ptr pNodeGraph);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_graph_read_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_graph_read_pcm_frames(ma_node_graph_ptr pNodeGraph, IntPtr pFramesOut, ma_uint64 frameCount, IntPtr pFramesRead);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_graph_get_channels")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_node_graph_get_channels(ma_node_graph_ptr pNodeGraph);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_graph_get_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint64 ma_node_graph_get_time(ma_node_graph_ptr pNodeGraph);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_graph_set_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_graph_set_time(ma_node_graph_ptr pNodeGraph, ma_uint64 globalTime);

    // ma_node
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_config ma_node_config_init();

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_heap_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_get_heap_size(ma_node_graph_ptr pNodeGraph, ref ma_node_config pConfig, out size_t pHeapSizeInBytes);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_init_preallocated")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_init_preallocated(ma_node_graph_ptr pNodeGraph, ref ma_node_config pConfig, IntPtr pHeap, ma_node_ptr pNode);

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe ma_result ma_node_init(ma_node_graph_ptr pNodeGraph, ref ma_node_config pConfig, ma_allocation_callbacks* pAllocationCallbacks, ma_node_ptr pNode);

    internal static ma_result ma_node_init(ma_node_graph_ptr pNodeGraph, ref ma_node_config pConfig, ref ma_allocation_callbacks pAllocationCallbacks, ma_node_ptr pNode)
    {
        unsafe
        {
            fixed (ma_allocation_callbacks* pCallbacks = &pAllocationCallbacks)
            {
                return ma_node_init(pNodeGraph, ref pConfig, pCallbacks, pNode);
            }
        }
    }

    internal static ma_result ma_node_init(ma_node_graph_ptr pNodeGraph, ref ma_node_config pConfig, ma_node_ptr pNode)
    {
        unsafe
        {
            return ma_node_init(pNodeGraph, ref pConfig, null, pNode);
        }
    }

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void ma_node_uninit(ma_node_ptr pNode, ma_allocation_callbacks* pAllocationCallbacks);

    internal static void ma_node_uninit(ma_node_ptr pNode, ref ma_allocation_callbacks pAllocationCallbacks)
    {
        unsafe
        {
            fixed (ma_allocation_callbacks* pCallbacks = &pAllocationCallbacks)
            {
                ma_node_uninit(pNode, pCallbacks);
            }
        }
    }

    internal static void ma_node_uninit(ma_node_ptr pNode)
    {
        unsafe
        {
            ma_node_uninit(pNode, null);
        }
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_node_graph")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_graph_ptr ma_node_get_node_graph(ma_node_ptr pNode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_input_bus_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_node_get_input_bus_count(ma_node_ptr pNode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_output_bus_count")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_node_get_output_bus_count(ma_node_ptr pNode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_input_channels")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_node_get_input_channels(ma_node_ptr pNode, ma_uint32 inputBusIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_output_channels")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint32 ma_node_get_output_channels(ma_node_ptr pNode, ma_uint32 outputBusIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_attach_output_bus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_attach_output_bus(ma_node_ptr pNode, ma_uint32 outputBusIndex, ma_node_ptr pOtherNode, ma_uint32 otherNodeInputBusIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_detach_output_bus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_detach_output_bus(ma_node_ptr pNode, ma_uint32 outputBusIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_detach_all_output_buses")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_detach_all_output_buses(ma_node_ptr pNode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_set_output_bus_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_set_output_bus_volume(ma_node_ptr pNode, ma_uint32 outputBusIndex, float volume);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_output_bus_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_node_get_output_bus_volume(ma_node_ptr pNode, ma_uint32 outputBusIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_set_state")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_set_state(ma_node_ptr pNode, ma_node_state state);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_state")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_state ma_node_get_state(ma_node_ptr pNode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_set_state_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_set_state_time(ma_node_ptr pNode, ma_node_state state, ma_uint64 globalTime);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_state_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint64 ma_node_get_state_time(ma_node_ptr pNode, ma_node_state state);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_state_by_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_state ma_node_get_state_by_time(ma_node_ptr pNode, ma_uint64 globalTime);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_state_by_time_range")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_node_state ma_node_get_state_by_time_range(ma_node_ptr pNode, ma_uint64 globalTimeBeg, ma_uint64 globalTimeEnd);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_get_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_uint64 ma_node_get_time(ma_node_ptr pNode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_node_set_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_node_set_time(ma_node_ptr pNode, ma_uint64 localTime);

    // ma_effect_node
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_effect_node_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_effect_node_config ma_effect_node_config_init(ma_uint32 channels, ma_uint32 sampleRate, IntPtr onProcess, IntPtr pUserData);

    internal static ma_effect_node_config ma_effect_node_config_init(ma_uint32 channels, ma_uint32 sampleRate, ma_effect_node_process_proc onProcess, IntPtr pUserData)
    {
        return ma_effect_node_config_init(channels, sampleRate, MarshalHelper.GetFunctionPointerForDelegate(onProcess), pUserData);
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_effect_node_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial ma_result ma_effect_node_init(ma_node_graph_ptr pNodeGraph, ref ma_effect_node_config pConfig, ma_allocation_callbacks* pAllocationCallbacks, ma_effect_node_ptr pEffectNode);

    internal static ma_result ma_effect_node_init(ma_node_graph_ptr pNodeGraph, ref ma_effect_node_config pConfig, ma_effect_node_ptr pEffectNode)
    {
        unsafe
        {
            return ma_effect_node_init(pNodeGraph, ref pConfig, null, pEffectNode);
        }
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_effect_node_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial void ma_effect_node_uninit(ma_effect_node_ptr pEffectNode, ma_allocation_callbacks* pAllocationCallbacks);

    internal static void ma_effect_node_uninit(ma_effect_node_ptr pEffectNode)
    {
        unsafe
        {
            ma_effect_node_uninit(pEffectNode, null);
        }
    }

    // ma_data_source
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_data_source_config ma_data_source_config_init();

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_init(ref ma_data_source_config pConfig, ma_data_source_ptr pDataSource);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_uninit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_data_source_uninit(ma_data_source_ptr pDataSource);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_read_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_read_pcm_frames(ma_data_source_ptr pDataSource, IntPtr pFramesOut, ma_uint64 frameCount, IntPtr pFramesRead);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_seek_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_seek_pcm_frames(ma_data_source_ptr pDataSource, ma_uint64 frameCount, out ma_uint64 pFramesSeeked);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_seek_to_pcm_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_seek_to_pcm_frame(ma_data_source_ptr pDataSource, ma_uint64 frameIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_seek_seconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_seek_seconds(ma_data_source_ptr pDataSource, float secondCount, out float pSecondsSeeked);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_seek_to_second")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_seek_to_second(ma_data_source_ptr pDataSource, float seekPointInSeconds);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_data_format")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_get_data_format(ma_data_source_ptr pDataSource, out ma_format pFormat, out ma_uint32 pChannels, out ma_uint32 pSampleRate, ma_channel_ptr pChannelMap, size_t channelMapCap);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_cursor_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_get_cursor_in_pcm_frames(ma_data_source_ptr pDataSource, out ma_uint64 pCursor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_length_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_get_length_in_pcm_frames(ma_data_source_ptr pDataSource, out ma_uint64 pLength);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_cursor_in_seconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_get_cursor_in_seconds(ma_data_source_ptr pDataSource, out float pCursor);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_length_in_seconds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_get_length_in_seconds(ma_data_source_ptr pDataSource, out float pLength);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_set_looping")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_set_looping(ma_data_source_ptr pDataSource, ma_bool32 isLooping);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_is_looping")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_data_source_is_looping(ma_data_source_ptr pDataSource);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_set_range_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_set_range_in_pcm_frames(ma_data_source_ptr pDataSource, ma_uint64 rangeBegInFrames, ma_uint64 rangeEndInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_range_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_data_source_get_range_in_pcm_frames(ma_data_source_ptr pDataSource, out ma_uint64 pRangeBegInFrames, out ma_uint64 pRangeEndInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_set_loop_point_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_set_loop_point_in_pcm_frames(ma_data_source_ptr pDataSource, ma_uint64 loopBegInFrames, ma_uint64 loopEndInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_loop_point_in_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_data_source_get_loop_point_in_pcm_frames(ma_data_source_ptr pDataSource, out ma_uint64 pLoopBegInFrames, out ma_uint64 pLoopEndInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_set_current")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_set_current(ma_data_source_ptr pDataSource, ma_data_source_ptr pCurrentDataSource);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_current")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_data_source_ptr ma_data_source_get_current(ma_data_source_ptr pDataSource);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_set_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_set_next(ma_data_source_ptr pDataSource, ma_data_source_ptr pNextDataSource);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_data_source_ptr ma_data_source_get_next(ma_data_source_ptr pDataSource);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_set_next_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_set_next_callback(ma_data_source_ptr pDataSource, ma_data_source_get_next_proc onGetNext);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_get_next_callback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_data_source_get_next_proc ma_data_source_get_next_callback(ma_data_source_ptr pDataSource);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_node_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_data_source_node_config ma_data_source_node_config_init(ma_data_source_ptr pDataSource);

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe ma_result ma_data_source_node_init(ma_node_graph_ptr pNodeGraph, ref ma_data_source_node_config pConfig, ma_allocation_callbacks* pAllocationCallbacks, ma_data_source_node_ptr pDataSourceNode);

    internal static ma_result ma_data_source_node_init(ma_node_graph_ptr pNodeGraph, ref ma_data_source_node_config pConfig, ref ma_allocation_callbacks pAllocationCallbacks, ma_data_source_node_ptr pDataSourceNode)
    {
        unsafe
        {
            fixed (ma_allocation_callbacks* pCallbacks = &pAllocationCallbacks)
            {
                return ma_data_source_node_init(pNodeGraph, ref pConfig, pCallbacks, pDataSourceNode);
            }
        }
    }

    internal static ma_result ma_data_source_node_init(ma_node_graph_ptr pNodeGraph, ref ma_data_source_node_config pConfig, ma_data_source_node_ptr pDataSourceNode)
    {
        unsafe
        {
            return ma_data_source_node_init(pNodeGraph, ref pConfig, null, pDataSourceNode);
        }
    }

    [DllImport(LIB_MINIAUDIO_EX, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void ma_data_source_node_uninit(ma_data_source_node_ptr pDataSourceNode, ma_allocation_callbacks* pAllocationCallbacks);

    internal static void ma_data_source_node_uninit(ma_data_source_node_ptr pDataSourceNode, ref ma_allocation_callbacks pAllocationCallbacks)
    {
        unsafe
        {
            fixed (ma_allocation_callbacks* pCallbacks = &pAllocationCallbacks)
            {
                ma_data_source_node_uninit(pDataSourceNode, pCallbacks);
            }
        }
    }

    internal static unsafe void ma_data_source_node_uninit(ma_data_source_node_ptr pDataSourceNode)
    {
        unsafe
        {
            ma_data_source_node_uninit(pDataSourceNode, null);
        }
    }

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_node_set_looping")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_data_source_node_set_looping(ma_data_source_node_ptr pDataSourceNode, ma_bool32 isLooping);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_data_source_node_is_looping")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_data_source_node_is_looping(ma_data_source_node_ptr pDataSourceNode);

    // ma_fader
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_fader_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_fader_config ma_fader_config_init(ma_format format, ma_uint32 channels, ma_uint32 sampleRate);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_fader_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_fader_init(ref ma_fader_config pConfig, ma_fader_ptr pFader);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_fader_process_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_fader_process_pcm_frames(ma_fader_ptr pFader, IntPtr pFramesOut, IntPtr pFramesIn, ma_uint64 frameCount);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_fader_get_data_format")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_fader_get_data_format(ma_fader_ptr pFader, out ma_format pFormat, out ma_uint32 pChannels, out ma_uint32 pSampleRate);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_fader_set_fade")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_fader_set_fade(ma_fader_ptr pFader, float volumeBeg, float volumeEnd, ma_uint64 lengthInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_fader_set_fade_ex")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_fader_set_fade_ex(ma_fader_ptr pFader, float volumeBeg, float volumeEnd, ma_uint64 lengthInFrames, ma_int64 startOffsetInFrames);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_fader_get_current_volume")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_fader_get_current_volume(ma_fader_ptr pFader);

    // ma_panner
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_panner_config_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_panner_config ma_panner_config_init(ma_format format, ma_uint32 channels);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_panner_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_panner_init(ref ma_panner_config pConfig, ma_panner_ptr pPanner);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_panner_process_pcm_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_result ma_panner_process_pcm_frames(ma_panner_ptr pPanner, IntPtr pFramesOut, IntPtr pFramesIn, ma_uint64 frameCount);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_panner_set_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_panner_set_mode(ma_panner_ptr pPanner, ma_pan_mode mode);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_panner_get_mode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_pan_mode ma_panner_get_mode(ma_panner_ptr pPanner);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_panner_set_pan")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_panner_set_pan(ma_panner_ptr pPanner, float pan);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_panner_get_pan")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial float ma_panner_get_pan(ma_panner_ptr pPanner);

    // ma_channel_map
    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_get_channel")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_channel ma_channel_map_get_channel(ma_channel_ptr pChannelMap, ma_uint32 channelCount, ma_uint32 channelIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_init_blank")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_channel_map_init_blank(ma_channel_ptr pChannelMap, ma_uint32 channels);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_init_standard")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_channel_map_init_standard(ma_standard_channel_map standardChannelMap, ma_channel_ptr pChannelMap, size_t channelMapCap, ma_uint32 channels);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_copy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_channel_map_copy(ma_channel_ptr pOut, ma_channel_ptr pIn, ma_uint32 channels);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_copy_or_default")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void ma_channel_map_copy_or_default(ma_channel_ptr pOut, size_t channelMapCapOut, ma_channel_ptr pIn, ma_uint32 channels);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_is_valid")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_channel_map_is_valid(ma_channel_ptr pChannelMap, ma_uint32 channels);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_is_equal")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_channel_map_is_equal(ma_channel_ptr pChannelMapA, ma_channel_ptr pChannelMapB, ma_uint32 channels);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_is_blank")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_channel_map_is_blank(ma_channel_ptr pChannelMap, ma_uint32 channels);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_contains_channel_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_channel_map_contains_channel_position(ma_uint32 channels, ma_channel_ptr pChannelMap, ma_channel channelPosition);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_find_channel_position")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial ma_bool32 ma_channel_map_find_channel_position(ma_uint32 channels, ma_channel_ptr pChannelMap, ma_channel channelPosition, out ma_uint32 pChannelIndex);

    [LibraryImport(LIB_MINIAUDIO_EX, EntryPoint = "ma_channel_map_to_string")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial size_t ma_channel_map_to_string(ma_channel_ptr pChannelMap, ma_uint32 channels, IntPtr pBufferOut, size_t bufferCap);
}

internal static class MarshalHelper
{
    /// <summary>
    /// A value that can be passed to unmanaged code, which, in turn, can use it to call the underlying managed delegate. Does not throw exceptions.
    /// </summary>
    /// <typeparam name="TDelegate">The type of delegate to convert.</typeparam>
    /// <param name="d">The delegate to be passed to unmanaged code.</param>
    /// <returns>A value that can be passed to unmanaged code, which, in turn, can use it to call the underlying managed delegate. Returns IntPtr.Zero if the passed delegate is null.</returns>
    internal static IntPtr GetFunctionPointerForDelegate<TDelegate>(TDelegate d) where TDelegate : notnull
    {
        if (d == null)
            return IntPtr.Zero;
        return Marshal.GetFunctionPointerForDelegate(d);
    }

    /// <summary>
    /// Helper method that supports netstandard 2.0. Converts a pointer to an UTF8 string.
    /// </summary>
    /// <param name="p">The pointer with the string to convert</param>
    /// <returns>A UTF8 encoded string</returns>
    internal static string PtrToStringUTF8(IntPtr p)
    {
        if (p == IntPtr.Zero)
            return null;

        int length = 0;

        while (Marshal.ReadByte(p, length) != 0)
        {
            length++;
        }

        byte[] bytes = new byte[length];
        Marshal.Copy(p, bytes, 0, length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    internal static IntPtr AllocHGlobal(int cb)
    {
        return Marshal.AllocHGlobal(cb);
    }

    internal static IntPtr AllocHGlobal(byte[] data, out int size)
    {
        size = 0;

        if (data == null)
            return IntPtr.Zero;

        if (data.Length == 0)
            return IntPtr.Zero;

        IntPtr pData = Marshal.AllocHGlobal(data.Length);

        if (pData == IntPtr.Zero)
            return IntPtr.Zero;

        Marshal.Copy(data, 0, pData, data.Length);
        size = data.Length;
        return pData;
    }

    internal static void FreeHGlobal(IntPtr hglobal)
    {
        Marshal.FreeHGlobal(hglobal);
    }
}
