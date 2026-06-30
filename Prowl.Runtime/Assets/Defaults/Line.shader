Shader "Default/Line"
{
    Properties
    {
        _MainTex ("Texture", Texture2D) = "white" {}
        _StartColor ("Start Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _EndColor ("End Color", Color) = (1.0, 1.0, 1.0, 1.0)
    }

    Pass
    {
        Name "Line"
        Tags { "RenderOrder" = "Transparent" }
        Cull Off
        Blend SourceAlpha InverseSourceAlpha

        SLANGPROGRAM
        import ProwlCG;
        import Lighting;

        struct MaterialData { Sampler2D<float4> _MainTex; }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float4 color : COLOR0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 currentPos : TEXCOORD2;
            float4 previousPos : TEXCOORD3;
            float4 vColor : COLOR0;
        }
        struct FragOutput
        {
            float4 gAlbedo : SV_Target0;
            float4 gMotionVector : SV_Target1;
            float4 gNormal : SV_Target2;
            float4 gSurface : SV_Target3;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = mul(Object.mvp, float4(input.position, 1.0));
            output.currentPos = output.position;
            output.texCoord0 = input.uv;

            float4 prevWorldPos = mul(Object.prowl_PrevObjectToWorld, float4(input.position, 1.0));
            output.previousPos = mul(Frame.prowl_PrevViewProj, prevWorldPos);

            output.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
            output.vColor = input.color;
            return output;
        }

        [shader("fragment")]
        FragOutput Fragment(Varyings input)
        {
            FragOutput o;

            float2 curNDC = (input.currentPos.xy / input.currentPos.w) - Frame._CameraJitter;
            float2 prevNDC = (input.previousPos.xy / input.previousPos.w) - Frame._CameraPreviousJitter;
            o.gMotionVector = float4((curNDC - prevNDC) * 0.5, 0.0, 1.0);

            float4 albedo = Mat._MainTex.Sample(input.texCoord0) * input.vColor;

            o.gNormal = float4(0.0, 0.0, 1.0, 1.0);
            o.gSurface = float4(1.0, 0.0, 0.0, 1.0);

            float3 baseColor = gammaToLinearSpace(albedo.rgb);
            baseColor = ApplyFog(baseColor, input.worldPos);

            o.gAlbedo = float4(baseColor, albedo.a);
            return o;
        }
        ENDSLANG
    }
}
