using System;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class Module : ComponentType
{
    internal IModule _module;


    internal Module(IModule module, Session session) : base(module, session)
    {
        _module = module;
    }


    /// Find and an entry point by name.
    /// Note that this does not work in case the function is not explicitly designated as an entry
    /// point, e.g. using a `[shader("...")]` attribute. In such cases, consider using
    /// `IModule::findAndCheckEntryPoint` instead.
    public EntryPoint FindEntryPointByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        _module.FindEntryPointByName(str, out IEntryPoint* entryPointPtr).Throw();

        return new EntryPoint(NativeComProxy.Create(entryPointPtr), _session);
    }

    /// Get number of entry points defined in the module. An entry point defined in a module
    /// is by default not included in the linkage, so calls to `IComponentType::getEntryPointCount`
    /// on an `IModule` instance will always return 0. However `IModule::getDefinedEntryPointCount`
    /// will return the number of defined entry points.
    public int GetDefinedEntryPointCount()
    {
        return _module.GetDefinedEntryPointCount();
    }

    /// Get the name of an entry point defined in the module.
    public EntryPoint GetDefinedEntryPoint(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, GetDefinedEntryPointCount(), nameof(index));
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 0, nameof(index));

        _module.GetDefinedEntryPoint(index, out IEntryPoint* entryPointPtr).Throw();

        return new EntryPoint(NativeComProxy.Create(entryPointPtr), _session);
    }

    /// Get a serialized representation of the checked module.
    public Memory<byte> Serialize()
    {
        _module.Serialize(out ISlangBlob* serializedPtr).Throw();

        return NativeComProxy.Create(serializedPtr).ReadBytes();
    }

    /// Write the serialized representation of this module to a file.
    public void WriteToFile(string fileName)
    {
        using U8Str str = U8Str.Alloc(fileName);
        _module.WriteToFile(str).Throw();
    }

    /// Get the name of the module.
    public string GetName()
    {
        return _module.GetName().String;
    }

    /// Get the path of the module.
    public string GetFilePath()
    {
        return _module.GetFilePath().String;
    }

    /// Get the unique identity of the module.
    public string GetUniqueIdentity()
    {
        return _module.GetUniqueIdentity().String;
    }

    /// Find and validate an entry point by name, even if the function is
    /// not marked with the `[shader("...")]` attribute.
    public EntryPoint FindAndCheckEntryPoint(string name, SlangStage stage, out string? diagnostics)
    {
        using U8Str str = U8Str.Alloc(name);

        _module.FindAndCheckEntryPoint(str, stage, out IEntryPoint* entryPointPtr, out ISlangBlob* diagnosticsPtr).ThrowOrDiagnose(diagnosticsPtr, out diagnostics);

        return new EntryPoint(NativeComProxy.Create(entryPointPtr), _session);
    }

    /// Get the number of dependency files that this module depends on.
    /// This includes both the explicit source files, as well as any
    /// additional files that were transitively referenced (e.g., via
    /// a `#include` directive).
    public int GetDependencyFileCount()
    {
        return _module.GetDependencyFileCount();
    }

    /// Get the path to a file this module depends on.
    public string GetDependencyFilePath(int index)
    {
        return _module.GetDependencyFilePath(index).String;
    }

    public DeclReflection GetModuleReflection()
    {
        return new DeclReflection(_module.GetModuleReflection(), this);
    }

    /** Disassemble a module.
     */
    public Memory<byte> Disassemble()
    {
        _module.Disassemble(out ISlangBlob* blobPtr).Throw();

        return NativeComProxy.Create(blobPtr).ReadBytes();
    }
}
