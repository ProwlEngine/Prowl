Shader "PBRRenderer/Lit"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Emission("Emission", Color) = (0, 0, 0, 1)
        _Metallic("Metallic", Float) = 0
        _Roughness("Roughness", Float) = 0.5
        _Tiling("Tiling", Float) = 1
        _Grid("Grid", Float) = 0
        _BaseMap("Base Map", Texture2D) = "white" {}
        _NormalMap("Normal Map", Texture2D) = "bump" {}
        _MetalRoughMap("Metal Roughness Map", Texture2D) = "white" {}
        _EmissionMap("Emission Map", Texture2D) = "white" {}
    }

    Pass
    {
        Tags { "LightMode" = "Prepass" }
        Cull Back
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"
            void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
        }
        Fragment { void main() { } }
        ENDGLSL
    }

    Pass
    {
        Tags { "LightMode" = "Forward" }
        Cull Back
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"
            layout(location = 0) out vec3 vWorld;
            layout(location = 1) out vec3 vNormal;
            layout(location = 2) out vec2 vUV;
            void main()
            {
                vWorld = Graphite_ObjectToWorld(vertexPosition);
                vNormal = Graphite_ObjectToWorldNormal(vertexNormal);
                vUV = vertexUV0;
                gl_Position = Graphite_RelativeToClip(vWorld);
            }
        }
        Fragment
        {
            #include "Graphite/Lighting.glsl"
            #include "Graphite/Passes.glsl"

            uniform vec4 _BaseColor;
            uniform vec4 _Emission;
            uniform float _Metallic;
            uniform float _Roughness;
            uniform float _Tiling;
            uniform float _Grid;
            uniform sampler2D _BaseMap;
            uniform sampler2D _NormalMap;
            uniform sampler2D _MetalRoughMap;
            uniform sampler2D _EmissionMap;

            layout(location = 0) in vec3 vWorld;
            layout(location = 1) in vec3 vNormal;
            layout(location = 2) in vec2 vUV;
            layout(location = 0) out vec4 outColor;

            vec3 ToLinear(vec3 c) { return pow(c, vec3(2.2)); }

            void main()
            {
                vec2 uv = vec2(vUV.x, 1.0 - vUV.y) * _Tiling;
                vec3 n = normalize(vNormal);

                vec3 dp1 = dFdx(vWorld);
                vec3 dp2 = dFdy(vWorld);
                vec2 duv1 = dFdx(uv);
                vec2 duv2 = dFdy(uv);
                vec3 dp2perp = cross(dp2, n);
                vec3 dp1perp = cross(n, dp1);
                vec3 t = dp2perp * duv1.x + dp1perp * duv2.x;
                vec3 b = dp2perp * duv1.y + dp1perp * duv2.y;
                float invMax = inversesqrt(max(max(dot(t, t), dot(b, b)), 1e-12));
                vec3 tangentNormal = texture(_NormalMap, uv).xyz * 2.0 - 1.0;
                vec3 N = normalize(mat3(t * invMax, b * invMax, n) * tangentNormal);

                vec3 albedo = _BaseColor.rgb * ToLinear(texture(_BaseMap, uv).rgb);
                if (_Grid > 0.0)
                {
                    vec2 p = Graphite_AbsoluteFromRelative(vWorld).xz / _Grid;
                    vec2 g = abs(fract(p - 0.5) - 0.5) / max(fwidth(p), vec2(1e-4));
                    albedo *= mix(1.0, 0.5, 1.0 - min(min(g.x, g.y), 1.0));
                }

                vec3 metalRough = texture(_MetalRoughMap, uv).rgb;
                float roughness = clamp(_Roughness * metalRough.g, 0.04, 1.0);
                float metallic = clamp(_Metallic * metalRough.b, 0.0, 1.0);
                float ao = 1.0;

                vec3 V = GetWorldViewDir(vWorld);
                vec3 F0 = mix(vec3(0.04), albedo, metallic);
                vec3 direct = CalculateForwardLighting(vWorld, N, n, V, albedo, metallic, roughness, ao);
                vec3 diffuseGI = Graphite_IndirectDiffuse(N, vec2(0.0));
                vec3 indirect = albedo * (1.0 - metallic) * diffuseGI * ao
                    + Graphite_IndirectSpecular(vWorld, N, V, F0, roughness, ao, diffuseGI * ao);
                vec3 emission = _Emission.rgb * ToLinear(texture(_EmissionMap, uv).rgb);

                outColor = vec4(ApplyFog(direct + indirect + emission, vWorld), 1.0);
                Graphite_WriteGBuffer(N, roughness, metallic);
            }
        }
        ENDGLSL
    }
}
