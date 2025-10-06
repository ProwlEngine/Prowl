Shader "Default/Gizmos"

Properties
{
}

Pass "Gizmos"
{
    Tags { "RenderOrder" = "Opaque" }

    Cull None
    Blend Alpha

	GLSLPROGRAM
	Vertex
	{
        #include "Fragment"
        #include "VertexAttributes"
		out vec4 vColor;
		out vec4 screenPos;
		
		uniform mat4 mvp;
		void main()
		{
			gl_Position = PROWL_MATRIX_VP * vec4(vertexPosition, 1.0);
		    vColor = vertexColor;
		    screenPos = gl_Position;
		}
	}
	Fragment
	{
		in vec4 vColor;
		in vec4 screenPos;
		layout (location = 0) out vec4 finalColor;
        //uniform sampler2D _CameraDepthTexture;
		
		void main()
		{
			// Convert screen position to normalized device coordinates
			//vec2 screenUV = (screenPos.xy / screenPos.w) * 0.5 + 0.5;
			
			// Sample the depth texture
			//float sceneDepth = texture(_CameraDepthTexture, screenUV).r;
			
			// Check if this fragment is behind existing geometry
			//float depthFactor = (gl_FragCoord.z > sceneDepth + 0.0001) ? 0.25 : 1.0;
			
			finalColor = vColor;// * depthFactor;
		}
	}
	ENDGLSL
}