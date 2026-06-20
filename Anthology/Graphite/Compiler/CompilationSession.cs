using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Graphite.Shaders;
using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


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
    public struct variantAttribute
    {
        string values;
    };
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


    public ReadOnlyCollection<CompilerModule> Modules => _modules.AsReadOnly();

    private List<CompilerModule> _modules = [];

    private Session? _session;

    private DiagnosticHandler _handler = new DefaultDiagnosticHandler();

    private int _variantCap = int.MaxValue;


    public void RegisterModule(CompilerModule module)
    {
        _modules.Add(module);
    }


    public int GetModuleIndex(CompilerModule module)
    {
        return _modules.IndexOf(module);
    }


    public void RegisterDiagnosticHandler(DiagnosticHandler handler)
    {
        _handler = handler;
    }


    public void SetVariantCap(int max)
    {
        _variantCap = max;
    }


    /// <summary>
    /// Begins a compilation session. Any modules imported or loaded during compilation will remain imported or loaded until this instance is done being used.
    /// </summary>
    /// <param name="searchPaths"></param>
    /// <param name="provider"></param>
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


    public void EndSession()
    {
        _session = null;
    }


    /// <summary>
    /// Compiles yo shi for ya
    /// </summary>
    /// <param name="moduleName">The module filename to load</param>
    /// <exception cref="CompilationException">
    /// Thrown when the Slang shader compiler fails to compile shader code.
    /// View <see cref="CompilationException.Diagnostics"/> to diagnose compilation exceptions.
    /// </exception>
    public CompilationResult CompileShader(string moduleName, ShaderType type)
    {
        if (_session == null)
            throw new InvalidOperationException("CompileShader called before BeginSession!");

        // Add the module for VariantAttributes to the compilation pipeline
        _session.LoadModuleFromSource("VariantAttributes", "VariantAttributes.slang", s_variantModule, out DiagnosticInfo diagnostics);
        _handler.HandleCompilationDiagnostics(diagnostics);

        LoadUVOriginDeclModule();

        Module module = _session.LoadModule(moduleName, out diagnostics);
        return CompileShader(module, type);
    }


    /// <summary>
    /// Compiles yo shi for ya
    /// </summary>
    /// <param name="moduleName">The module filename</param>
    /// <param name="path">The module's virtual source path</param>
    /// <param name="sourceUtf8">The source string in a utf-8 memory array</param>
    /// <exception cref="CompilationException">
    /// Thrown when the Slang shader compiler fails to compile shader code.
    /// View <see cref="CompilationException.Diagnostics"/> to diagnose compilation exceptions.
    /// </exception>
    public CompilationResult CompileShader(string moduleName, string path, Memory<byte> sourceUtf8, ShaderType type)
    {
        if (_session == null)
            throw new InvalidOperationException("CompileShader called before BeginSession!");

        // Add the module for VariantAttributes to the compilation pipeline
        _session.LoadModuleFromSource("VariantAttributes", "VariantAttributes.slang", s_variantModule, out DiagnosticInfo diagnostics);
        _handler.HandleCompilationDiagnostics(diagnostics);

        LoadUVOriginDeclModule();

        Module module = _session.LoadModuleFromSource(moduleName, path, sourceUtf8, out diagnostics);
        return CompileShader(module, type);
    }


    private CompilationResult CompileShader(Module module, ShaderType type)
    {
        EntryPoint[] entryPoints = FindEntryPoints(module, type);

        // Get all variants and split into declarations and variant spaces
        VariantSpace[] variantFields = [.. GetAllModuleVariants(module, out List<Module> affectedModules)];

        Keyword[][] variants = VariantGenerator.Generate(variantFields, _variantCap);

        // Create a composite module to link the variant specialization module to all modules affected by variant options.
        ComponentType compositeModules = _session!.CreateCompositeComponentType([.. affectedModules, .. entryPoints], out DiagnosticInfo diagnostics);
        _handler.HandleCompilationDiagnostics(diagnostics);

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


    private List<VariantSpace> GetAllModuleVariants(Module requiredModule, out List<Module> linkedModules)
    {
        linkedModules = [requiredModule];

        List<(Module, string[])> moduleExterns = [];
        List<VariantSpace> moduleVariants = [];

        int loadedCount = _session!.GetLoadedModuleCount();

        // Collect all variant spaces and extern declarations
        for (int i = 0; i < loadedCount; i++)
        {
            Module loaded = _session.GetLoadedModule(i);

            DeclReflection[] decls = [.. GetExternFields(loaded)];
            string[] declNames = new string[decls.Length];

            for (int j = 0; j < decls.Length; j++)
            {
                DeclReflection decl = decls[j];
                declNames[j] = decl.Name;

                List<string> variants = GetVariantAttributes(decl.Type);

                if (variants.Count > 0)
                    moduleVariants.Add(new VariantSpace(declNames[j], decl.AsVariable().Type.FullName, variants));
            }

            moduleExterns.Add((loaded, declNames));
        }

        // Scan for modules that require a linked extern declaration
        foreach ((Module module, string[] externDecls) in moduleExterns)
        {
            for (int decl = 0; decl < externDecls.Length; decl++)
            {
                string declName = externDecls[decl];
                for (int variant = 0; variant < moduleVariants.Count; variant++)
                {
                    if (declName == moduleVariants[variant].Name)
                    {
                        if (!module.Equals(requiredModule))
                            linkedModules.Add(module);
                        goto ContinueOuter;
                    }
                }
            }

        ContinueOuter:
            continue;
        }

        return moduleVariants;
    }


    private static IEnumerable<DeclReflection> GetExternFields(Module module)
    {
        DeclReflection moduleReflection = module.GetModuleReflection();
        foreach (DeclReflection child in moduleReflection.GetChildrenOfKind(DeclKind.Variable))
        {
            if (!child.AsVariable().HasModifier(ModifierID.Extern))
                continue;

            yield return child;
        }
    }


    private Module CreateVariantModule(VariantSpace[] spaces, Keyword[] variants)
    {
        StringBuilder sb = new();

        // The session caches loaded modules by name, so each permutation needs a distinct module
        // name; otherwise every variant after the first reuses the first variant's constants.
        string name = "__Variant_" + string.Join("_", variants.Select(v => v.ValueId));

        sb.AppendLine($"module {name};");

        for (int i = 0; i < spaces.Length; i++)
        {
            VariantSpace target = spaces[i];
            Keyword variant = variants[i];
            sb.AppendLine($"export public static const {target.DeclType} {target.Name} = {variant.Value};");
        }

        string variantModule = sb.ToString();

        Module loaded = _session!.LoadModuleFromSourceString(name, $"{name}.slang", variantModule, out DiagnosticInfo diagnostics);
        _handler.HandleCompilationDiagnostics(diagnostics);

        return loaded;
    }


    private static List<string> GetVariantAttributes(TypeReflection reflection)
    {
        List<string> variants = [];

        foreach (Slang.Attribute userAttribute in reflection.UserAttributes)
        {
            if (userAttribute.Name == "variant")
            {
                string? value = userAttribute.GetArgumentValueString(0);

                if (value == null)
                    continue;

                variants.Add(value);
            }
        }

        return variants;
    }


    private void LoadUVOriginDeclModule()
    {
        _session!.LoadModuleFromSource("UVOrigin", "UVOrigin.slang", UVOriginDeclModule, out DiagnosticInfo diagnostics);
        _handler.HandleCompilationDiagnostics(diagnostics);
    }


    private Module LoadUVOriginModule(bool topLeft)
    {
        string name = topLeft ? "UVOriginTopLeft" : "UVOriginBottomLeft";
        byte[] source = topLeft ? UVOriginTopLeftModule : UVOriginBottomLeftModule;

        Module loaded = _session!.LoadModuleFromSource(name, $"{name}.slang", source, out DiagnosticInfo diagnostics);
        _handler.HandleCompilationDiagnostics(diagnostics);

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
                _handler.HandleCompilationDiagnostics(diagnostics);

                ComponentType linked = compositeVariant.Link(out diagnostics);
                _handler.HandleCompilationDiagnostics(diagnostics);

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
