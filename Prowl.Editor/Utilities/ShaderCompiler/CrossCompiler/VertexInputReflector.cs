// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.RegularExpressions;

using Prowl.Runtime.Rendering;

using SPIRVCross.NET;

using Veldrid;

namespace Prowl.Editor;


public static partial class ShaderCrossCompiler
{
    public delegate bool SemanticFormatter(string semantic, out VertexElementFormat format);

    public static VertexInput[] GetStageInputs(Reflector reflector, Resources resources, SemanticFormatter formatter)
    {
        VertexInput[] inputLocations = new VertexInput[resources.StageInputs.Length];

        for (int i = 0; i < resources.StageInputs.Length; i++)
        {
            ReflectedResource resource = resources.StageInputs[i];

            var typeInfo = reflector.GetTypeHandle(resource.type_id);

            string semantic = reflector.GetDecorationString(resource.id, Decoration.HlslSemanticGOOGLE) ?? "SEMANTIC_UNKNOWN";
            semantic = semantic.ToUpperInvariant();

            ParseSemantic(semantic, formatter, out VertexInput input);

            if (!reflector.HasDecoration(resource.id, Decoration.Location))
                throw new Exception("Stage input does not contain location decoration.");

            uint location = reflector.GetDecoration(resource.id, Decoration.Location);

            if (location >= inputLocations.Length)
                throw new Exception($"Invalid input location: {location}. Is the location being manually defined?");

            inputLocations[location] = input;
        }

        return inputLocations;
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingInteger();

    private static bool ParseSemantic(string semantic, SemanticFormatter formatter, out VertexInput input)
    {
        // If the uniform has no trailing index, force its index to 0.
        if (!TrailingInteger().IsMatch(semantic))
            semantic += "0";

        input = new VertexInput(semantic, VertexElementFormat.Float1);

        if (!formatter.Invoke(semantic, out VertexElementFormat format))
            return false;

        input = new VertexInput(semantic, format);

        return true;
    }
}
