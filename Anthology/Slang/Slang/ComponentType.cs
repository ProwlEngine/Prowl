// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Slang.Native;


namespace Prowl.Slang;


/// <summary>
/// <para>
/// A component type is a unit of shader code that can be included into
/// a linked and compiled shader program.
/// </para>
/// <para>
/// Each component type may have:
/// </para>
/// <list type="bullet">
/// <item>
///  Zero or more uniform shader parameters, representing textures,
///  buffers, etc.that the code in the component depends on.
/// </item>
/// <item>
///  Zero or more *specialization* parameters, which are type or
///  value parameters that can be used to synthesize specialized
///  versions of the component type.
/// </item>
/// <item>
///  Zero or more entry points, which are the individually invocable
///  kernels that can have final code generated.
/// </item>
/// <item>
///  Zero or more *requirements*, which are other component
///  types on which the component type depends.
/// </item>
/// </list>
/// <para>
/// One example of a component type is a module of Slang code:
/// </para>
/// <list type="bullet">
/// <item>
///  The global-scope shader parameters declared in the module are
///  the parameters when considered as a component type.
/// </item>
/// <item>
///  Any global-scope generic or interface type parameters introduce
///  specialization parameters for the module.
/// </item>
/// <item>
///  A module does not by default include any entry points when
///  considered as a component type (although the code of the
///  module might *declare* some entry points).
/// </item>
/// <item>
///  Any other modules that are `import`ed in the source code
///  become requirements of the module, when considered as a
///  component type.
/// </item>
/// </list>
/// <para>
/// An entry point is another example of a component type:
/// </para>
/// <list type="bullet">
/// <item>
///  The `uniform` parameters of the entry point function are
///  its shader parameters when considered as a component type.
/// </item>
/// <item>
///  Any generic or interface-type parameters of the entry point
///  introduce specialization parameters.
/// </item>
/// <item>
///  An entry point component type exposes a single entry point(itself).
/// </item>
/// <item>
///  An entry point has one requirement for the module in which
///  it was defined.
/// </item>
/// </list>
/// <para>
/// Component types can be manipulated in a few ways:
/// </para>
/// <list type="bullet">
/// <item>
///  Multiple component types can be combined into a composite, which
///  combines all of their code, parameters, etc.
/// </item>
/// <item>
///  A component type can be specialized, by "plugging in" types and
///  values for its specialization parameters.
/// </item>
/// <item>
///  A component type can be laid out for a particular target, giving
///  offsets/bindings to the shader parameters it contains.
/// </item>
/// <item>
///  Generated kernel code can be requested for entry points.
/// </item>
/// </list>
/// </summary>
public unsafe class ComponentType
{
    // Components depend on a Session instance, but sessions dont depend on components.
    // Keep a ref to the session to prevent its underlying native ptr from being disposed while this component still exists.
    internal Session _session;
    internal IComponentType _componentType;


    internal ComponentType(IComponentType componentType, Session session)
    {
        _session = session;
        _componentType = componentType;
    }


    /// <summary>
    /// Get the runtime session that this component type belongs to.
    /// </summary>
    public Session GetSession()
    {
        return _session;
    }


    /// <summary>
    /// <para>
    /// Get the layout for this program for the chosen `targetIndex`.
    /// </para>
    /// <para>
    /// The resulting layout will establish offsets/bindings for all
    /// of the global and entry-point shader parameters in the
    /// component type.
    /// </para>
    /// <para>
    /// If this component type has specialization parameters (that is,
    /// it is not fully specialized), then the resulting layout may
    /// be incomplete, and plugging in arguments for generic specialization
    /// parameters may result in a component type that doesn't have
    /// a compatible layout. If the component type only uses
    /// interface-type specialization parameters, then the layout
    /// for a specialization should be compatible with an unspecialized
    /// layout(all parameters in the unspecialized layout will have
    /// the same offset/binding in the specialized layout).
    /// </para>
    /// <para>
    /// If this component type is combined into a composite, then
    /// the absolute offsets/bindings of parameters may not stay the same.
    /// If the shader parameters in a component type don't make
    /// use of explicit binding annotations(e.g., `register(...)`),
    /// then the *relative* offset of shader parameters will stay
    /// the same when it is used in a composition.
    /// </para>
    /// </summary>
    public ShaderReflection GetLayout(nint targetIndex, out DiagnosticInfo diagnostics)
    {
        Native.ShaderReflection* reflectionPtr = _componentType.GetLayout(targetIndex, out ISlangBlob* diagnosticsPtr);

        Utility.ValidatePtr(reflectionPtr, diagnosticsPtr, out diagnostics);

        return new ShaderReflection(reflectionPtr, this);
    }

    /// <inheritdoc cref="GetLayout(nint, out DiagnosticInfo)"/>
    public ShaderReflection GetLayout()
    {
        return GetLayout(0, out _);
    }

    /// <summary>
    /// Get the number of (unspecialized) specialization parameters for the component type.
    /// </summary>
    public nint GetSpecializationParamCount()
    {
        return _componentType.GetSpecializationParamCount();
    }


    /// <summary>
    /// <para>
    /// Get the compiled code for the entry point at `entryPointIndex` for the chosen `targetIndex`
    /// </para>
    /// <para>
    /// Entry point code can only be computed for a component type that
    /// has no specialization parameters(it must be fully specialized)
    /// and that has no requirements(it must be fully linked).
    /// </para>
    /// <para>
    /// If code has not already been generated for the given entry point and target,
    /// then a compilation error may be detected, in which case `outDiagnostics`
    /// (if non-null) will be filled in with a blob of messages diagnosing the error.
    /// </para>
    /// </summary>
    public Memory<byte> GetEntryPointCode(nint entryPointIndex, nint targetIndex, out DiagnosticInfo diagnostics)
    {
        _componentType.GetEntryPointCode(entryPointIndex, targetIndex, out ISlangBlob* codePtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return NativeComProxy.Create(codePtr).ReadBytes();
    }


    /* Get the compilation result as a file system.

    Has the same requirements as getEntryPointCode.

    The result is not written to the actual OS file system, but is made available as an
    in memory representation.
    */

    // SlangResult GetResultAsFileSystem(
    //     nint entryPointIndex,
    //     nint targetIndex,
    //     out ISlangMutableFileSystem* outFileSystem)
    // {
    // }

    /// <summary>
    /// <para>
    /// Compute a hash for the entry point at `entryPointIndex` for the chosen `targetIndex`.
    /// </para>
    /// <para>
    /// This computes a hash based on all the dependencies for this component type as well as the
    /// target settings affecting the compiler backend. The computed hash is used as a key for caching
    /// the output of the compiler backend to implement shader caching.
    /// </para>
    /// </summary>
    public Memory<byte> GetEntryPointHash(int entryPointIndex, int targetIndex)
    {
        _componentType.GetEntryPointHash(entryPointIndex, targetIndex, out ISlangBlob* outHash);

        return NativeComProxy.Create(outHash).ReadBytes();
    }


    /// <summary>
    /// <para>
    /// Specialize the component by binding its specialization parameters to concrete arguments.
    /// </para>
    /// <para>
    /// If any diagnostics (error or warnings) are produced, they will be written to `outDiagnostics`.
    /// </para>
    /// </summary>
    public ComponentType Specialize(TypeReflection[] specializationArgs, out DiagnosticInfo diagnostics)
    {
        SpecializationArg* specializationArgsPtr = stackalloc SpecializationArg[specializationArgs.Length];

        for (int i = 0; i < specializationArgs.Length; i++)
            specializationArgsPtr[i] = SpecializationArg.FromType(specializationArgs[i]._ptr);

        _componentType.Specialize(specializationArgsPtr, specializationArgs.Length, out IComponentType* componentPtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return new ComponentType(NativeComProxy.Create(componentPtr), _session);
    }


    /// <summary>
    /// <para>
    /// Link this component type against all of its unsatisfied dependencies.
    /// </para>
    /// <para>
    /// A component type may have unsatisfied dependencies.For example, a module
    /// depends on any other modules it `import`s, and an entry point depends
    /// on the module that defined it.
    /// </para>
    /// <para>
    /// A user can manually satisfy dependencies by creating a composite
    /// component type, and when doing so they retain full control over
    /// the relative ordering of shader parameters in the resulting layout.
    /// </para>
    /// <para>
    /// It is an error to try to generate/access compiled kernel code for
    /// a component type with unresolved dependencies, so if dependencies
    /// remain after whatever manual composition steps an application
    /// cares to perform, the `link()` function can be used to automatically
    /// compose in any remaining dependencies.The order of parameters
    /// (and hence the global layout) that results will be deterministic,
    /// but is not currently documented.
    /// </para>
    /// </summary>
    public ComponentType Link(out DiagnosticInfo diagnostics)
    {
        _componentType.Link(out IComponentType* componentPtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return new ComponentType(NativeComProxy.Create(componentPtr), _session);
    }


    /// <summary>
    /// <para>
    /// Get entry point 'callable' functions accessible through the ISlangSharedLibrary interface.
    /// </para>
    /// <para>
    /// The functions remain in scope as long as the ISlangSharedLibrary interface is in scope.
    /// </para>
    /// <remarks>
    /// Requires a compilation target of SLANG_HOST_CALLABLE.
    /// </remarks>
    /// </summary>
    internal SharedLibrary GetEntryPointHostCallable(int entryPointIndex, int targetIndex, out DiagnosticInfo diagnostics)
    {
        _componentType.GetEntryPointHostCallable(entryPointIndex, targetIndex, out ISlangSharedLibrary* sharedLibPtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return new SharedLibrary(NativeComProxy.Create(sharedLibPtr));
    }

    /// <summary>
    /// <para>
    /// Get a new ComponentType object that represents a renamed entry point.
    /// </para>
    /// <para>
    /// The current object must be a single EntryPoint, or a CompositeComponentType or
    /// SpecializedComponentType that contains one EntryPoint component.
    /// </para>
    /// </summary>
    public ComponentType RenameEntryPoint(string newName)
    {
        using U8Str str = U8Str.Alloc(newName);

        _componentType.RenameEntryPoint(str, out IComponentType* entryPointPtr)
            .Throw($"Failed to rename entrypoint to '{newName}'");

        return new ComponentType(NativeComProxy.Create(entryPointPtr), _session);
    }


    /// <summary>
    /// Link and specify additional compiler options when generating code
    /// from the linked program.
    /// </summary>
    public ComponentType LinkWithOptions(CompilerOptionEntry[] compilerOptionEntries, out DiagnosticInfo diagnostics)
    {
        Native.CompilerOptionEntry* compilerOptionEntriesPtr = stackalloc Native.CompilerOptionEntry[compilerOptionEntries.Length];

        for (int i = 0; i < compilerOptionEntries.Length; i++)
            compilerOptionEntriesPtr[i].Allocate(compilerOptionEntries[i]);

        _componentType.LinkWithOptions(out IComponentType* linkedComponentPtr, (uint)compilerOptionEntries.Length, compilerOptionEntriesPtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        for (int i = 0; i < compilerOptionEntries.Length; i++)
            compilerOptionEntriesPtr[i].Free();

        return new ComponentType(NativeComProxy.Create(linkedComponentPtr), _session);
    }


    /// <summary>
    /// Gets the compiled code for the target at a given index.
    /// </summary>
    public Memory<byte> GetTargetCode(int targetIndex, out DiagnosticInfo diagnostics)
    {
        _componentType.GetTargetCode(targetIndex, out ISlangBlob* codePtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return NativeComProxy.Create(codePtr).ReadBytes();
    }


    /// <summary>
    /// Gets the metadata information for the target at a given index.
    /// </summary>
    public Metadata GetTargetMetadata(int targetIndex, out DiagnosticInfo diagnostics)
    {
        _componentType.GetTargetMetadata(targetIndex, out IMetadata* metadataPtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return new Metadata(NativeComProxy.Create(metadataPtr));
    }


    /// <summary>
    /// Gets the metadata information for the specified entrypoint and target at a given index.
    /// </summary>
    public Metadata GetEntryPointMetadata(int entryPointIndex, int targetIndex, out DiagnosticInfo diagnostics)
    {
        _componentType.GetEntryPointMetadata(entryPointIndex, targetIndex, out IMetadata* metadataPtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return new Metadata(NativeComProxy.Create(metadataPtr));
    }
}
