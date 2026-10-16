// World space SDF text for TextMeshComponent, depth tested so nearer geometry hides it.
Shader "UI/Text Mesh"
{
    Properties
    {
        _MainTex("SDF Atlas", Texture2D) = "white"
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling("Tiling", Vector2) = (1.0, 1.0)
        _Offset("Offset", Vector2) = (0.0, 0.0)
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "RenderQueue" = "3000" }
        Cull Off
        ZWrite Off
        Blend SourceAlpha InverseSourceAlpha
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"

            uniform vec2 _Tiling;
            uniform vec2 _Offset;

            out vec2 vUV;
            out vec4 vColor;

            void main()
            {
                gl_Position = Graphite_ObjectToClip(vertexPosition);
                vUV = vertexUV0 * _Tiling + _Offset;
                vColor = vertexColor;
            }
        }
        Fragment
        {
            uniform sampler2D _MainTex;
            uniform vec4 _MainColor;

            in vec2 vUV;
            in vec4 vColor;

            layout(location = 0) out vec4 fragColor;

            const float sdfPxRange = 4.0;

            // Per axis then the smaller, so a stretched underline keeps the gradient on its thin edges
            float SdfScreenPxRange(vec2 uv)
            {
                vec2 unitRange = vec2(sdfPxRange) / vec2(textureSize(_MainTex, 0));
                vec2 range = unitRange / fwidth(uv);
                return max(min(range.x, range.y), 1.0);
            }

            void main()
            {
                float sd = texture(_MainTex, vUV).r;
                float coverage = clamp(SdfScreenPxRange(vUV) * (sd - 0.5) + 0.5, 0.0, 1.0);
                vec4 color = vColor * _MainColor;
                fragColor = vec4(color.rgb, color.a * coverage);
            }
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}
