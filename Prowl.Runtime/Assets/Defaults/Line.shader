Shader "Default/Line"

Properties
{
    _MainTex ("Texture", Texture2D) = "white"
    _StartColor ("Start Color", Color) = (1.0, 1.0, 1.0, 1.0)
    _EndColor ("End Color", Color) = (1.0, 1.0, 1.0, 1.0)
}

Pass "Line"
{
    Tags { "RenderOrder" = "Transparent" }
    Cull Off
    Blend Alpha

	GLSLPROGRAM
		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 currentPos;
			out vec4 previousPos;
			out float fogCoord;
			out vec4 vColor;

			void main()
			{
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
				fogCoord = gl_Position.z;
				currentPos = gl_Position;
				texCoord0 = vertexTexCoord0;

				vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0);
				previousPos = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;

				worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
				vColor = vertexColor;
			}
		}

		Fragment
		{
            #include "ProwlCG"

			layout (location = 0) out vec4 gAlbedo;
			layout (location = 1) out vec4 gMotionVector;
			layout (location = 2) out vec4 gNormal;
			layout (location = 3) out vec4 gSurface;

			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 currentPos;
			in vec4 previousPos;
			in float fogCoord;
			in vec4 vColor;

			uniform sampler2D _MainTex;

			void main()
			{
				vec2 curNDC = (currentPos.xy / currentPos.w) - _CameraJitter;
				vec2 prevNDC = (previousPos.xy / previousPos.w) - _CameraPreviousJitter;
			    gMotionVector = vec4((curNDC - prevNDC) * 0.5, 0.0, 1.0);

				vec4 albedo = texture(_MainTex, texCoord0) * vColor;

				// Lines don't have meaningful normals in billboarded mode
                gNormal = vec4(0.0, 0.0, 1.0, 1.0);

				// Unlit surface properties
				gSurface = vec4(1.0, 0.0, 0.0, 1.0);

				vec3 baseColor = albedo.rgb;
				baseColor.rgb = gammaToLinearSpace(baseColor.rgb);

				gAlbedo = vec4(baseColor, albedo.a);
				gAlbedo.rgb = ApplyFog(fogCoord, gAlbedo.rgb);
			}
		}
	ENDGLSL
}
