Shader "Default/Sprite"
{
    Properties
    {
        _MainTex("Sprite Texture", Texture2D) = "white"
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
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

            out vec2 vUV;
            out vec3 vRelativePos;
            out vec4 vColor;

            void main()
            {
                vec3 relative = Graphite_ObjectToRelative(vertexPosition);
                gl_Position = Graphite_RelativeToClip(relative);
                vUV = vertexUV0;
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

    Pass
    {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }
        Cull Off
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"

            out vec2 vUV;
            out float vAlpha;

            void main()
            {
                gl_Position = Graphite_ObjectToClip(vertexPosition);
                vUV = vertexUV0;
                vAlpha = vertexColor.a * Graphite_Object().color.a;
            }
        }
        Fragment
        {
            uniform sampler2D _MainTex;
            uniform vec4 _MainColor;

            in vec2 vUV;
            in float vAlpha;

            void main()
            {
                if (texture(_MainTex, vUV).a * vAlpha * _MainColor.a < 0.5)
                    discard;
            }
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}
