using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Graphite.Vk;

internal static unsafe class CommonStrings
{
    public const string VK_KHR_SURFACE_EXTENSION_NAME = "VK_KHR_surface";
    public static byte* VK_KHR_SURFACE_EXTENSION_NAMEUtf8 => Utf8("VK_KHR_surface"u8);

    public const string VK_KHR_WIN32_SURFACE_EXTENSION_NAME = "VK_KHR_win32_surface";

    public const string VK_KHR_XLIB_SURFACE_EXTENSION_NAME = "VK_KHR_xlib_surface";

    public const string VK_KHR_ANDROID_SURFACE_EXTENSION_NAME = "VK_KHR_android_surface";

    public const string VK_MVK_MACOS_SURFACE_EXTENSION_NAME = "VK_MVK_macos_surface";

    public const string VK_MVK_IOS_SURFACE_EXTENSION_NAME = "VK_MVK_ios_surface";

    public const string VK_EXT_DEBUG_REPORT_EXTENSION_NAME = "VK_EXT_debug_report";
    public static byte* VK_EXT_DEBUG_REPORT_EXTENSION_NAMEUtf8 => Utf8("VK_EXT_debug_report"u8);

    public const string VK_EXT_DEBUG_MARKER_EXTENSION_NAME = "VK_EXT_debug_marker";
    public static byte* VK_EXT_DEBUG_MARKER_EXTENSION_NAMEUtf8 => Utf8("VK_EXT_debug_marker"u8);

    public const string StandardValidationLayerName = "VK_LAYER_LUNARG_standard_validation";
    public static byte* StandardValidationLayerNameUtf8 => Utf8("VK_LAYER_LUNARG_standard_validation"u8);

    public const string KhronosValidationLayerName = "VK_LAYER_KHRONOS_validation";
    public static byte* KhronosValidationLayerNameUtf8 => Utf8("VK_LAYER_KHRONOS_validation"u8);

    public const string main = "main";
    public static byte* mainUtf8 => Utf8("main"u8);

    public const string VK_KHR_get_physical_device_properties2 = "VK_KHR_get_physical_device_properties2";
    public static byte* VK_KHR_get_physical_device_properties2Utf8 => Utf8("VK_KHR_get_physical_device_properties2"u8);

    public const string VK_KHR_portability_subset = "VK_KHR_portability_subset";
    public static byte* VK_KHR_portability_subsetUtf8 => Utf8("VK_KHR_portability_subset"u8);

    public const string VK_KHR_portability_enumeration = "VK_KHR_portability_enumeration";
    public static byte* VK_KHR_portability_enumerationUtf8 => Utf8("VK_KHR_portability_enumeration"u8);

    // u8 literals live in the assembly's readonly data section, so the pointer needs no pinning
    // and stays valid for the process lifetime. Only ever pass a u8 literal here.
    internal static byte* Utf8(ReadOnlySpan<byte> literal) =>
        (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(literal));
}
