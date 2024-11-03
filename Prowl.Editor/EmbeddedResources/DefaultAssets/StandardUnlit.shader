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

        // Per-draw buffer. These only require an update to data in a uniform buffer, so it's good to group them depending on where their data changes.
        // In this case, this buffer gets updated every draw call with new matrices, so it's appropriately named _PerDraw
        cbuffer _PerDraw
        {
            float4x4 _Matrix_MVP;
            float4 _MainColor;
            int _ObjectID;
        }

        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

			output.position = mul(_Matrix_MVP, float4(input.position.xyz, 1.0));
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
