using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Slang;

using Prowl.Graphite.ShaderDef;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// Discovers variant axes from the Slang reflection tree. An axis is an extern field tagged with the
/// <c>[VariantAxis]</c> attribute; its value space is derived from the field's type (an enum's cases,
/// or true/false for a bool). This is purely a reflection concern - it does not generate or compile
/// any variant permutations.
/// </summary>
internal static class VariantReflection
{
    private const string AxisAttributeName = "VariantAxis";


    /// <summary>
    /// Collects the variant axes visible to the compilation of <paramref name="requiredModule"/>.
    /// <paramref name="linkedModules"/> receives the required module plus any other loaded module that
    /// declares an extern matching a discovered axis, so those modules are linked into every variant.
    /// </summary>
    public static List<VariantSpace> CollectVariantSpaces(Session session, Module requiredModule, out List<Module> linkedModules)
    {
        linkedModules = [requiredModule];

        List<Module> loadedModules = [];
        int loadedCount = session.GetLoadedModuleCount();
        for (int i = 0; i < loadedCount; i++)
            loadedModules.Add(session.GetLoadedModule(i));

        List<(Module, string[])> moduleExterns = [];
        List<(Module Module, VariantSpace Space)> moduleVariants = [];

        // Collect all axes and extern declarations, tagged with their owning module. The session is
        // reused across every shader compiled during its lifetime, so GetLoadedModule enumerates
        // modules belonging to unrelated shaders too; the owning module is tracked here so unrelated
        // axes can be filtered out below instead of polluting this shader's variant space.
        foreach (Module loaded in loadedModules)
        {
            DeclReflection[] decls = [.. GetExternFields(loaded)];
            string[] declNames = new string[decls.Length];

            for (int j = 0; j < decls.Length; j++)
            {
                DeclReflection decl = decls[j];
                declNames[j] = decl.Name;

                if (TryGetAxis(decl, loadedModules, out VariantSpace space))
                    moduleVariants.Add((loaded, space));
            }

            moduleExterns.Add((loaded, declNames));
        }

        // Scan for modules that require a linked extern declaration.
        foreach ((Module module, string[] externDecls) in moduleExterns)
        {
            for (int decl = 0; decl < externDecls.Length; decl++)
            {
                string declName = externDecls[decl];
                for (int variant = 0; variant < moduleVariants.Count; variant++)
                {
                    if (declName == moduleVariants[variant].Space.Name)
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

        // Only axes declared by modules actually linked into this shader's compilation are relevant.
        List<Module> linked = linkedModules;
        List<VariantSpace> spaces = [.. moduleVariants.Where(v => linked.Contains(v.Module)).Select(v => v.Space)];

        foreach (VariantSpace space in spaces)
        {
            if (space.IsEnum)
                EnsureEnumAccessible(session, space);
        }

        return spaces;
    }


    // A variant specialization module references the axis enum by name from a separate module, which
    // Slang only permits for public types. This surfaces a clear error naming the offending enum
    // instead of the opaque "declaration not accessible" that would otherwise appear later at link.
    private static void EnsureEnumAccessible(Session session, VariantSpace space)
    {
        string probeName = "__VariantAxisProbe_" + space.Name;

        string source =
            $"module {probeName};\n" +
            $"import {space.TypeModule};\n" +
            $"export public static const {space.DeclType} __probe = {space.DeclType}.{space.Values[0]};";

        try
        {
            session.LoadModuleFromSourceString(probeName, $"{probeName}.slang", source, out _);
        }
        catch (CompilationException)
        {
            throw new Exception($"Variant axis enum '{space.DeclType}' (axis '{space.Name}') must be declared 'public' so it can be referenced from generated variant specialization modules.");
        }
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


    private static bool TryGetAxis(DeclReflection decl, IReadOnlyList<Module> modules, out VariantSpace space)
    {
        space = default;

        VariableReflection variable = decl.AsVariable();

        if (!variable.UserAttributes.Any(a => a.Name == AxisAttributeName))
            return false;

        TypeReflection type = variable.Type;
        string declType = type.FullName;

        if (type.ScalarType == ScalarType.Bool)
        {
            space = new VariantSpace(decl.Name, "bool", ["false", "true"], false);
            return true;
        }

        if (TryGetEnumCases(modules, declType, out List<string> cases, out string typeModule))
        {
            space = new VariantSpace(decl.Name, declType, cases, true, typeModule);
            return true;
        }

        throw new Exception($"Variant axis '{decl.Name}' has unsupported type '{declType}'. Only bool and enum axes are supported.");
    }


    // Enum types are not exposed as a dedicated DeclKind by the reflection binding. The enum's cases
    // surface as its leading UnsupportedForReflection children with non-empty names (a trailing
    // empty-named child and the synthesized operator functions are skipped). The owning module is
    // returned so a specialization module can import it to reference the enum type.
    private static bool TryGetEnumCases(IReadOnlyList<Module> modules, string enumFullName, out List<string> cases, out string typeModule)
    {
        foreach (Module module in modules)
        {
            DeclReflection moduleReflection = module.GetModuleReflection();

            foreach (DeclReflection child in moduleReflection.Children)
            {
                if (child.Type.FullName != enumFullName)
                    continue;

                List<string> found = [];

                foreach (DeclReflection enumCase in child.Children)
                {
                    if (enumCase.Kind != DeclKind.UnsupportedForReflection)
                        continue;

                    if (string.IsNullOrEmpty(enumCase.Name))
                        continue;

                    found.Add(enumCase.Name);
                }

                if (found.Count > 0)
                {
                    cases = found;
                    typeModule = moduleReflection.Name;
                    return true;
                }
            }
        }

        cases = [];
        typeModule = string.Empty;
        return false;
    }
}
