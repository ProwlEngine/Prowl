Shader "Default/StandardUnlit"

Properties
{
    _AlbedoTex("Albedo Texture", Texture2D)
    _MainColor("Main Color", Color)
}

Pass "Unlit"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull Back

    // Stencil state
    DepthStencil
    {
        // Depth write
        DepthWrite Off

        // Comparison kind
        DepthTest LessEqual
    }

	HLSLPROGRAM
		#pragma vertex Vertex
        #pragma fragment Fragment
        
        #include "Prowl.hlsl"

		struct Attributes
		{
			float3 position : POSITION;
			float2 uv : TEXCOORD0;
		};

		struct Varyings
		{
			float4 position : SV_POSITION;
			float2 uv : TEXCOORD0;
		};

        // Global uniforms. These ideally are set per-material, as the binding system requires the resource set to be re-created to update these.
		Texture2D<float4> _AlbedoTex;
		SamplerState sampler_AlbedoTex;

        float4 _MainColor;

        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

			output.position = mul(PROWL_MATRIX_MVP, float4(input.position.xyz, 1.0));
			output.uv = input.uv;

            return output;
        }


        float4 Fragment(Varyings input) : SV_TARGET
        {
			float4 color = _AlbedoTex.Sample(sampler_AlbedoTex, input.uv) * _MainColor;

            return color;
        }
	ENDHLSL
}
