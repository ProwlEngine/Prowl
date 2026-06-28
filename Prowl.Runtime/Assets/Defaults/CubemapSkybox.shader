Shader "Default/CubemapSkybox"
{
    Properties
    {
        _CubeRight("Right (+X)", Texture2D) = "white" {}
        _CubeLeft("Left (-X)", Texture2D) = "white" {}
        _CubeTop("Top (+Y)", Texture2D) = "white" {}
        _CubeBottom("Bottom (-Y)", Texture2D) = "white" {}
        _CubeFront("Front (+Z)", Texture2D) = "white" {}
        _CubeBack("Back (-Z)", Texture2D) = "white" {}
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Exposure("Exposure", Float) = 1.0
    }

    Pass
    {
        Name "CubemapSkybox"
        Tags { "RenderOrder" = "Opaque" }
        Cull Front
        ZWrite Off
        ZTest LessEqual

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; }
        struct Varyings { float4 position : SV_Position; float3 vDirection : TEXCOORD0; }

        struct Material
        {
            float4 _Tint;
            float _Exposure;
            Sampler2D<float4> _CubeRight;
            Sampler2D<float4> _CubeLeft;
            Sampler2D<float4> _CubeTop;
            Sampler2D<float4> _CubeBottom;
            Sampler2D<float4> _CubeFront;
            Sampler2D<float4> _CubeBack;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            // Strip translation from view matrix (GLSL V[3][i] -> Slang V[i][3]).
            float4x4 viewNoTranslation = Frame.prowl_MatV;
            viewNoTranslation[0][3] = 0.0;
            viewNoTranslation[1][3] = 0.0;
            viewNoTranslation[2][3] = 0.0;

            float4 pos = mul(mul(Frame.prowl_MatP, viewNoTranslation), float4(input.position, 1.0));

            Varyings o;
            o.position = pos.xyww;
            o.vDirection = input.position;
            return o;
        }

        // Sample a cubemap face from 6 separate 2D textures
        float4 sampleCubemap(float3 dir)
        {
            float3 absDir = abs(dir);
            float2 uv;
            float4 color;

            if (absDir.x >= absDir.y && absDir.x >= absDir.z)
            {
                if (dir.x > 0.0)
                {
                    uv = float2(-dir.z, -dir.y) / absDir.x * 0.5 + 0.5;
                    color = Mat._CubeRight.Sample(uv);
                }
                else
                {
                    uv = float2(dir.z, -dir.y) / absDir.x * 0.5 + 0.5;
                    color = Mat._CubeLeft.Sample(uv);
                }
            }
            else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
            {
                if (dir.y > 0.0)
                {
                    uv = float2(dir.x, dir.z) / absDir.y * 0.5 + 0.5;
                    color = Mat._CubeTop.Sample(uv);
                }
                else
                {
                    uv = float2(dir.x, -dir.z) / absDir.y * 0.5 + 0.5;
                    color = Mat._CubeBottom.Sample(uv);
                }
            }
            else
            {
                if (dir.z > 0.0)
                {
                    uv = float2(dir.x, -dir.y) / absDir.z * 0.5 + 0.5;
                    color = Mat._CubeFront.Sample(uv);
                }
                else
                {
                    uv = float2(-dir.x, -dir.y) / absDir.z * 0.5 + 0.5;
                    color = Mat._CubeBack.Sample(uv);
                }
            }

            return color;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float3 dir = normalize(input.vDirection);
            float4 color = sampleCubemap(dir);
            color.rgb *= Mat._Tint.rgb * Mat._Exposure;

            return float4(color.rgb, 1.0);
        }

        ENDSLANG
    }
}
