using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Graphite.Variants;


namespace Prowl.Graphite.Compiler;


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



    public static Keyword[][] Generate(IReadOnlyList<VariantSpace> props, int maxCap)
    {
        // Total combinations
        int total = 1;
        for (int i = 0; i < props.Count; i++)
            total *= props[i].Values.Count;
        total = Math.Min(total, maxCap);

        Keyword[][] result = new Keyword[total][];

        int[] indices = new int[props.Count];

        for (int count = 0; count < total; count++)
        {
            // Build one combination
            Keyword[] combo = new Keyword[props.Count];
            for (int i = 0; i < props.Count; i++)
            {
                VariantSpace space = props[i];

                combo[i] = new Keyword(space.Name, space.Values[indices[i]]);
            }

            result[count] = combo;

            // Increment like an odometer
            for (int i = props.Count - 1; i >= 0; i--)
            {
                indices[i]++;

                if (indices[i] < props[i].Values.Count)
                    break;

                indices[i] = 0;
            }
        }

        return result;
    }
}
