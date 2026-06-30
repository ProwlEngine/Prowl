using SlangUInt = nuint;

namespace Prowl.Slang.NativeAPI;


[UUID(0x8044a8a3, 0xddc0, 0x4b7f, 0xaf, 0x8e, 0x2, 0x6e, 0x90, 0x5d, 0x73, 0x32)]
public interface IMetadata : ISlangCastable
{
    /*
    Returns whether a resource parameter at the specified binding location is actually being used
    in the compiled shader.
    */
    SlangResult IsParameterLocationUsed(
        SlangParameterCategory category, // is this a `t` register? `s` register?
        SlangUInt spaceIndex,            // `space` for D3D12, `set` for Vulkan
        SlangUInt registerIndex,         // `register` for D3D12, `binding` for Vulkan
        out CBool outUsed);
}
