using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Slang;

namespace SlangQuickCompile;


// Raw-Slang baseline generator for the compiler test suite. Compiles the shared .slang shaders under
// Tests/Compiler/Shaders to GLSL / HLSL / SPIR-V using Prowl.Slang directly (no dependency on
// Prowl.Graphite.Compiler), writing the per-stage results into Tests/Compiler/KnownGood and printing
// a reflection table per target. Because it shares nothing with the compiler it verifies, a match
// against its output is an independent regression lock rather than a tautology.
//
// Usage (from the repo root, or any subdirectory):
//   dotnet run --project Tools/SlangQuickCompile -- [--dump] [--write] [shaderName ...]
//     --dump    print the reflection table for each shader/target (default when no flag given)
//     --write   (re)write the KnownGood output files
//   With no shader names every shader in the manifest is processed.
internal static class Program
{
    // A target backend: the output file extension plus the Slang profile and format that produce it.
    sealed record Target(string Extension, string Profile, CompileTarget Format);

    // One specialized permutation of a shader. ConstLines define any `extern` constants the shader
    // needs to link (variant specialization); Suffix disambiguates the output file name.
    sealed record Permutation(string Suffix, string[] ConstLines)
    {
        public static readonly Permutation Default = new("", []);
    }

    sealed record ShaderJob(string Module, Target[] Targets, Permutation[] Permutations);


    static readonly Target s_glsl = new("glsl", "glsl_450", CompileTarget.Glsl);
    static readonly Target s_hlsl = new("hlsl", "sm_5_0", CompileTarget.Hlsl);
    static readonly Target s_spirv = new("spv", "spirv_1_5", CompileTarget.Spirv);

    static readonly Target[] s_allTargets = [s_glsl, s_hlsl, s_spirv];


    static readonly ShaderJob[] s_manifest =
    [
        new("Graphics", s_allTargets, [Permutation.Default]),
        new("Modules", s_allTargets, [Permutation.Default]),
        new("ConstantBuffers", s_allTargets, [Permutation.Default]),
        new("ParameterBlocks", s_allTargets, [Permutation.Default]),
        new("Variants", s_allTargets,
        [
            new("_false", ["export public static const bool DoubleColor = false;"]),
            new("_true", ["export public static const bool DoubleColor = true;"]),
        ]),
    ];


    static int Main(string[] args)
    {
        bool write = args.Contains("--write");
        bool dump = args.Contains("--dump") || !write;
        string[] names = [.. args.Where(a => !a.StartsWith("--"))];

        string shaderDir = LocateDirectory("Tests/Compiler/Shaders");
        string knownGoodDir = LocateDirectory("Tests/Compiler/KnownGood");

        Console.WriteLine($"Shaders:   {shaderDir}");
        Console.WriteLine($"KnownGood: {knownGoodDir}\n");

        foreach (ShaderJob job in s_manifest)
        {
            if (names.Length > 0 && !names.Contains(job.Module))
                continue;

            foreach (Target target in job.Targets)
                foreach (Permutation permutation in job.Permutations)
                    Process(job, target, permutation, shaderDir, knownGoodDir, write, dump);
        }

        return 0;
    }


    static void Process(
        ShaderJob job, Target target, Permutation permutation,
        string shaderDir, string knownGoodDir, bool write, bool dump)
    {
        Session session = CreateSession(target, shaderDir);

        // VariantAttributes provides the [variant(...)] attribute the variant shader imports.
        session.LoadModuleFromSourceString("VariantAttributes", "VariantAttributes.slang", VariantAttributesModule, out _);

        Module module = session.LoadModule(job.Module, out DiagnosticInfo diagnostics);
        Report(diagnostics);

        EntryPoint[] entryPoints = DefinedEntryPoints(module);

        List<ComponentType> parts = [module, .. entryPoints];

        if (permutation.ConstLines.Length > 0)
            parts.Add(SpecializationModule(session, job.Module + permutation.Suffix, permutation.ConstLines));

        ComponentType composite = session.CreateCompositeComponentType([.. parts], out diagnostics);
        Report(diagnostics);

        ComponentType linked = composite.Link(out diagnostics);
        Report(diagnostics);

        ShaderReflection layout = linked.GetLayout(0, out _);

        string label = $"{job.Module}{permutation.Suffix} -> {target.Extension}";

        for (uint i = 0; i < layout.EntryPointCount; i++)
        {
            EntryPointReflection ep = layout.GetEntryPointByIndex(i);
            Memory<byte> code = linked.GetEntryPointCode((nint)i, 0, out diagnostics);
            Report(diagnostics);

            string fileName = $"{job.Module}.{StageName(ep.Stage)}{permutation.Suffix}.{target.Extension}";

            if (write)
            {
                byte[] bytes = target.Format == CompileTarget.Hlsl
                    ? Encoding.UTF8.GetBytes(NormalizeSourcePaths(Encoding.UTF8.GetString(code.Span)))
                    : code.ToArray();

                File.WriteAllBytes(Path.Combine(knownGoodDir, fileName), bytes);
                Console.WriteLine($"  wrote {fileName} ({bytes.Length} bytes)");
            }
        }

        if (dump)
        {
            Console.WriteLine($"== {label} ==");
            DumpParameters(layout);
            Console.WriteLine();
        }
    }


    static Session CreateSession(Target target, string shaderDir)
    {
        SessionDescription description = new()
        {
            Targets =
            [
                new TargetDescription { Profile = GlobalSession.FindProfile(target.Profile), Format = target.Format }
            ],
            SearchPaths = [shaderDir],
            DefaultMatrixLayoutMode = MatrixLayoutMode.ColumnMajor,
        };

        return GlobalSession.CreateSession(description);
    }


    static EntryPoint[] DefinedEntryPoints(Module module)
    {
        List<EntryPoint> entryPoints = [];

        for (int i = 0; i < module.GetDefinedEntryPointCount(); i++)
            entryPoints.Add(module.GetDefinedEntryPoint(i));

        return [.. entryPoints];
    }


    static Module SpecializationModule(Session session, string name, string[] constLines)
    {
        string moduleName = "__Spec_" + name.Replace("_", "");

        StringBuilder sb = new();
        sb.AppendLine($"module {moduleName};");
        foreach (string line in constLines)
            sb.AppendLine(line);

        Module module = session.LoadModuleFromSourceString(moduleName, $"{moduleName}.slang", sb.ToString(), out DiagnosticInfo diagnostics);
        Report(diagnostics);
        return module;
    }


    // Walks the program's global parameters, descending into parameter blocks, and prints the binding
    // each resource receives for the current target. The category offsets are exactly what the backend
    // reflectors read, so this is the ground-truth view for authoring expected layouts.
    static void DumpParameters(ShaderReflection layout)
    {
        foreach (VariableLayoutReflection parameter in layout.Parameters)
            DumpParameter(parameter, indent: "  ");
    }


    static void DumpParameter(VariableLayoutReflection parameter, string indent)
    {
        TypeLayoutReflection typeLayout = parameter.TypeLayout;

        StringBuilder categories = new();
        for (uint c = 0; c < parameter.CategoryCount; c++)
        {
            ParameterCategory category = parameter.GetCategoryByIndex(c);
            categories.Append($" {category}(offset={parameter.GetOffset(category)},space={parameter.GetBindingSpace(category)})");
        }

        Console.WriteLine($"{indent}{parameter.Name,-16} kind={typeLayout.Kind,-16} size={typeLayout.GetSize(),-4}{categories}");

        if (typeLayout.Kind == TypeKind.ConstantBuffer)
            DumpUniformFields(typeLayout.ElementTypeLayout, indent + "    ");

        if (typeLayout.Kind == TypeKind.ParameterBlock)
        {
            TypeLayoutReflection element = typeLayout.ElementTypeLayout;
            Console.WriteLine($"{indent}  [block container] cbufferOffset(slot)={typeLayout.ContainerVarLayout.GetOffset(ParameterCategory.DescriptorTableSlot)} elementSize={element.GetSize()}");
            DumpUniformFields(element, indent + "    ");

            foreach (VariableLayoutReflection field in element.Fields)
                if (field.TypeLayout.Kind is TypeKind.Resource or TypeKind.SamplerState or TypeKind.ParameterBlock)
                    DumpParameter(field, indent + "    ");
        }
    }


    static void DumpUniformFields(TypeLayoutReflection block, string indent)
    {
        foreach (VariableLayoutReflection field in block.Fields)
        {
            TypeKind kind = field.TypeLayout.Kind;
            if (kind is TypeKind.Scalar or TypeKind.Vector or TypeKind.Matrix)
                Console.WriteLine($"{indent}.{field.Name,-14} offset={field.GetOffset(),-4} size={field.TypeLayout.GetSize(),-4} {field.TypeLayout.Kind}");
        }
    }


    // The HLSL target embeds the source file path in every #line directive. That path depends on how
    // the module was loaded (and the machine it was compiled on), so it is reduced to the bare file
    // name. The compiler-under-test applies the same reduction before comparing, keeping the checked-in
    // known-good output portable.
    static readonly Regex s_lineDirective = new("^(?<pre>\\s*#line\\s+\\d+\\s+)\"(?<path>[^\"]*)\"", RegexOptions.Multiline);

    public static string NormalizeSourcePaths(string hlsl)
        => s_lineDirective.Replace(hlsl, m => $"{m.Groups["pre"].Value}\"{Path.GetFileName(m.Groups["path"].Value)}\"");


    static string StageName(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => "vertex",
        ShaderStage.Fragment => "fragment",
        ShaderStage.Compute => "compute",
        ShaderStage.Geometry => "geometry",
        ShaderStage.Hull => "hull",
        ShaderStage.Domain => "domain",
        _ => stage.ToString().ToLowerInvariant(),
    };


    static void Report(DiagnosticInfo diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(diagnostics.Message))
            Console.WriteLine(diagnostics.Message);
    }


    // Walks up from the working directory to find the requested repo-relative directory.
    static string LocateDirectory(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate '{relative}' from {AppContext.BaseDirectory}.");
    }


    const string VariantAttributesModule = """
        module VariantAttributes;

        [__AttributeUsage(_AttributeTargets.Var)]
        public struct variantAttribute
        {
            string values;
        };
        """;
}
