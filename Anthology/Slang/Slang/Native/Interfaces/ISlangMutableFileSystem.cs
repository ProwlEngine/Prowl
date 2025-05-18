namespace Prowl.Slang.Native;


[UUID(0xa058675c, 0x1d65, 0x452a, 0x84, 0x58, 0xcc, 0xde, 0xd1, 0x42, 0x71, 0x5)]
public unsafe interface ISlangMutableFileSystem : ISlangFileSystemExt
{
    /** Write data to the specified path.

    @param path The path for data to be saved to
    @param data The data to be saved
    @param size The size of the data in bytes
    @returns SLANG_OK if successful (SLANG_E_NOT_IMPLEMENTED if not implemented, or some other
    error code)
    */
    SlangResult SaveFile(ConstU8Str path, void* data, nuint size);

    /** Write data in the form of a blob to the specified path.

    Depending on the implementation writing a blob might be faster/use less memory. It is
    assumed the blob is *immutable* and that an implementation can reference count it.

    It is not guaranteed loading the same file will return the *same* blob - just a blob with
    same contents.

    @param path The path for data to be saved to
    @param dataBlob The data to be saved
    @returns SLANG_OK if successful (SLANG_E_NOT_IMPLEMENTED if not implemented, or some other
    error code)
    */
    SlangResult SaveFileBlob(ConstU8Str path, ISlangBlob* dataBlob);

    /** Remove the entry in the path (directory of file). Will only delete an empty directory,
    if not empty will return an error.

    @param path The path to remove
    @returns SLANG_OK if successful
    */
    SlangResult Remove(ConstU8Str path);

    /** Create a directory.

    The path to the directory must exist

    @param path To the directory to create. The parent path *must* exist otherwise will return
    an error.
    @returns SLANG_OK if successful
    */
    SlangResult CreateDirectory(ConstU8Str path);
}
