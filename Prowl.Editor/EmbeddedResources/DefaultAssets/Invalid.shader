Shader "Default/Invalid"

Properties
{
}

Pass 0
{
	RenderMode "Opaque"

	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;
#ifdef HAS_NORMALS
		layout (location = 3) in vec3 vertexNormal;
#else
		vec3 vertexNormal = vec3(0.0, 1.0, 0.0);
#endif

		out vec3 FragPos;
		out vec3 VertNormal;
		out vec4 PosProj;
		out vec4 PosProjOld;
		
		uniform mat4 matModel;
		uniform mat4 matView;
		uniform mat4 mvp;
		uniform mat4 mvpOld;

		void main()
		{
		 	vec4 viewPos = matView * matModel * vec4(vertexPosition, 1.0);
			VertNormal = (matView * vec4(vertexNormal, 0.0)).xyz;
		    FragPos = viewPos.xyz; 

		    PosProj = mvp * vec4(vertexPosition, 1.0);
		    PosProjOld = mvpOld * vec4(vertexPosition, 1.0);
		
		    gl_Position = PosProj;
		}
	}

	Fragment
	{
		layout (location = 0) out vec4 gAlbedoAO; // AlbedoR, AlbedoG, AlbedoB, Ambient Occlusion
		layout (location = 1) out vec4 gNormalMetallic; // NormalX, NormalY, NormalZ, Metallic
		layout (location = 2) out vec4 gPositionRoughness; // PositionX, PositionY, PositionZ, Roughness
		layout (location = 4) out vec2 gVelocity; // VelocityX, VelocityY
		layout (location = 5) out float gObjectID; // ObjectID

		in vec3 FragPos;
		in vec3 VertNormal;
		in vec4 PosProj;
		in vec4 PosProjOld;

		uniform vec2 Jitter;
		uniform vec2 PreviousJitter;
		uniform int ObjectID;
		
		void main()
		{
			gAlbedoAO = vec4(1.0, 0.0, 1.0, 0.0);
			gPositionRoughness = vec4(FragPos, 0.5);
			gNormalMetallic = vec4(VertNormal, 0.5);

			// Velocity
			vec2 a = (PosProj.xy / PosProj.w) - Jitter;
			vec2 b = (PosProjOld.xy / PosProjOld.w) - PreviousJitter;
			gVelocity.xy = (b - a) * 0.5;

			gObjectID = float(ObjectID);
		}
	}
}

			
ShadowPass 0
{
	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;
		
		uniform mat4 mvp;
		void main()
		{
		    gl_Position =  mvp * vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout (location = 0) out float fragmentdepth;
		
		void main()
		{
		}
	}
}