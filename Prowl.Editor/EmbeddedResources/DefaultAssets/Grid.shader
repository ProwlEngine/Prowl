Shader "Default/Grid"

Pass 0
{
	DepthTest Off
	DepthWrite Off
	Blend On
	BlendSrc SrcAlpha
	BlendDst OneMinusSrcAlpha
	BlendEquation FuncAdd
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
		
		float Grid(vec3 ro, vec3 rd, out float d) {
			d = -ro.y / rd.y;
			if (d <= 0.0) return 0.0;
			
			vec2 p = (ro.xz + rd.xz * d) * 2.0;
			vec2 e = fwidth(p);
			vec2 grid = abs(fract(p) - 0.5);
			vec2 lines = smoothstep(0.5 * e, e, grid);
			
			float line = min(lines.x, lines.y);
			
			// Distance fade
			float fadeDist = 128.0; // Adjust this value to control the fade distance
			float fadeAmount = 1.0 - clamp(d / fadeDist, 0.0, 1.0);
			
			return mix(0.9, 0.0, line) * fadeAmount;
		}

		void main()
		{
            vec3 gPos = textureLod(gPositionRoughness, TexCoords, 0).rgb;
			
			float d = 0.0;
			float g = Grid(Camera_WorldPosition * 0.5, normalize(vPosition), d);
			
			float depth = length(gPos.xyz) * 0.5;
			
			if(depth > d || depth == 0.0)
			{
				OutputColor = vec4(1.0, 1.0, 1.0, g);
            }
		}

	}
}