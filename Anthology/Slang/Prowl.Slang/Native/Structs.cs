using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

using System;
using System.Linq;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
public unsafe struct CompilerOptionEntry
{
    public CompilerOptionName Name;
    public CompilerOptionValueKind Kind;

    public int IntValue0;
    public int IntValue1;

    public ConstU8Str StringValue0;
    public ConstU8Str StringValue1;


    public CompilerOptionEntry Allocate(Slang.CompilerOptionEntry src)
    {
        Name = src.Name;
        Kind = src.Value.Kind;
        IntValue0 = src.Value.IntValue0;
        IntValue1 = src.Value.IntValue1;

        if (src.Value.StringValue0 != null)
            StringValue0 = U8Str.Alloc(src.Value.StringValue0);

        if (src.Value.StringValue1 != null)
            StringValue1 = U8Str.Alloc(src.Value.StringValue1);

        return this;
    }


    public void Free()
    {
        if (StringValue0.Data != null)
            NativeMemory.Free(StringValue0.Data);

        if (StringValue1.Data != null)
            NativeMemory.Free(StringValue1.Data);

        StringValue0.Data = null;
        StringValue1.Data = null;
    }


    public Slang.CompilerOptionEntry Read()
    {
        return new()
        {
            Name = Name,
            Value = new()
            {
                Kind = Kind,
                IntValue0 = IntValue0,
                IntValue1 = IntValue1,
                StringValue0 = StringValue0.String,
                StringValue1 = StringValue1.String
            }
        };
    }
}


/** Description of a code generation target.
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct TargetDescription()
{
    private nuint _structureSize = (nuint)sizeof(TargetDescription);

    /** The target format to generate code for (e.g., SPIR-V, DXIL, etc.)
     */
    public SlangCompileTarget Format = SlangCompileTarget.Unknown;

    /** The compilation profile supported by the target (e.g., "Shader Model 5.1")
     */
    public SlangProfileID Profile = SlangProfileID.UNKNOWN;

    /** Flags for the code generation target. Currently unused. */
    public SlangTargetFlags Flags = SlangTargetFlags.Default;

    /** Default mode to use for floating-point operations on the target.
     */
    public SlangFloatingPointMode FloatingPointMode = SlangFloatingPointMode.DEFAULT;

    /** The line directive mode for output source code.
     */
    public SlangLineDirectiveMode LineDirectiveMode = SlangLineDirectiveMode.DEFAULT;

    /** Whether to force `scalar` layout for glsl shader storage buffers.
     */
    public CBool ForceGLSLScalarBufferLayout = false;


    public NativeUIntArray<CompilerOptionEntry> CompilerOptionEntries;


    public TargetDescription Allocate(Slang.TargetDescription src)
    {
        Format = src.Format;
        Profile = src.Profile;
        Flags = src.Flags;
        FloatingPointMode = src.FloatingPointMode;
        LineDirectiveMode = src.LineDirectiveMode;
        ForceGLSLScalarBufferLayout = src.ForceGLSLScalarBufferLayout;

        if (src.CompilerOptionEntries != null)
            CompilerOptionEntries.Allocate([.. src.CompilerOptionEntries.Select(x => new CompilerOptionEntry().Allocate(x))]);

        return this;
    }


    public void Free()
    {
        CompilerOptionEntries.ForEach(x => x.Free());
        CompilerOptionEntries.Free();
    }


    public Slang.TargetDescription Read()
    {
        return new()
        {
            Format = Format,
            Profile = Profile,
            Flags = Flags,
            FloatingPointMode = FloatingPointMode,
            LineDirectiveMode = LineDirectiveMode,
            ForceGLSLScalarBufferLayout = ForceGLSLScalarBufferLayout,
            CompilerOptionEntries = [.. CompilerOptionEntries.Select(x => x.Read())]
        };
    }
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct PreprocessorMacroDescription
{
    public ConstU8Str Name;
    public ConstU8Str Value;


    public PreprocessorMacroDescription Allocate(Slang.PreprocessorMacroDescription src)
    {
        if (src.Name != null)
            Name = U8Str.Alloc(src.Name);

        if (src.Value != null)
            Value = U8Str.Alloc(src.Value);

        return this;
    }


    public void Free()
    {
        if (Name.Data != null)
            NativeMemory.Free(Name.Data);

        if (Value.Data != null)
            NativeMemory.Free(Value.Data);

        Name.Data = null;
        Value.Data = null;
    }


    public Slang.PreprocessorMacroDescription Read()
    {
        return new()
        {
            Name = Name.String,
            Value = Value.String
        };
    }
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SessionDescription()
{
    private nuint _structureSize = (nuint)sizeof(SessionDescription);

    /** Code generation targets to include in the session.
     */
    public NativeNIntArray<TargetDescription> Targets;

    /** Flags to configure the session.
     */
    public SessionFlags Flags = SessionFlags.None;

    /** Default layout to assume for variables with matrix types.
     */
    public SlangMatrixLayoutMode DefaultMatrixLayoutMode = SlangMatrixLayoutMode.ROW_MAJOR;

    /** Paths to use when searching for `#include`d or `import`ed files.
     */
    public NativeNIntArray<ConstU8Str> SearchPaths;

    public NativeNIntArray<PreprocessorMacroDescription> PreprocessorMacros;

    public ISlangFileSystem* FileSystem;

    public CBool EnableEffectAnnotations;
    public CBool AllowGLSLSyntax;


    public NativeUIntArray<CompilerOptionEntry> CompilerOptionEntries;


    public SessionDescription Allocate(Slang.SessionDescription src, out FileSystem? fsAllocation)
    {
        Flags = src.Flags;
        DefaultMatrixLayoutMode = src.DefaultMatrixLayoutMode;
        EnableEffectAnnotations = src.EnableEffectAnnotations;
        AllowGLSLSyntax = src.AllowGLSLSyntax;

        fsAllocation = null;
        if (src.FileProvider != null)
            FileSystem = fsAllocation = new FileSystem(src.FileProvider);

        if (src.Targets != null)
            Targets.Allocate([.. src.Targets.Select(x => new TargetDescription().Allocate(x))]);

        if (src.SearchPaths != null)
            SearchPaths.Allocate([.. src.SearchPaths.Select(U8Str.Alloc)]);

        if (src.PreprocessorMacros != null)
            PreprocessorMacros.Allocate([.. src.PreprocessorMacros.Select(x => new PreprocessorMacroDescription().Allocate(x))]);

        if (src.CompilerOptionEntries != null)
            CompilerOptionEntries.Allocate([.. src.CompilerOptionEntries.Select(x => new CompilerOptionEntry().Allocate(x))]);

        return this;
    }


    public void Free(FileSystem? fsAllocation)
    {
        Targets.ForEach(x => x.Free());
        Targets.Free();

        SearchPaths.ForEach(x => NativeMemory.Free(x.Data));
        SearchPaths.Free();

        PreprocessorMacros.ForEach(x => x.Free());
        PreprocessorMacros.Free();

        CompilerOptionEntries.ForEach(x => x.Free());
        CompilerOptionEntries.Free();

        fsAllocation?.Release();
    }


    public Slang.SessionDescription Read()
    {
        return new()
        {
            Flags = Flags,
            DefaultMatrixLayoutMode = DefaultMatrixLayoutMode,
            EnableEffectAnnotations = EnableEffectAnnotations,
            AllowGLSLSyntax = AllowGLSLSyntax,
            Targets = [.. Targets.Select(x => x.Read())],
            SearchPaths = [.. SearchPaths.Select(x => x.String)],
            PreprocessorMacros = [.. PreprocessorMacros.Select(x => x.Read())],
            CompilerOptionEntries = [.. CompilerOptionEntries.Select(x => x.Read())]
        };
    }
};


/// <summary>
/// Description of a Slang global session.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GlobalSessionDescription()
{
    private uint _structureSize = (uint)sizeof(GlobalSessionDescription);

    /// Slang API version.
    public uint ApiVersion = 0;

    /// Slang language version.
    public SlangLanguageVersion LanguageVersion = SlangLanguageVersion._2025;

    /// Whether to enable GLSL support.
    public CBool EnableGLSL = false;

    /// Reserved for future use.
    private unsafe fixed uint _reserved[16];
};