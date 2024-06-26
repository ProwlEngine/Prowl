Shader "Default/Grid"

Pass 0
{
	DepthTest Off
	DepthWrite Off
	Blend On
	BlendSrc SrcAlpha
	BlendDst OneMinusSrcAlpha
	BlendMode Add
	Cull Off

	Vertex
	{
		in vec3 vertexPosition;
		in vec2 vertexTexCoord;
		
		out vec2 TexCoords;
        out vec3 vPosition;

		uniform mat4 mvpInverse;

		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
            // vertexPosition is in screen space, convert it into world space
            vPosition = (mvpInverse * vec4(vertexPosition, 1.0)).xyz;
			TexCoords = vertexTexCoord;
		}
	}

	Fragment
	{
		layout(location = 0) out vec4 OutputColor;
		
        in vec3 vPosition;
		in vec2 TexCoords;

		uniform vec2 Resolution;
		uniform vec3 Camera_WorldPosition;
		
		uniform sampler2D gPositionRoughness; // Pos
		
		uniform float u_lineWidth; 
		uniform float u_primaryGridSize; 
		uniform float u_secondaryGridSize; 
		
		// https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8
		float pristineGrid( in vec2 uv, vec2 lineWidth)
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
		
		float Grid(vec3 ro, float scale, vec3 rd, float lineWidth, out float d) {
			ro /= scale;
			#ifdef GRID_YZ
			    d = -ro.x / rd.x;
			    if (d <= 0.0) return 0.0;
			    vec2 p = (ro.zy + rd.zy * d);
			#elif defined(GRID_XY)
			    d = -ro.z / rd.z;
			    if (d <= 0.0) return 0.0;
			    vec2 p = (ro.xy + rd.xy * d);
			#else
			    d = -ro.y / rd.y;
			    if (d <= 0.0) return 0.0;
			    vec2 p = (ro.xz + rd.xz * d);
			#endif
			
			return pristineGrid(p, vec2(lineWidth * u_lineWidth));
		}

		void main()
		{
            vec3 gPos = textureLod(gPositionRoughness, TexCoords, 0).rgb;
			
			float d = 0.0;
			float bd = 0.0;
			float sg = Grid(Camera_WorldPosition, u_primaryGridSize, normalize(vPosition), 0.02, d);
			float bg = Grid(Camera_WorldPosition, u_secondaryGridSize, normalize(vPosition), 0.02, bd);
			
			float depth = length(gPos.xyz);
			
			// Do not attempt to move this into Grid() It doesnt work, Why? idk compiler black magic
			bool drawGrid = false;
			#ifdef GRID_YZ
			if(abs(dot(normalize(vPosition), vec3(1.0, 0.0, 0.0))) > 0.005)
				drawGrid = true;
			#elif defined(GRID_XY)
			if(abs(dot(normalize(vPosition), vec3(0.0, 0.0, 1.0))) > 0.005)
				drawGrid = true;
			#else
			if(abs(dot(normalize(vPosition), vec3(0.0, 1.0, 0.0))) > 0.005)
				drawGrid = true;
			#endif
			
			if((depth > d || depth == 0.0) && drawGrid)
			{ 
				OutputColor = vec4(1.0, 1.0, 1.0, sg);
				OutputColor += vec4(1.0, 1.0, 1.0, bg * 0.5);
				//OutputColor *= mix(1.0, 0.0, min(1.0, d / 10000.0));
            }
		}

	}
}