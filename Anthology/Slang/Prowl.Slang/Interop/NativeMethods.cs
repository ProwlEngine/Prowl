using System;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


internal static unsafe partial class SlangNative
{
    const string LibName = "slang";

    [LibraryImport(LibName)]
    public static partial SlangResult slang_createGlobalSession(int apiVersion, IGlobalSession** outGlobalSession);

    [LibraryImport(LibName)]
    public static partial SlangResult slang_createGlobalSession2(SlangGlobalSessionDesc* desc, IGlobalSession** outGlobalSession);

    [LibraryImport(LibName)]
    public static partial SlangResult slang_createGlobalSessionWithoutCoreModule(int apiVersion, IGlobalSession** outGlobalSession);

    // [LibraryImport(LibName)]
    // public static partial ISlangBlob* slang_getEmbeddedCoreModule();

    [LibraryImport(LibName)]
    public static partial void slang_shutdown();
}
