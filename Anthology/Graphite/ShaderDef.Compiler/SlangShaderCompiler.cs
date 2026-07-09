using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Graphite.ShaderDef;
using Prowl.Slang;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// A Slang-based <see cref="IShaderCompiler"/>. Wraps the native Slang compiler with platform-specific
/// codegen and variant discovery, slotting into a <see cref="ShaderDefinition"/> to supply and compile
/// variants on demand or all at once.
/// <para>
/// If compiling a heavy workload with a large set of shaders, it is best to reuse a single compiler to
/// preserve cached loaded modules.
/// </para>
/// </summary>
public sealed class SlangShaderCompiler : IShaderCompiler
{
    private sealed class Prepared
    {
        public required VariantSpace[] Axes;
        public required ComponentType Composite;
    }


    private class FileProvider : IFileProvider
    {
        public required Func<string, Memory<byte>?> Provider;

        public Memory<byte>? LoadFile(string path)
            => Provider.Invoke(path);
    }


    private static FileProvider s_defaultProvider = new()
    {
        Provider = (x) =>
        {
            if (!File.Exists(x))
                return null;

            return File.ReadAllBytes(x);
        }
    };


    private static byte[] s_variantModule =
    """
    module VariantAttributes;

    [__AttributeUsage(_AttributeTargets.Var)]
    public struct VariantAxisAttribute { }
    """u8.ToArray();


    // Always loaded so user shaders can `import UVOrigin` and read IsUVOriginTopLeft. The extern is
    // resolved at link time by one of the hardcoded implementation modules below, chosen per backend.
    private static byte[] UVOriginDeclModule =
    """
    module UVOrigin;
    extern public static const bool IsUVOriginTopLeft;
    """u8.ToArray();

    private static byte[] UVOriginTopLeftModule =
    """
    module UVOriginTopLeft;
    export public static const bool IsUVOriginTopLeft = true;
    """u8.ToArray();

    private static byte[] UVOriginBottomLeftModule =
    """
    module UVOriginBottomLeft;
    export public static const bool IsUVOriginTopLeft = false;
    """u8.ToArray();


    /// <summary>
    /// The platform compilation modules registered on this compiler.
    /// </summary>
    public ReadOnlyCollection<CompilerModule> Modules => _modules.AsReadOnly();

    private List<CompilerModule> _modules = [];

    private Session? _session;

    private DiagnosticHandler _handler = (x) =>
    {
        if (string.IsNullOrWhiteSpace(x.Message))
            return;

        Console.WriteLine(x.Message);
    };

    private Dictionary<ShaderPass, Prepared> _prepared = new();
    private Module? _uvTopLeft;
    private Module? _uvBottomLeft;
    private bool _commonLoaded;
    private int _passCounter;


    /// <summary>
    /// Registers a platform module. Cannot de-register a module.
    /// </summary>
    public void RegisterModule(CompilerModule module)
    {
        _modules.Add(module);
    }


    /// <summary>
    /// Gets the index for a specific module in the registered module list.
    /// </summary>
    public int GetModuleIndex(CompilerModule module)
    {
        return _modules.IndexOf(module);
    }


    /// <summary>
    /// Registers a diagnostic handler delegate to read all compiler error/warning logs. If unset, uses <see cref="Console.WriteLine()"/>
    /// </summary>
    public void RegisterDiagnosticHandler(DiagnosticHandler handler)
    {
        _handler = handler;
    }


    /// <summary>
    /// Begins a compilation session. Any modules imported or loaded during compilation will remain imported or loaded until this instance is done being used.
    /// </summary>
    /// <param name="searchPaths">Directories searched when resolving imported modules.</param>
    /// <param name="provider">Optional callback that supplies module source bytes for a given path.</param>
    /// <param name="pragmas">Optional preprocessor macro name/value pairs applied to the session.</param>
    public void BeginSession(DirectoryInfo[] searchPaths, Func<string, Memory<byte>?>? provider = null, (string, string)[]? pragmas = null)
    {
        SessionDescription sessionDesc = new()
        {
            Targets = [.. _modules.Select(x => x.Target)],
            SearchPaths = [.. searchPaths.Select(x => x.FullName)],
            FileProvider = provider != null ? new FileProvider() { Provider = provider } : s_defaultProvider,
            DefaultMatrixLayoutMode = MatrixLayoutMode.ColumnMajor
        };

        if (pragmas != null)
            sessionDesc.PreprocessorMacros = pragmas.Select(x => new PreprocessorMacroDescription() { Name = x.Item1, Value = x.Item2 }).ToArray();

        _session = GlobalSession.CreateSession(sessionDesc);
        ResetSessionState();
    }


    /// <summary>
    /// Ends this compilation session. Modules and diagnostic handlers are preserved if <see cref="BeginSession"/> is invoked again.
    /// </summary>
    public void EndSession()
    {
        _session = null;
        ResetSessionState();
    }


    /// <inheritdoc/>
    public IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass)
    {
        return Prepare(pass).Axes;
    }


    /// <inheritdoc/>
    public ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)
    {
        Prepared prepared = Prepare(pass);
        CompilerModule module = ModuleFor(backend, out int layoutIndex);

        Module variantModule = CreateVariantModule(prepared.Axes, combo);
        Module uvModule = IsBackendTopLeft(backend) ? UvModule(true) : UvModule(false);

        ComponentType composite = _session!.CreateCompositeComponentType([prepared.Composite, variantModule, uvModule], out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        ComponentType linked = composite.Link(out diagnostics);
        _handler.Invoke(diagnostics);

        return module.CompileForTarget(linked, layoutIndex, _handler);
    }


    private Prepared Prepare(ShaderPass pass)
    {
        if (_session == null)
            throw new InvalidOperationException("Compile called before BeginSession!");

        if (_prepared.TryGetValue(pass, out Prepared? existing))
            return existing;

        LoadCommonModules();

        string name = !string.IsNullOrEmpty(pass.Name) ? pass.Name : "pass_" + _passCounter++;
        byte[] utf8 = Encoding.UTF8.GetBytes(pass.InlineSlang);

        Module module = _session.LoadModuleFromSource(name, name + ".slang", utf8, out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        EntryPoint[] entryPoints = FindEntryPoints(module);

        VariantSpace[] axes = [.. VariantReflection.CollectVariantSpaces(_session, module, out List<Module> affectedModules)];

        ComponentType composite = _session.CreateCompositeComponentType([.. affectedModules, .. entryPoints], out diagnostics);
        _handler.Invoke(diagnostics);

        Prepared prepared = new() { Axes = axes, Composite = composite };
        _prepared[pass] = prepared;
        return prepared;
    }


    private static EntryPoint[] FindEntryPoints(Module module)
    {
        List<(EntryPoint Entry, EntryPointReflection Reflection)> entryPoints = [];

        bool Has(ShaderStage stage)
        {
            return entryPoints.Exists(x => x.Reflection.Stage == stage);
        }

        for (uint i = 0; i < module.GetDefinedEntryPointCount(); i++)
        {
            EntryPoint entry = module.GetDefinedEntryPoint((int)i);
            EntryPointReflection ep = entry.GetLayout().EntryPoints.First();

            if (!Has(ep.Stage))
                entryPoints.Add((entry, ep));
        }

        if (entryPoints.Count == 0)
            throw new Exception("Shader pass contains no entrypoints.");

        return [.. entryPoints.Select(x => x.Entry)];
    }


    private CompilerModule ModuleFor(GraphicsBackend backend, out int layoutIndex)
    {
        for (int i = 0; i < _modules.Count; i++)
        {
            if (_modules[i].Backend == backend)
            {
                layoutIndex = i;
                return _modules[i];
            }
        }

        throw new InvalidOperationException($"No compiler module registered for backend {backend}.");
    }


    private Module CreateVariantModule(VariantSpace[] spaces, Keyword[] variants)
    {
        // The session caches loaded modules by name, so each permutation needs a distinct module
        // name; otherwise every variant after the first reuses the first variant's constants.
        string name = "__Variant_" + string.Join("_", variants.Select(v => v.ValueId));

        string variantModule = VariantGenerator.BuildSpecializationModule(name, spaces, variants);

        Module loaded = _session!.LoadModuleFromSourceString(name, $"{name}.slang", variantModule, out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        return loaded;
    }


    private void LoadCommonModules()
    {
        if (_commonLoaded)
            return;

        _session!.LoadModuleFromSource("VariantAttributes", "VariantAttributes.slang", s_variantModule, out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        _session!.LoadModuleFromSource("UVOrigin", "UVOrigin.slang", UVOriginDeclModule, out diagnostics);
        _handler.Invoke(diagnostics);

        _commonLoaded = true;
    }


    private Module UvModule(bool topLeft)
    {
        if (topLeft && _uvTopLeft != null)
            return _uvTopLeft;

        if (!topLeft && _uvBottomLeft != null)
            return _uvBottomLeft;

        string name = topLeft ? "UVOriginTopLeft" : "UVOriginBottomLeft";
        byte[] source = topLeft ? UVOriginTopLeftModule : UVOriginBottomLeftModule;

        Module loaded = _session!.LoadModuleFromSource(name, $"{name}.slang", source, out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        if (topLeft)
            _uvTopLeft = loaded;
        else
            _uvBottomLeft = loaded;

        return loaded;
    }


    private static bool IsBackendTopLeft(GraphicsBackend backend)
        => backend is GraphicsBackend.Direct3D11 or GraphicsBackend.Vulkan;


    private void ResetSessionState()
    {
        _prepared.Clear();
        _uvTopLeft = null;
        _uvBottomLeft = null;
        _commonLoaded = false;
        _passCounter = 0;
    }
}
