using System.Runtime.InteropServices;
using System.Linq;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SessionDescription()
{
    private nuint _structureSize = (nuint)sizeof(SessionDescription);

    public NativeNIntArray<TargetDescription> Targets;

    public SessionFlags Flags = SessionFlags.None;

    public SlangMatrixLayoutMode DefaultMatrixLayoutMode = SlangMatrixLayoutMode.ROW_MAJOR;

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
}
