namespace Prowl.Slang.Native;


/** A (real or virtual) file system.

Slang can make use of this interface whenever it would otherwise try to load files
from disk, allowing applications to hook and/or override filesystem access from
the compiler.

It is the responsibility of
the caller of any method that returns a ISlangBlob to release the blob when it is no
longer used (using 'release').
*/

[UUID(0x003A09FC, 0x3A4D, 0x4BA0, 0xAD, 0x60, 0x1F, 0xD8, 0x63, 0xA9, 0x15, 0xAB)]
public unsafe interface ISlangFileSystem : ISlangCastable
{
    /** Load a file from `path` and return a blob of its contents
    @param path The path to load from, as a null-terminated UTF-8 string.
    @param outBlob A destination pointer to receive the blob of the file contents.
    @returns A `SlangResult` to indicate success or failure in loading the file.

    NOTE! This is a *binary* load - the blob should contain the exact same bytes
    as are found in the backing file.

    If load is successful, the implementation should create a blob to hold
    the file's content, store it to `outBlob`, and return 0.
    If the load fails, the implementation should return a failure status
    (any negative value will do).
    */
    SlangResult LoadFile(ConstU8Str path, out ISlangBlob* outBlob);
}
