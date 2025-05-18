namespace Prowl.Slang.Native;


[UUID(0x003A09FC, 0x3A4D, 0x4BA0, 0xAD, 0x60, 0x1F, 0xD8, 0x63, 0xA9, 0x15, 0xAB)]
internal unsafe interface ISlangFileSystem : ISlangCastable
{
    SlangResult LoadFile(ConstU8Str path, out ISlangBlob* outBlob);
}
