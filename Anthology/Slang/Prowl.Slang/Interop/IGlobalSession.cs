using System;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate SlangResult QueryInterfaceDelegate(void* thisPtr, ref SlangUUID uuid, out IntPtr outObject);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate uint AddRefDelegate(IntPtr thisPtr);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate uint ReleaseDelegate(IntPtr thisPtr);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate SlangResult CreateSessionDelegate(void* thisPtr, SessionDesc* sessionDesc, out IntPtr outSession);


[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGlobalSession
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VTable
    {
        public IntPtr QueryInterface;
        public IntPtr AddRef;
        public IntPtr Release;
        public IntPtr CreateSession;
    }

    public VTable* lpVtbl;
}


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SlangGlobalSessionDesc()
{
    // Size of this struct.
    public uint structureSize = (uint)sizeof(SlangGlobalSessionDesc);

    // Slang API version.
    public uint apiVersion = 0;

    // Slang language version.
    public uint languageVersion = 2025;

    // Whether to enable GLSL support.
    public bool enableGLSL = false;

    // Reserved for future use.
    fixed uint reserved[16];
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SessionDesc()
{
    // The size of this structure, in bytes.
    public nuint structureSize = (nuint)sizeof(SessionDesc);

    // Code generation targets to include in the session.
    public void* targets = null;
    public int targetCount = 0;

    // Flags to configure the session.
    public uint flags = 0;

    // Default layout to assume for variables with matrix types.
    public uint defaultMatrixLayoutMode = 1;

    // Paths to use when searching for `#include`d or `import`ed files.
    public char** searchPaths = null;
    public int searchPathCount = 0;

    public void* preprocessorMacros = null;
    public int preprocessorMacroCount = 0;

    public void* fileSystem = null;

    public bool enableEffectAnnotations = false;
    public bool allowGLSLSyntax = false;

    // Pointer to an array of compiler option entries, whose size is compilerOptionEntryCount.
    public void* compilerOptionEntries = null;

    // Number of additional compiler option entries.
    public uint compilerOptionEntryCount = 0;

    // Whether to skip SPIRV validation.
    public bool skipSPIRVValidation = false;
};
