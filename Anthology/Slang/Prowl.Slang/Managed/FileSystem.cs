using System;
using System.Runtime.InteropServices;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public interface IFileProvider
{
    public Memory<byte>? LoadFile(string path);
}


public sealed unsafe class FileSystem(IFileProvider provider) : ManagedComProxy<ISlangFileSystem>, ISlangFileSystem
{
    public IFileProvider Provider = provider;

    public unsafe void* CastAs(ref Guid guid) => null;

    public unsafe SlangResult LoadFile(ConstU8Str path, out ISlangBlob* outBlob)
    {
        Memory<byte>? memory = Provider.LoadFile(path.String);

        if (memory == null)
        {
            outBlob = null;
            return SlangResult.Ok;
        }

        outBlob = ManagedBlob.FromMemory(memory.Value);
        return SlangResult.Ok;
    }
}