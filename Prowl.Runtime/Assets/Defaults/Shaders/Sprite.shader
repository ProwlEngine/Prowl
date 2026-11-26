Shader "Default/Sprite"
{
    Properties
    {
        _MainTex("Sprite Texture", Texture2D) = "white"
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
            out vec3 vRelativePos;
            out vec4 vColor;

            void main()
            {
                vec3 relative = Graphite_ObjectToRelative(vertexPosition);
                gl_Position = Graphite_RelativeToClip(relative);
                vUV = vertexUV0 * _Tiling + _Offset;
                vRelativePos = relative;
                vColor = vertexColor * Graphite_Object().color;
            }
        }
        Fragment
        {
            #include "Prowl/Common.glsl"
            #include "Graphite/Lighting.glsl"

            uniform sampler2D _MainTex;
            uniform vec4 _MainColor;

            in vec2 vUV;
            in vec3 vRelativePos;
            in vec4 vColor;

            layout(location = 0) out vec4 fragColor;

            void main()
            {
                vec4 texel = texture(_MainTex, vUV);
                vec4 tint = vColor * _MainColor;
                vec4 albedo = vec4(texel.rgb * gammaToLinearSpace(tint.rgb), texel.a * tint.a);
                vec3 color = Graphite_ApplyFog(albedo.rgb, vRelativePos);
                fragColor = vec4(color, albedo.a);
            }
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}
