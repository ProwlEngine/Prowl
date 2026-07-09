using System.Collections.Generic;
using System.Text;

using Prowl.Graphite.ShaderDef;


namespace Prowl.Graphite.ShaderDef.Compiler;


internal static class VariantGenerator
{
    public static string BuildSpecializationModule(string moduleName, IReadOnlyList<VariantSpace> spaces, Keyword[] combo)
    {
        StringBuilder sb = new();

        sb.AppendLine($"module {moduleName};");

        HashSet<string> imported = [];
        for (int i = 0; i < spaces.Count; i++)
        {
            string? typeModule = spaces[i].TypeModule;

            if (!string.IsNullOrEmpty(typeModule) && imported.Add(typeModule))
                sb.AppendLine($"import {typeModule};");
        }

        for (int i = 0; i < spaces.Count; i++)
        {
            VariantSpace space = spaces[i];

            string literal = space.IsEnum ? $"{space.DeclType}.{combo[i].Value}" : combo[i].Value;

            sb.AppendLine($"export public static const {space.DeclType} {space.Name} = {literal};");
        }

        return sb.ToString();
    }
}
