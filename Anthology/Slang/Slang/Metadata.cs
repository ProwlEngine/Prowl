using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class Metadata
{
    internal IMetadata _metadata;


    internal Metadata(IMetadata metadata)
    {
        _metadata = metadata;
    }


    /*
Returns whether a resource parameter at the specified binding location is actually being used
in the compiled shader.
*/
    public bool IsParameterLocationUsed(
        SlangParameterCategory category, // is this a `t` register? `s` register?
        uint spaceIndex,            // `space` for D3D12, `set` for Vulkan
        uint registerIndex)         // `register` for D3D12, `binding` for Vulkan
    {
        _metadata.IsParameterLocationUsed(category, spaceIndex, registerIndex, out CBool outUsed).Throw();

        return outUsed;
    }
}
