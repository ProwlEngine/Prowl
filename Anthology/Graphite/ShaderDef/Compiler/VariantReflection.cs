using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Slang;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// Finds variant axes in the Slang reflection tree. An axis is an extern field marked [VariantAxis];
/// values come from the field type (enum cases, or bool true/false). Reflection only, no codegen.
/// </summary>
internal static class VariantReflection
{
    private const string AxisAttributeName = "VariantAxis";


    /// <summary>
    /// Collects variant axes declared by requiredModule or any module it transitively imports.
    /// linkedModules gets the required module plus any of those declaring a matching extern.
    /// </summary>
    public static List<VariantSpace> CollectVariantSpaces(Session session, Module requiredModule, out List<Module> linkedModules)
    {
        linkedModules = [requiredModule];

        // The session is reused across every shader compiled during its lifetime, so GetLoadedModule
        // enumerates modules belonging to unrelated shaders too. Only modules this pass transitively
        // imports can define an axis it could read, so everything else is dropped up front.
        List<Module> scopedModules = ScopeToImports(session, requiredModule);

        List<(Module, string[])> moduleExterns = [];
        List<(Module Module, VariantSpace Space)> moduleVariants = [];

        foreach (Module loaded in scopedModules)
        {
            DeclReflection[] decls = [.. GetExternFields(loaded)];
            string[] declNames = new string[decls.Length];

            for (int j = 0; j < decls.Length; j++)
            {
                DeclReflection decl = decls[j];
                declNames[j] = decl.Name;

                if (TryGetAxis(decl, scopedModules, out VariantSpace space))
                    moduleVariants.Add((loaded, space));
            }

            moduleExterns.Add((loaded, declNames));
        }

        // Scan for modules that require a linked extern declaration.
        foreach ((Module module, string[] externDecls) in moduleExterns)
            if (!module.Equals(requiredModule) && externDecls.Any(n => moduleVariants.Any(v => v.Space.Name == n)))
                linkedModules.Add(module);

        // Only axes declared by modules actually linked into this shader's compilation are relevant.
        List<VariantSpace> spaces = [];
        foreach ((Module module, VariantSpace space) in moduleVariants)
            if (linkedModules.Contains(module))
                spaces.Add(space);

        foreach (VariantSpace space in spaces)
        {
            if (space.IsEnum)
                EnsureEnumAccessible(session, space);
        }

        return spaces;
    }


    // The modules requiredModule transitively imports, itself included. Slang reports dependencies by
    // unique identity for file-backed modules and by file path for ones loaded from a source string,
    // so a module matches on either. A module identifiable by neither is kept, since over-enumerating
    // an axis only costs compile time while dropping a live one renders the wrong variant.
    private static List<Module> ScopeToImports(Session session, Module requiredModule)
    {
        HashSet<string> dependencies = [];
        int dependencyCount = requiredModule.GetDependencyFileCount();
        for (int i = 0; i < dependencyCount; i++)
            dependencies.Add(requiredModule.GetDependencyFilePath(i));

        List<Module> scoped = [];
        int loadedCount = session.GetLoadedModuleCount();

        for (int i = 0; i < loadedCount; i++)
        {
            Module loaded = session.GetLoadedModule(i);

            if (loaded.Equals(requiredModule))
            {
                scoped.Add(loaded);
                continue;
            }

            string identity = loaded.GetUniqueIdentity();
            string path = loaded.GetFilePath();

            bool identifiable = !string.IsNullOrEmpty(identity) || !string.IsNullOrEmpty(path);
            bool imported = dependencies.Contains(identity) || dependencies.Contains(path);

            if (!identifiable || imported)
                scoped.Add(loaded);
        }

        return scoped;
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
