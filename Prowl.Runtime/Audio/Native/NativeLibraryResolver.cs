// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Runtime.Audio.Native;

/// <summary>
/// Resolves the "miniaudioex" native library from its vendored
/// <c>runtimes/&lt;rid&gt;/native/</c> folder (see Prowl.Runtime.csproj's CopyLibraries target).
/// Unlike a real NuGet native-asset package, this folder isn't recognized by the SDK's publish
/// pipeline, so a self-contained publish never flattens it to the app root and the CLR's default
/// probing doesn't find it there either. Resolving it explicitly here works the same way
/// regardless of build/publish mode.
/// </summary>
internal static class NativeLibraryResolver
{
    [ModuleInitializer]
    internal static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "miniaudioex")
            return IntPtr.Zero;

        string? os = OperatingSystem.IsWindows() ? "win"
                   : OperatingSystem.IsMacOS() ? "osx"
                   : OperatingSystem.IsLinux() ? "linux"
                   : null;

        // ProcessArchitecture (not OSArchitecture): a 32-bit process on a 64-bit Windows OS must
        // load the x86 native lib, not x64 - Libraries/win-x86 exists precisely for that case.
        string? arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => null,
        };

        if (os == null || arch == null)
            return IntPtr.Zero;

        string fileName = OperatingSystem.IsWindows() ? "miniaudioex.dll"
                         : OperatingSystem.IsMacOS() ? "libminiaudioex.dylib"
                         : "libminiaudioex.so";

        string path = Path.Combine(AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", fileName);
        return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
    }
}
