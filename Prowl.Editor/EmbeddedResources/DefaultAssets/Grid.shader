Shader "Default/Grid"

Pass "Grid"
{
	Blend
    {
        Src Color SourceAlpha
        Src Alpha SourceAlpha

        Dest Color InverseSourceAlpha
        Dest Alpha InverseSourceAlpha

        Mode Color Add
        Mode Alpha Add
    }

    // Stencil state
    DepthStencil
    {
        // Depth write
        DepthWrite Off
    }

    // Rasterizer culling mode
    Cull None

	SHADERPROGRAM
        #pragma vertex Vertex
        #pragma fragment Fragment


        struct Attributes
        {
            float3 pos : POSITION;
            float2 uv : TEXCOORD0;
        };


        struct Varyings
        {
            float4 pos : SV_POSITION;
            float3 wpos : POSITION;
            float2 uv : TEXCOORD0;
        };


        float4x4 MvpInverse;
        float3 CameraPosition;
        float4 GridColor;

		float3 PlaneNormal;
		float3 PlaneRight;
		float3 PlaneUp;

		float PrimaryGridSize;
		float LineWidth;
		float SecondaryGridSize;


        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;

            output.pos = float4(input.pos, 1.0);
            output.wpos = mul(MvpInverse, float4(input.pos.xy, 1.0, 1.0)).xyz;
            output.uv = input.uv;

            return output;
        }


		// https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8
        float pristineGrid(float2 uv, float2 lineWidth)
        {
            lineWidth = saturate(lineWidth);

            float4 uvDDXY = float4(ddx(uv), ddy(uv));
            float2 uvDeriv = float2(length(uvDDXY.xz), length(uvDDXY.yw));

            bool2 invertLine = lineWidth > 0.5;

            float2 targetWidth = select(invertLine, 1.0 - lineWidth, lineWidth);
            float2 drawWidth = clamp(targetWidth, uvDeriv, 0.5);

            float2 lineAA = max(uvDeriv, 0.000001) * 1.5;
            float2 gridUV = abs(frac(uv) * 2.0 - 1.0);

            gridUV = select(invertLine, gridUV, 1.0 - gridUV);

            float2 grid2 = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);

            grid2 *= saturate(targetWidth / drawWidth);
            grid2 = lerp(grid2, targetWidth, saturate(uvDeriv * 2.0 - 1.0));
            grid2 = select(invertLine, 1.0 - grid2, grid2);

            return lerp(grid2.x, 1.0, grid2.y);
        }


		float Grid(float3 ro, float scale, float3 rd, float lineWidth, out float d)
		{
            d = 0.0;
			ro /= scale;

			float ndotd = dot(-PlaneNormal, rd);

			if (ndotd == 0.0)
			    return 0.0;

			d = dot(PlaneNormal, ro) / ndotd;

			if (d <= 0.0)
			    return 0.0;

			float3 hit = ro + rd * d;

			float u = dot(hit, PlaneRight);
			float v = dot(hit, PlaneUp);

			return pristineGrid(float2(u, v), (float2)lineWidth * LineWidth);
		}


		float4 Fragment(Varyings input) : SV_TARGET
		{
			float d = 0.0;
			float bd = 0.0;

			float sg = Grid(CameraPosition, PrimaryGridSize, normalize(input.wpos), 0.02, d);
			float bg = Grid(CameraPosition, SecondaryGridSize, normalize(input.wpos), 0.02, bd);

			float4 OutputColor = float4(GridColor.xyz, sg);
			OutputColor += float4(GridColor.xyz, bg * 0.5);

            return OutputColor;
		}
	ENDPROGRAM
}
