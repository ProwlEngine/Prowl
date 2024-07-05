Shader "Default/Depth"

Pass
{
	Cull Front

	PROGRAM VERTEX
		layout (location = 0) in vec3 vertexPosition;

		layout (set = 0, binding = 0) uniform mvpBuffer
		{
			mat4 mvp;
		};

		void main()
		{
		    gl_Position =  mvp * vec4(vertexPosition, 1.0);
		}
	ENDPROGRAM

	PROGRAM FRAGMENT
		layout (location = 0) out float fragmentdepth;
		
		//uniform sampler2D _MainTex; // diffuse

		void main()
		{
			//if(texture(_MainTex, TexCoords).a < 0.5) discard;
			// Not really needed, OpenGL does it anyway
			//fragmentdepth = gl_FragCoord.z;
		}
	ENDPROGRAM
}