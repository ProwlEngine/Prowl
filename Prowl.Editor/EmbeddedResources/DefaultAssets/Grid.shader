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
        
        // Comparison kind
        DepthTest Off
    }

    // Rasterizer culling mode
    Cull None

	Inputs
	{
		VertexInput 
        {
            Position // Input location 0
            UV0 // Input location 1
        }
        
        // Set 0
        Set
        {
            // Binding 0
            Buffer MVPBuffer
            {
                MvpInverse Matrix4x4
            }
        }

        // Set 1
        Set
        {
            // Binding 0
            Buffer ResourceBuffer
            {
				CameraPosition Vector3
				GridColor Vector4

				PlaneNormal Vector3
				PlaneRight Vector3
				PlaneUp Vector3

				LineWidth Float
				PrimaryGridSize Float
				SecondaryGridSize Float
            }
        }
	}

	Features
	{
		CLIP_SPACE_Y_INVERTED [ 0 1 ]
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		layout(location = 1) in vec2 vertexTexCoord;
		
        layout(location = 0) out vec3 Position;
		layout(location = 1) out vec2 TexCoords;
		
		layout(set = 0, binding = 0, std140) uniform MVPBuffer
		{
			mat4 MvpInverse;
		};

		void main() 
		{
			gl_Position = vec4(vertexPosition, 1.0);

			if (CLIP_SPACE_Y_INVERTED == 1)
			{
				gl_Position.y *= -1.0;
			}

            // vertexPosition is in screen space, convert it into world space
            Position = (MvpInverse * vec4(vertexPosition.xy, 1.0, 1.0)).xyz;
			TexCoords = vertexTexCoord;
		}
	ENDPROGRAM

	PROGRAM FRAGMENT	
        layout(location = 0) in vec3 Position;
		layout(location = 1) in vec2 TexCoords;

		layout(location = 0) out vec4 OutputColor;

		layout(set = 1, binding = 0, std140) uniform ResourceBuffer
		{
			vec3 CameraPosition;
			vec4 GridColor;

			vec3 PlaneNormal;
			vec3 PlaneRight;
			vec3 PlaneUp;

			float PrimaryGridSize;
			float LineWidth;
			float SecondaryGridSize; 
		};
		
		// https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8
		float pristineGrid(in vec2 uv, vec2 lineWidth)
		{
			vec2 ddx = dFdx(uv);
			vec2 ddy = dFdy(uv);

			vec2 uvDeriv = vec2(length(vec2(ddx.x, ddy.x)), length(vec2(ddx.y, ddy.y)));
			
			bvec2 invertLine = bvec2(lineWidth.x > 0.5, lineWidth.y > 0.5);
			
			vec2 targetWidth = vec2(
				invertLine.x ? 1.0 - lineWidth.x : lineWidth.x,
				invertLine.y ? 1.0 - lineWidth.y : lineWidth.y
			);
			
			vec2 drawWidth = clamp(targetWidth, uvDeriv, vec2(0.5));
			
			vec2 lineAA = uvDeriv * 1.5;
			
			vec2 gridUV = abs(fract(uv) * 2.0 - 1.0);
			
			gridUV.x = invertLine.x ? gridUV.x : 1.0 - gridUV.x;
			gridUV.y = invertLine.y ? gridUV.y : 1.0 - gridUV.y;
			
			vec2 grid2 = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);
		
			grid2 *= clamp(targetWidth / drawWidth, 0.0, 1.0);
			grid2 = mix(grid2, targetWidth, clamp(uvDeriv * 2.0 - 1.0, 0.0, 1.0));
			
			grid2.x = invertLine.x ? 1.0 - grid2.x : grid2.x;
			grid2.y = invertLine.y ? 1.0 - grid2.y : grid2.y;
			
			return mix(grid2.x, 1.0, grid2.y);
		}
		
		float Grid(vec3 ro, float scale, vec3 rd, float lineWidth, out float d) 
		{
			ro /= scale;

			float ndotd = dot(-PlaneNormal, rd);

			if (ndotd == 0.0)
			    return 0.0; 

			d = dot(PlaneNormal, ro) / ndotd;

			if (d <= 0.0)
			    return 0.0; 

			vec3 hit = ro + rd * d;

			float u = dot(hit, PlaneRight);
			float v = dot(hit, PlaneUp);
			
			return pristineGrid(vec2(u, v), vec2(lineWidth * LineWidth));
		}

		void main()
		{
			float d = 0.0;
			float bd = 0.0;

			float sg = Grid(CameraPosition, PrimaryGridSize, normalize(Position), 0.02, d);
			float bg = Grid(CameraPosition, SecondaryGridSize, normalize(Position), 0.02, bd);
		
			if (abs(dot(normalize(Position), vec3(0.0, 1.0, 0.0))) > 0.005)
			{ 
				OutputColor = vec4(GridColor.xyz, sg);
				OutputColor += vec4(GridColor.xyz, bg * 0.5);
            }
		}
	ENDPROGRAM
}