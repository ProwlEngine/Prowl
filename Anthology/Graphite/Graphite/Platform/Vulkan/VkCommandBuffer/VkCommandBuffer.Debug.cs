using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private protected override void PushDebugGroupCore(string name)
    {
        if (_gd.DebugUtils == null) return;

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        DebugUtilsLabelEXT label = Label(name, utf8Ptr);
        _gd.DebugUtils.CmdBeginDebugUtilsLabel(_cb, &label);
    }

    private protected override void InsertDebugMarkerCore(string name)
    {
        if (_gd.DebugUtils == null) return;

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        DebugUtilsLabelEXT label = Label(name, utf8Ptr);
        _gd.DebugUtils.CmdInsertDebugUtilsLabel(_cb, &label);
    }

    private protected override void PopDebugGroupCore() => _gd.DebugUtils?.CmdEndDebugUtilsLabel(_cb);

    private static DebugUtilsLabelEXT Label(string name, byte* utf8Buffer)
    {
        Utf8Stack.Write(name, utf8Buffer);

        return new DebugUtilsLabelEXT
        {
            SType = StructureType.DebugUtilsLabelExt,
            PLabelName = utf8Buffer
        };
    }
}
