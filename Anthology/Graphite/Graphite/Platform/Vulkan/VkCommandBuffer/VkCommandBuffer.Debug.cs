using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private protected override void PushDebugGroupCore(string name)
    {
        vkCmdDebugMarkerBeginEXT_t func = _gd.MarkerBegin;
        if (func == null) return;

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        DebugMarkerMarkerInfoEXT markerInfo = MarkerInfo(name, utf8Ptr);
        func(_cb, &markerInfo);
    }

    private protected override void InsertDebugMarkerCore(string name)
    {
        vkCmdDebugMarkerInsertEXT_t func = _gd.MarkerInsert;
        if (func == null) return;

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        DebugMarkerMarkerInfoEXT markerInfo = MarkerInfo(name, utf8Ptr);
        func(_cb, &markerInfo);
    }

    private protected override void PopDebugGroupCore() => _gd.MarkerEnd?.Invoke(_cb);

    // The caller owns the buffer: a stackalloc made here would be freed on return.
    private static DebugMarkerMarkerInfoEXT MarkerInfo(string name, byte* utf8Buffer)
    {
        Utf8Stack.Write(name, utf8Buffer);

        return new DebugMarkerMarkerInfoEXT
        {
            SType = StructureType.DebugMarkerMarkerInfoExt,
            PMarkerName = utf8Buffer
        };
    }
}
