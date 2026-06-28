Shader "Default/GizmoIcon"
{
    Properties
    {
        _MainTex("Icon", Texture2D) = "white" {}
        _IconColor("Color", Vector) = (1.0, 1.0, 1.0, 1.0)
        _IconCenter("Center", Vector) = (0.0, 0.0, 0.0, 0)
        _IconScale("Scale", Float) = 1.0
    }

    Pass
    {
        Name "GizmoIcon"
        Tags { "RenderOrder" = "Transparent" }
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; }
        struct Varyings { float4 position : SV_Position; float2 vUV : TEXCOORD0; }

        struct Material
        {
            float3 _IconCenter;
            float _IconScale;
            float4 _IconColor;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _CameraDepthTexture;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            // World-space billboard: 1 meter * _IconScale, always facing camera
            float halfSize = Mat._IconScale * 0.5;

            // Camera right/up are rows 0/1 of the view matrix (GLSL V[i][0]/V[i][1] -> Slang V[0]/V[1]).
            float3 camRight = Frame.prowl_MatV[0].xyz;
            float3 camUp    = Frame.prowl_MatV[1].xyz;

            // position is -1..1 from fullscreen quad
            float3 worldPos = Mat._IconCenter
                + camRight * (input.position.x * halfSize)
                + camUp    * (input.position.y * halfSize);

            Varyings o;
            o.position = mul(Frame.prowl_MatVP, float4(worldPos, 1.0));
            o.vUV = input.position.xy * 0.5 + 0.5;
            return o;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 texColor = Mat._MainTex.Sample(input.vUV);
            float4 color = texColor * Mat._IconColor;

            if (color.a < 0.01) discard;

            // Depth-based dimming (same as gizmo shader)
            float2 screenUV = input.position.xy / Frame._ScreenParams.xy;
            float sceneDepth = Mat._CameraDepthTexture.Sample(screenUV).r;
            float fragmentDepth = input.position.z;
            float occluded = step(sceneDepth, fragmentDepth - 0.00001);
            if (occluded > 0.5)
            {
                color.rgb *= 0.5;
                color.a *= 0.3;
            }

            return color;
        }

        ENDSLANG
    }
}
