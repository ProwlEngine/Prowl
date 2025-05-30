// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Slang.Native;


namespace Prowl.Slang;


/// <summary>
/// <para>
/// A module is the granularity of shader code compilation and loading.
/// </para>
/// <para>
/// In most cases a module corresponds to a single compile "translation unit."
/// This will often be a single `.slang` or `.hlsl` file and everything it
/// `#include`s.
/// </para>
/// <para>
/// Notably, a module `M` does *not* include the things it `import`s, as these
/// as distinct modules that `M` depends on. There is a directed graph of
/// module dependencies, and all modules in the graph must belong to the
/// same session (`ISession`).
/// </para>
/// <para>
/// A module establishes a namespace for looking up types, functions, etc.
/// </para>
/// </summary>
public unsafe class Module : ComponentType
{
    internal IModule _module;


    internal Module(IModule module, Session session) : base(module, session)
    {
        _module = module;
    }


    /// <summary>
    /// Find and an entry point by name.
    /// Note that this does not work in case the function is not explicitly designated as an entry
    /// point, e.g. using a `[shader("...")]` attribute. In such cases, consider using
    /// <see cref="FindAndCheckEntryPoint"/> instead.
    /// </summary>
    public EntryPoint FindEntryPointByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);

        _module.FindEntryPointByName(str, out IEntryPoint* entryPointPtr)
            .Throw($"Failed to find matching entrypoint `{name}`");

        return new EntryPoint(NativeComProxy.Create(entryPointPtr), _session);
    }


    /// <summary>
    /// Get number of entry points defined in the module. An entry point defined in a module
    /// is by default not included in the linkage, so calls to ComponentType.GetEntryPointCount
    /// on an `Module` instance will always return 0. However <see cref="GetDefinedEntryPointCount"/>
    /// will return the number of defined entry points.
    /// </summary>
    public int GetDefinedEntryPointCount()
    {
        return _module.GetDefinedEntryPointCount();
    }


    /// <summary>
    /// Get the name of an entry point defined in the module.
    /// </summary>
    public EntryPoint GetDefinedEntryPoint(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, GetDefinedEntryPointCount(), nameof(index));
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 0, nameof(index));

        _module.GetDefinedEntryPoint(index, out IEntryPoint* entryPointPtr)
            .Throw($"Failed to get entry point at index '{index}'");

        return new EntryPoint(NativeComProxy.Create(entryPointPtr), _session);
    }


    /// <summary>
    /// Get a serialized representation of the checked module.
    /// </summary>
    public Memory<byte> Serialize()
    {
        _module.Serialize(out ISlangBlob* serializedPtr)
            .Throw("Failed to serialize module");

        return NativeComProxy.Create(serializedPtr).ReadBytes();
    }


    /// <summary>
    /// Write the serialized representation of this module to a file.
    /// </summary>
    public void WriteToFile(string fileName)
    {
        using U8Str str = U8Str.Alloc(fileName);

        _module.WriteToFile(str)
            .Throw("Failed to write module to file");
    }


    /// <summary>
    /// Get the name of the module.
    /// </summary>
    public string GetName()
    {
        return _module.GetName().String;
    }


    /// <summary>
    /// Get the path of the module.
    /// </summary>
    public string GetFilePath()
    {
        return _module.GetFilePath().String;
    }


    /// <summary>
    /// Get the unique identity of the module.
    /// </summary>
    public string GetUniqueIdentity()
    {
        return _module.GetUniqueIdentity().String;
    }


    /// <summary>
    /// Find and validate an entry point by name, even if the function is
    /// not marked with the `[shader("...")]` attribute.
    /// </summary>
    public EntryPoint FindAndCheckEntryPoint(string name, ShaderStage stage, out DiagnosticInfo diagnostics)
    {
        using U8Str str = U8Str.Alloc(name);

        _module.FindAndCheckEntryPoint(str, stage, out IEntryPoint* entryPointPtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return new EntryPoint(NativeComProxy.Create(entryPointPtr), _session);
    }


    /// <summary>
    /// Get the number of dependency files that this module depends on.
    /// This includes both the explicit source files, as well as any
    /// additional files that were transitively referenced (e.g., via
    /// a `#include` directive).
    /// </summary>
    public int GetDependencyFileCount()
    {
        return _module.GetDependencyFileCount();
    }


    /// <summary>
    /// Get the path to a file this module depends on.
    /// </summary>
    public string GetDependencyFilePath(int index)
    {
        return _module.GetDependencyFilePath(index).String;
    }


    /// <summary>
    /// Gets the reflection information for this module.
    /// </summary>
    public DeclReflection GetModuleReflection()
    {
        return new DeclReflection(_module.GetModuleReflection(), this);
    }


    /// <summary>
    /// Disassemble a module.
    /// </summary>
    public Memory<byte> Disassemble()
    {
        _module.Disassemble(out ISlangBlob* blobPtr)
            .Throw("Failed to disassemble module");

        return NativeComProxy.Create(blobPtr).ReadBytes();
    }
}
