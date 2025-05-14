namespace Prowl.Slang.NativeAPI;


[UUID(0x6264ab2b, 0xa3e8, 0x4a06, 0x97, 0xf1, 0x49, 0xbc, 0x2d, 0x2a, 0xb1, 0x4d)]
public unsafe interface ISlangSharedLibraryLoader : IUnknown
{
    /** Load a shared library. In typical usage the library name should *not* contain any
    platform specific elements. For example on windows a dll name should *not* be passed with a
    '.dll' extension, and similarly on linux a shared library should *not* be passed with the
    'lib' prefix and '.so' extension
    @path path The unadorned filename and/or path for the shared library
    @ param sharedLibraryOut Holds the shared library if successfully loaded */
    SlangResult LoadSharedLibrary(ConstU8Str path, out ISlangSharedLibrary* sharedLibraryOut);
}
