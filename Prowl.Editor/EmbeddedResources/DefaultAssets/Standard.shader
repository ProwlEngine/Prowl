Shader "Default/Standard"

Properties
{
	// Material property declarations go here
	_MainTex("Albedo Map", TEXTURE2D)
	_NormalTex("Normal Map", TEXTURE2D)
	_EmissionTex("Emissive Map", TEXTURE2D)
	_SurfaceTex("Surface Map x:AO y:Rough z:Metal", TEXTURE2D)
	_OcclusionTex("Occlusion Map", TEXTURE2D)

	_EmissiveColor("Emissive Color", COLOR)
	_EmissionIntensity("Emissive Intensity", FLOAT)
	_MainColor("Main Color", COLOR)

	//_ExampleName("Integer display name", INTEGER)
	//_ExampleName("Float display name", FLOAT)
	//
	//_ExampleName("Float with range", FLOAT)
	//_ExampleName("Texture2D display name", TEXTURE2D)
	//
	//_ExampleName("Texture2D display name", TEXTURE2D)
}

Pass 0
{
	RenderMode "Opaque"

	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;
		layout (location = 1) in vec2 vertexTexCoord;
		layout (location = 2) in vec3 vertexNormal;
		layout (location = 3) in vec3 vertexColor;
		layout (location = 4) in vec3 vertexTangent;
#ifdef SKINNING
		layout (location = 5) in ivec4 vertexBoneIndices;
		layout (location = 6) in vec4 vertexBoneWeights;
		
		const int MAX_BONE_INFLUENCE = 4;
		uniform mat4 bindposes[100];
#endif

		out vec3 FragPos;
		out vec3 Pos;
		out vec2 TexCoords;
		out vec3 VertNormal;
		out vec3 VertColor;
		//out mat3 TBN;
		out vec4 PosProj;
		out vec4 PosProjOld;
		
		uniform mat4 matModel;
		uniform mat4 matView;
		uniform mat4 mvp;
		uniform mat4 mvpOld;

		void main()
		{
			vec3 boneVertexPosition = vertexPosition;
			vec3 boneVertexNormal = vertexNormal;
			
#ifdef SKINNING
			for(int i=0; i<MAX_BONE_INFLUENCE; i++) {
				int index = vertexBoneIndices[i];
				if (index != 0) {
					boneVertexPosition += (bindposes[index] * vec4(vertexPosition, 1.0)).xyz * vertexBoneWeights[i];
					boneVertexNormal += (bindposes[index] * vec4(vertexNormal, 0.0)).xyz * vertexBoneWeights[i];
				}
			}
#endif

		    /*
		    * Position and Normal are in view space
		    */
		 	vec4 viewPos = matView * matModel * vec4(boneVertexPosition, 1.0);
		    Pos = (matModel * vec4(boneVertexPosition, 1.0)).xyz;
		    FragPos = viewPos.xyz; 
		    TexCoords = vertexTexCoord;
		    VertColor = vertexColor;

			mat3 normalMatrix = transpose(inverse(mat3(matModel)));
			VertNormal = normalize(normalMatrix * boneVertexNormal);

		    //vec3 n = normalize((matModel * vec4(boneVertexNormal, 0.0)).xyz);
		    //vec3 t = normalize((matModel * vec4(vertexTangent, 0.0)).xyz);
		    //t = normalize(t - dot(t, n) * n);
		    
		    //vec3 bitangent = cross(n, t);
		    //TBN = mat3(t, bitangent, n);
		
		    PosProj = mvp * vec4(boneVertexPosition, 1.0);
		    PosProjOld = mvpOld * vec4(boneVertexPosition, 1.0);
		
		    gl_Position = PosProj;
		}
	}

	Fragment
	{
		layout (location = 0) out vec4 gAlbedoAO; // AlbedoR, AlbedoG, AlbedoB, Ambient Occlusion
		layout (location = 1) out vec4 gNormalMetallic; // NormalX, NormalY, NormalZ, Metallic
		layout (location = 2) out vec4 gPositionRoughness; // PositionX, PositionY, PositionZ, Roughness
		layout (location = 3) out vec3 gEmission; // EmissionR, EmissionG, EmissionB, 
		layout (location = 4) out vec2 gVelocity; // VelocityX, VelocityY
		layout (location = 5) out float gObjectID; // ObjectID

		in vec3 FragPos;
		in vec3 Pos;
		in vec2 TexCoords;
		in vec3 VertNormal;
		in vec3 VertColor;
		//in mat3 TBN;
		in vec4 PosProj;
		in vec4 PosProjOld;

		uniform int ObjectID;

		uniform mat4 matView;
		
		uniform sampler2D _MainTex; // diffuse
		uniform sampler2D _NormalTex; // Normal
		uniform sampler2D _SurfaceTex; // AO, Roughness, Metallic
		uniform sampler2D _EmissionTex; // Emissive
		uniform vec4 _EmissiveColor; // Emissive color
		uniform vec4 _MainColor; // color
		uniform float _EmissionIntensity;

		vec3 getNormalFromMap()
		{
		    vec3 tangentNormal = texture(_NormalTex, TexCoords).xyz * 2.0 - 1.0;
		
		    vec3 Q1  = dFdx(FragPos);
		    vec3 Q2  = dFdy(FragPos);
		    vec2 st1 = dFdx(TexCoords);
		    vec2 st2 = dFdy(TexCoords);
		
		    vec3 N  = normalize(VertNormal);
		    vec3 T  = normalize(Q1*st2.t - Q2*st1.t);
		    vec3 B  = -normalize(cross(N, T));
		    mat3 TBN = mat3(T, B, N);
		
		    return normalize(TBN * tangentNormal);
		}
		
		void main()
		{
			vec4 alb = texture(_MainTex, TexCoords).rgba;
			if(alb.a < 0.5) discard;
			alb.rgb *= VertColor;

			// AO, Roughness, Metallic
			vec3 surface = texture(_SurfaceTex, TexCoords).rgb;
			// Albedo
			//gAlbedoAO = vec4(alb.xyz * _MainColor.rgb, ao);
			gAlbedoAO = vec4(pow(alb.xyz * _MainColor.rgb, vec3(2.2)), surface.r);
	
			// Position & Roughness
			gPositionRoughness = vec4(FragPos, surface.g);

			// Normal & Metallic
			//vec4 normal = vec4(TBN * (texture(_NormalTex, TexCoords).rgb * 2.0 - 1), 0);
			//gNormalMetallic = vec4((view * normal).rgb, surface.b);
			// ^ Produces NAN?
			gNormalMetallic = vec4((matView * vec4(getNormalFromMap(), 0)).rgb, surface.b);
			
			// Emission
			gEmission.rgb = (texture(_EmissionTex, TexCoords).rgb + _EmissiveColor.rgb) * _EmissionIntensity;

			// Velocity
			vec2 a = (PosProj.xy / PosProj.w) * 0.5 + 0.5;
			vec2 b = (PosProjOld.xy / PosProjOld.w) * 0.5 + 0.5;
			gVelocity.xy = a - b;

			gObjectID = float(ObjectID);
		}
	}
}

			
ShadowPass 0
{
	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;
		layout (location = 1) in vec2 vertexTexCoord;
		
		out vec2 TexCoords;

		uniform mat4 mvp;
		void main()
		{
		    gl_Position =  mvp * vec4(vertexPosition, 1.0);
		    TexCoords = vertexTexCoord;
		}
	}

	Fragment
	{
		layout (location = 0) out float fragmentdepth;
		
		uniform sampler2D _MainTex; // diffuse

		in vec2 TexCoords;

		void main()
		{
			if(texture(_MainTex, TexCoords).a < 0.5) discard;

			//fragmentdepth = gl_FragCoord.z;
		}
	}
}