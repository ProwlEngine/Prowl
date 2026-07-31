// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Quill.ShaderGen;

/// <summary>
/// Raylib submits canvas geometry through rlgl's immediate-mode batch, which binds its own attribute
/// names and its own <c>mvp</c> matrix, so it cannot use the generated vertex shader. Only this glue
/// differs; the shading itself still comes from Canvas.slang via the generated fragment shader.
///
/// The varying names come from <see cref="Names"/> so this cannot drift out of step with what the
/// generated fragment stage expects.
/// </summary>
internal static class RaylibVertex
{
    public static string Build(GlslDialect dialect, IReadOnlyList<string> varyings)
    {
        // rlgl pushes position, texcoord and colour under these fixed names.
        string body = $$"""
            {{dialect.VersionDirective}}
            in vec3 vertexPosition;
            in vec2 vertexTexCoord;
            in vec4 vertexColor;

            uniform mat4 mvp;

            out vec2 vary_{{varyings[0]}};
            out vec4 vary_{{varyings[1]}};
            out vec2 vary_{{varyings[2]}};

            void main()
            {
                vary_{{varyings[0]}} = vertexTexCoord;
                vary_{{varyings[1]}} = vertexColor;
                vary_{{varyings[2]}} = vertexPosition.xy;
                gl_Position = mvp * vec4(vertexPosition, 1.0);
            }
            """;

        return body.Replace("\r\n", "\n") + "\n";
    }
}
