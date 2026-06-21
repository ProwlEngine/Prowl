using System;
using System.Collections.Generic;

using Prowl.Graphite.Variants;


namespace Prowl.Graphite.Compiler;

public static class VariantGenerator
{
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
