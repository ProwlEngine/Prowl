// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Quill.ShaderGen;

/// <summary>
/// SFML draws through its own fixed-function vertex pipeline, so the vertex stage reads gl_Vertex,
/// gl_Color and gl_MultiTexCoord0 rather than declared attributes. Only this glue differs; the
/// shading still comes from Canvas.slang via the generated fragment stage.
///
/// The varying names come from the same list the fragment stage was generated with, so the two
/// cannot drift apart.
/// </summary>
internal static class SfmlVertex
{
    public static string Build(IReadOnlyList<string> varyings)
    {
        // Unlike the GL backends there is no transpose here: this multiply is written by hand as
        // projection * vertex, so the matrix goes up exactly as the canvas hands it over.
        string body = $$"""
            uniform mat4 projection;

            varying vec2 vary_{{varyings[0]}};
            varying vec4 vary_{{varyings[1]}};
            varying vec2 vary_{{varyings[2]}};

            void main()
            {
                vary_{{varyings[0]}} = gl_MultiTexCoord0.xy;
                vary_{{varyings[1]}} = gl_Color;
                vary_{{varyings[2]}} = gl_Vertex.xy;
                gl_Position = projection * gl_Vertex;
            }
            """;

        return body.Replace("\r\n", "\n") + "\n";
    }
}
