Shader "Default/Particle"
{
    Properties
    {
        _MainTex("Particle Texture", Texture2D) = "white" {}
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _SoftParticlesFactor("Soft Particles Factor", Float) = 1.0
    }

    Pass
    {
        Name "Particle"
        Tags { "RenderOrder" = "Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        SLANGPROGRAM

        import ProwlCG;
        import VertexAttributes;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
            float4 color : COLOR0;
#ifdef GPU_INSTANCING
            float4 instanceModelRow0 : TEXCOORD8;
            float4 instanceModelRow1 : TEXCOORD9;
            float4 instanceModelRow2 : TEXCOORD10;
            float4 instanceModelRow3 : TEXCOORD11;
            float4 instanceColor : TEXCOORD12;
            float4 instanceCustomData : TEXCOORD13;
#endif
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float4 vColor : COLOR0;
            float3 worldPos : TEXCOORD1;
            float vLifetime : TEXCOORD2;
        }

        struct Material
        {
            float4 _MainColor;
            float _SoftParticlesFactor;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
#ifdef GPU_INSTANCING
            // Extract position and scale from instance matrix
            float3 particlePosition = input.instanceModelRow3.xyz;

            // Scale from the instance matrix column lengths
            float scaleX = length(input.instanceModelRow0.xyz);
            float scaleY = length(input.instanceModelRow1.xyz);

            // Normalized right/up vectors
            float3 matrixRight = input.instanceModelRow0.xyz / scaleX;
            float3 matrixUp = input.instanceModelRow1.xyz / scaleY;

            // Z-axis rotation that was applied
            float rotationAngle = atan2(matrixRight.y, matrixRight.x);

            // Camera right/up are rows 0/1 of the view matrix (GLSL V[i][0]/[i][1] -> Slang V[0]/[1]).
            float3 cameraRight = Frame.prowl_MatV[0].xyz;
            float3 cameraUp = Frame.prowl_MatV[1].xyz;

            // Apply rotation to the quad vertices
            float cosRot = cos(rotationAngle);
            float sinRot = sin(rotationAngle);
            float2 rotatedVertex = float2(
                input.position.x * cosRot - input.position.y * sinRot,
                input.position.x * sinRot + input.position.y * cosRot
            );

            // Build billboard quad in world space with rotation applied
            float3 worldPosition = particlePosition
                + cameraRight * rotatedVertex.x * scaleX
                + cameraUp * rotatedVertex.y * scaleY;

            o.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));

            // instanceCustomData: x=lifetime, y=UV offsetX, z=UV offsetY, w=UV scale
            float2 uvOffset = input.instanceCustomData.yz;
            float uvScale = input.instanceCustomData.w;
            o.texCoord0 = input.uv0 * uvScale + uvOffset;

            o.worldPos = worldPosition;
            o.vColor = input.color * input.instanceColor;
            o.vLifetime = input.instanceCustomData.x;
#else
            // Non-instanced fallback
            o.position = mul(Object.mvp, float4(input.position, 1.0));
            o.texCoord0 = input.uv0;
            o.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
            o.vColor = input.color;
            o.vLifetime = 0.0;
#endif
            return o;
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
