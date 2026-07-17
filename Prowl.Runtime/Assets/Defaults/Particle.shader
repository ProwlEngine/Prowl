Shader "Default/Particle"
{
    Properties
    {
        _MainTex ("Particle Texture", Texture2D) = "white" {}
        _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _SoftParticlesFactor ("Soft Particles Factor", Float) = 1.0
    }

    Pass
    {
        Name "Particle"
        Tags { "RenderOrder" = "Transparent" }

        Cull Off
        ZWrite Off
        Blend SourceAlpha InverseSourceAlpha

        SLANGPROGRAM
        import ProwlCG;
        import VariantAttributes;

        [VariantAxis]
        extern static const bool GPU_INSTANCING;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float4 _MainColor;
            float _SoftParticlesFactor;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float4 color : COLOR0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
            float4 instColor : TEXCOORD12;
            float4 instCustom : TEXCOORD13;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float4 vColor : COLOR0;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            static if (GPU_INSTANCING)
            {
                float3 particlePosition = input.instRow3.xyz;

                float scaleX = length(input.instRow0.xyz);
                float scaleY = length(input.instRow1.xyz);

                float3 matrixRight = input.instRow0.xyz / scaleX;
                float3 matrixUp = input.instRow1.xyz / scaleY;

                float rotationAngle = atan2(matrixRight.y, matrixRight.x);

                float3 cameraRight = Frame.prowl_MatV[0].xyz;
                float3 cameraUp = Frame.prowl_MatV[1].xyz;

                float cosRot = cos(rotationAngle);
                float sinRot = sin(rotationAngle);
                float2 rotatedVertex = float2(
                    input.position.x * cosRot - input.position.y * sinRot,
                    input.position.x * sinRot + input.position.y * cosRot
                );

                float3 worldPosition = particlePosition
                    + cameraRight * rotatedVertex.x * scaleX
                    + cameraUp * rotatedVertex.y * scaleY;

                output.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));

                float2 uvOffset = input.instCustom.yz;
                float uvScale = input.instCustom.w;
                output.texCoord0 = input.uv * uvScale + uvOffset;

                output.vColor = input.color * input.instColor;
            }
            else
            {
                output.position = mul(Object.mvp, float4(input.position, 1.0));
                output.texCoord0 = input.uv;
                output.vColor = input.color;
            }
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 albedo = Mat._MainTex.Sample(input.texCoord0) * input.vColor * Mat._MainColor;

            if (albedo.a < 0.01)
                discard;

            float3 baseColor = gammaToLinearSpace(albedo.rgb);
            return float4(baseColor, albedo.a);
        }
        ENDSLANG
    }
}
