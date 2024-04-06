Shader "Default/Bloom"

Pass 0
{
	DepthTest Off
	DepthWrite Off
	// DepthMode Less
	Blend On
	BlendSrc SrcAlpha
	BlendDst OneMinusSrcAlpha
	BlendMode Add
	Cull Off
	// Winding CW

	Vertex
	{
		in vec3 vertexPosition;
		in vec2 vertexTexCoord;
		
		out vec2 TexCoords;

		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
			TexCoords = vertexTexCoord;
		}
	}

	Fragment
	{
		layout(location = 0) out vec4 OutputColor;
		
		in vec2 TexCoords;
		uniform vec2 Resolution;
		
		uniform sampler2D gColor;

		uniform float u_Radius;
		uniform float u_Threshold;
		uniform float u_Alpha;
		
		// ----------------------------------------------------------------------------
		
		void main()
		{
			// Kawase Blur
			vec2 ps = (vec2(1.0, 1.0) / Resolution) * u_Radius;
			vec3 thres = vec3(u_Threshold, u_Threshold, u_Threshold);
			vec3 zero = vec3(0.0, 0.0, 0.0);
			vec3 color = max(texture(gColor, TexCoords).rgb - thres, zero);
            color += max(texture(gColor, TexCoords + vec2(ps.x, ps.y)).rgb - thres, zero);
            color += max(texture(gColor, TexCoords + vec2(ps.x, -ps.y)).rgb - thres, zero);
            color += max(texture(gColor, TexCoords + vec2(-ps.x, ps.y)).rgb - thres, zero);
            color += max(texture(gColor, TexCoords + vec2(-ps.x, -ps.y)).rgb - thres, zero);
			color /= 5.0;


			OutputColor = vec4(color, u_Alpha);
		}

	}
}