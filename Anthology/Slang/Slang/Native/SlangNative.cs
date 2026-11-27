// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using SlangInt = nint;

namespace Prowl.Slang.Native;


internal static unsafe partial class SlangNative
{
    const string LibName = "slang-compiler";

    [LibraryImport(LibName)]
    public static partial SlangResult slang_createGlobalSession(SlangInt apiVersion, out IGlobalSession* outGlobalSession);

    [LibraryImport(LibName)]
    public static partial SlangResult slang_createGlobalSession2(GlobalSessionDescription* desc, out IGlobalSession* outGlobalSession);

    [LibraryImport(LibName)]
    public static partial SlangResult slang_createGlobalSessionWithoutCoreModule(SlangInt apiVersion, out IGlobalSession* outGlobalSession);

    [LibraryImport(LibName)]
    public static partial void slang_shutdown();

    [LibraryImport(LibName)]
    public static partial ConstU8Str slang_getLastInternalErrorMessage();
}
