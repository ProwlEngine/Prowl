using SlangUInt = nuint;

namespace Prowl.Slang.Native;


[UUID(0x8044a8a3, 0xddc0, 0x4b7f, 0xaf, 0x8e, 0x2, 0x6e, 0x90, 0x5d, 0x73, 0x32)]
internal interface IMetadata : ISlangCastable
{
    SlangResult IsParameterLocationUsed(
        SlangParameterCategory category,
        SlangUInt spaceIndex,
        SlangUInt registerIndex,
        out CBool outUsed);
}
