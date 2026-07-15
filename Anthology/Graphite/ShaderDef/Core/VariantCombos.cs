using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


internal static class VariantCombos
{
    // Enumerates every keyword combination across the given axes, as an odometer with the last axis
    // varying fastest. The order matches the mixed-radix indexing used by ShaderPass.
    public static Keyword[][] Generate(IReadOnlyList<VariantSpace> axes)
    {
        int total = 1;
        for (int i = 0; i < axes.Count; i++)
            total *= axes[i].Values.Count;

        Keyword[][] result = new Keyword[total][];
        int[] indices = new int[axes.Count];

        for (int count = 0; count < total; count++)
        {
            Keyword[] combo = new Keyword[axes.Count];
            for (int i = 0; i < axes.Count; i++)
                combo[i] = new Keyword(axes[i].Name, axes[i].Values[indices[i]]);

            result[count] = combo;

            for (int i = axes.Count - 1; i >= 0; i--)
            {
                indices[i]++;

                if (indices[i] < axes[i].Values.Count)
                    break;

                indices[i] = 0;
            }
        }

        return result;
    }
}
