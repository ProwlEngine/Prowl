// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Threading.Tasks;

using Prowl.Slang.Native;


namespace Prowl.Slang;


/// <summary>
/// A proxy file provider used by Slang's compiler when searching for and loading files from the user's filesystem.
/// </summary>
public interface IFileProvider
{
    /// <summary>
    /// Gets the bytes for a file at a given path.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The memory bytes of the file.</returns>
    public Memory<byte>? LoadFile(string path);
}


internal sealed unsafe class FileSystem(IFileProvider provider) : ManagedComProxy<ISlangFileSystem>, ISlangFileSystem
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
