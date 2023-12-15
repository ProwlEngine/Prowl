Shader "Default/Bloom"

Pass 0
{
	Vertex
	{
		in vec3 vertexPosition;

		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout(location = 0) out vec4 OutputColor;
		
		uniform vec2 Resolution;
		
		uniform sampler2D gColor;

		uniform float u_Radius;
		uniform float u_Threshold;
		uniform float u_Alpha;
		
		// ----------------------------------------------------------------------------
		
		void main()
		{
			vec2 texCoords = gl_FragCoord.xy / Resolution;

			// Kawase Blur
			vec2 ps = (vec2(1.0, 1.0) / Resolution) * u_Radius;
			vec3 thres = vec3(u_Threshold, u_Threshold, u_Threshold);
			vec3 zero = vec3(0.0, 0.0, 0.0);
			vec3 color = max(texture(gColor, texCoords).rgb - thres, zero);
            color += max(texture(gColor, texCoords + vec2(ps.x, ps.y)).rgb - thres, zero);
            color += max(texture(gColor, texCoords + vec2(ps.x, -ps.y)).rgb - thres, zero);
            color += max(texture(gColor, texCoords + vec2(-ps.x, ps.y)).rgb - thres, zero);
            color += max(texture(gColor, texCoords + vec2(-ps.x, -ps.y)).rgb - thres, zero);
			color /= 5.0;


			OutputColor = vec4(color, u_Alpha);
		}

	}
}