using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using Prowl.Graphite.Variants;
using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


/// <summary>
/// A shader compilation session, wrapping the native Slang compiler with platform-specific codegen and variant generation.
/// <para>
/// If compiling a heavy workload with a large set of shaders, it is best to reuse a single session to preserve cached loaded modules.
/// </para>
/// </summary>
public class CompilationSession
{
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
    /// The platform compilation modules registered on this session.
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

    private int _variantCap = int.MaxValue;


    /// <summary>
    /// Registers a platform module. Cannot de-register a module.
    /// </summary>
    public void RegisterModule(CompilerModule module)
    {
        _modules.Add(module);
    }


    /// <summary>
    /// Gets the index for a specific module to determine the result index of a certain platform's compiled shader in <see cref="VariantResult.Backends"/>.
    /// </summary>
    public int GetModuleIndex(CompilerModule module)
    {
        return _modules.IndexOf(module);
    }


    /// <summary>
    /// Registers a diagnostic handler delegate to read all compiler error/warning logs. If unset, uses <see cref="Console.WriteLine()"/>
    /// </summary>
    /// <param name="handler"></param>
    public void RegisterDiagnosticHandler(DiagnosticHandler handler)
    {
        _handler = handler;
    }


    /// <summary>
    /// Sets a cap on the absolute maximum amount of variants this session can generate. 
    /// Useful if you want to enforce a strict permutation limit on shader authors.
    /// </summary>
    public void SetVariantCap(int max)
    {
        _variantCap = max;
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
    }


    /// <summary>
    /// Ends this compilation session. Modules, diagnostic handlers, and variant caps are preserved if <see cref="BeginSession"/> is invoked again.
    /// </summary>
    public void EndSession()
    {
        _session = null;
    }


    /// <summary>
    /// Compiles a shader module from a filename.
    /// </summary>
    /// <param name="moduleName">The module filename to load</param>
    /// <param name="type">The shader stage to compile.</param>
    /// <exception cref="CompilationException">
    /// Thrown when the Slang shader compiler fails to compile shader code.
    /// View <see cref="CompilationException.Diagnostics"/> to diagnose compilation exceptions.
    /// </exception>
    public CompilationResult CompileShader(string moduleName, ShaderType type)
    {
        if (_session == null)
            throw new InvalidOperationException("CompileShader called before BeginSession!");

        _session.LoadModuleFromSource("VariantAttributes", "VariantAttributes.slang", s_variantModule, out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        LoadUVOriginDeclModule();

        Module module = _session.LoadModule(moduleName, out diagnostics);
        _handler.Invoke(diagnostics);

        return CompileShader(module, type);
    }


    /// <summary>
    /// Compiles a shader module from a filename, virtual path, and a user-provided raw source string.
    /// </summary>
    /// <param name="moduleName">The module filename</param>
    /// <param name="path">The module's virtual source path</param>
    /// <param name="sourceUtf8">The source string in a utf-8 memory array</param>
    /// <param name="type">The shader stage to compile.</param>
    /// <exception cref="CompilationException">
    /// Thrown when the Slang shader compiler fails to compile shader code.
    /// View <see cref="CompilationException.Diagnostics"/> to diagnose compilation exceptions.
    /// </exception>
    public CompilationResult CompileShader(string moduleName, string path, Memory<byte> sourceUtf8, ShaderType type)
    {
        if (_session == null)
            throw new InvalidOperationException("CompileShader called before BeginSession!");

        _session.LoadModuleFromSource("VariantAttributes", "VariantAttributes.slang", s_variantModule, out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        LoadUVOriginDeclModule();

        Module module = _session.LoadModuleFromSource(moduleName, path, sourceUtf8, out diagnostics);
        _handler.Invoke(diagnostics);

        return CompileShader(module, type);
    }


    private CompilationResult CompileShader(Module module, ShaderType type)
    {
        EntryPoint[] entryPoints = FindEntryPoints(module, type);

        // Get all variants and split into declarations and variant spaces
        VariantSpace[] variantFields = [.. VariantReflection.CollectVariantSpaces(_session!, module, out List<Module> affectedModules)];

        Keyword[][] variants = VariantGenerator.Generate(variantFields, _variantCap);

        // Create a composite module to link the variant specialization module to all modules affected by variant options.
        ComponentType compositeModules = _session!.CreateCompositeComponentType([.. affectedModules, .. entryPoints], out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        CompilationResult result = new()
        {
            VariantSpaces = variantFields,
            CompiledVariants = CompileVariants(compositeModules, variantFields, variants)
        };

        return result;
    }


    private static EntryPoint[] FindEntryPoints(Module module, ShaderType type)
    {
        List<(EntryPoint, EntryPointReflection)> entryPoints = [];

        bool Has(ShaderStage stage)
        {
            return entryPoints.Exists(x => x.Item2.Stage == stage);
        }

        for (uint i = 0; i < module.GetDefinedEntryPointCount(); i++)
        {
            EntryPoint entry = module.GetDefinedEntryPoint((int)i);
            EntryPointReflection ep = entry.GetLayout().EntryPoints.First();

            if (ShaderTypeExtensions.FromStage(ep.Stage) != type)
                continue;

            if (!Has(ep.Stage))
                entryPoints.Add((entry, ep));
        }


        if (type == ShaderType.Rasterization)
        {
            if (!Has(ShaderStage.Vertex) && !Has(ShaderStage.Mesh))
                throw new Exception("Missing valid vertex or mesh entrypoints for primitive stage.");

            if (!Has(ShaderStage.Fragment))
                throw new Exception("Missing valid fragment entrypoint.");

        }
        else if (type == ShaderType.Compute)
        {
            if (!Has(ShaderStage.Compute))
                throw new Exception("Missing valid compute entrypoint");
        }
        else if (type == ShaderType.Raytracing)
        {
            if (!Has(ShaderStage.RayGeneration))
                throw new Exception("Missing valid ray generation entrypoint");
        }
        else
        {
            throw new Exception($"Unknown shader type: {type}");
        }

        return [.. entryPoints.Select(x => x.Item1)];
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


    private void LoadUVOriginDeclModule()
    {
        _session!.LoadModuleFromSource("UVOrigin", "UVOrigin.slang", UVOriginDeclModule, out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);
    }


    private Module LoadUVOriginModule(bool topLeft)
    {
        string name = topLeft ? "UVOriginTopLeft" : "UVOriginBottomLeft";
        byte[] source = topLeft ? UVOriginTopLeftModule : UVOriginBottomLeftModule;

        Module loaded = _session!.LoadModuleFromSource(name, $"{name}.slang", source, out DiagnosticInfo diagnostics);
        _handler.Invoke(diagnostics);

        return loaded;
    }


    private static bool IsBackendTopLeft(GraphicsBackend backend)
        => backend is GraphicsBackend.Direct3D11 or GraphicsBackend.Vulkan;


    private VariantResult[] CompileVariants(ComponentType compositeModules, VariantSpace[] spaces, Keyword[][] variants)
    {
        VariantResult[] compiledVariants = new VariantResult[variants.Length];

        Module uvTopLeft = LoadUVOriginModule(true);
        Module uvBottomLeft = LoadUVOriginModule(false);

        for (int variant = 0; variant < variants.Length; variant++)
        {
            Module variantModule = CreateVariantModule(spaces, variants[variant]);

            (ShaderDescription Description, GraphicsBackend Backend)[] compiled = new (ShaderDescription, GraphicsBackend)[_modules.Count];

            // Link per backend so each one resolves IsUVOriginTopLeft against its hardcoded UV module.
            for (int compiler = 0; compiler < _modules.Count; compiler++)
            {
                Module uvModule = IsBackendTopLeft(_modules[compiler].Backend) ? uvTopLeft : uvBottomLeft;

                ComponentType compositeVariant = _session!.CreateCompositeComponentType([compositeModules, variantModule, uvModule], out DiagnosticInfo diagnostics);
                _handler.Invoke(diagnostics);

                ComponentType linked = compositeVariant.Link(out diagnostics);
                _handler.Invoke(diagnostics);

                compiled[compiler] = (_modules[compiler].CompileForTarget(linked, compiler, _handler), _modules[compiler].Backend);
            }

            VariantResult variantResult = new()
            {
                Variants = variants[variant],
                Backends = compiled
            };

            compiledVariants[variant] = variantResult;
        }

        return compiledVariants;
    }
}
