// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.RegularExpressions;

using Prowl.Runtime;

using SPIRVCross.NET;

using Veldrid;

#pragma warning disable

namespace Prowl.Editor.Utilities;

public static partial class VertexInputReflector
{

    public delegate bool SemanticFormatter(string semantic, out VertexElementFormat format);

    public static VertexInput[] GetStageInputs(Reflector reflector, Resources resources, SemanticFormatter formatter)
    {
        VertexInput[] inputLocations = new VertexInput[resources.StageInputs.Length];

        for (int i = 0; i < resources.StageInputs.Length; i++)
        {
            ReflectedResource resource = resources.StageInputs[i];

            // var typeInfo = reflector.GetTypeHandle(resource.type_id);

            if (!ParseSemantic(resource.name, formatter, out VertexInput input))
                throw new Exception($"Unknown semantic: {input.semantic}");

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

    private static bool ParseSemantic(string name, SemanticFormatter formatter, out VertexInput input)
    {
        string semantic = name.Substring(name.LastIndexOf('.') + 1);

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
