Shader "Default/Standard"

Properties
{
	// Material property declarations go here
	_MainTex("Albedo Map", TEXTURE2D)
	_NormalTex("Normal Map", TEXTURE2D)
	_RoughnessTex("Roughness Map", TEXTURE2D)
	_MetallicTex("Metallic Map", TEXTURE2D)
	_EmissionTex("Emission Map", TEXTURE2D)
	_OcclusionTex("Occlusion Map", TEXTURE2D)

	_MainColor("Main Color", VEC4)

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
		layout (location = 3) in vec4 vertexColor;
		layout (location = 4) in vec4 vertexTangent;

		out vec3 FragPos;
		out vec3 Pos;
		out vec2 TexCoords;
		out vec3 VertNormal;
		out vec4 VertColor;
		//out mat3 TBN;
		out vec4 PosProj;
		out vec4 PosProjOld;
		
		uniform mat4 matModel;
		uniform mat4 matView;
		uniform mat4 mvp;
		uniform mat4 mvpOld;

		void main()
		{
		    /*
		    * Position and Normal are in view space
		    */
		 	vec4 viewPos = matView * matModel * vec4(vertexPosition, 1.0);
		    Pos = (matModel * vec4(vertexPosition, 1.0)).xyz;
		    FragPos = viewPos.xyz; 
		    TexCoords = vertexTexCoord;
		    VertColor = vertexColor;

			mat3 normalMatrix = transpose(inverse(mat3(matModel)));
			VertNormal = normalize(normalMatrix * vertexNormal);

		    //vec3 n = normalize((matModel * vec4(vertexNormal, 0.0)).xyz);
		    //vec3 t = normalize((matModel * vec4(vertexTangent.rgb, 0.0)).xyz);
		    //t = normalize(t - dot(t, n) * n);
		    
		    //vec3 bitangent = cross(n, t);
		    //TBN = mat3(t, bitangent, n);
		
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
		layout (location = 3) out vec3 gEmission; // EmissionR, EmissionG, EmissionB, 
		layout (location = 4) out vec2 gVelocity; // VelocityX, VelocityY

		in vec3 FragPos;
		in vec3 Pos;
		in vec2 TexCoords;
		in vec3 VertNormal;
		in vec4 VertColor;
		//in mat3 TBN;
		in vec4 PosProj;
		in vec4 PosProjOld;

		uniform mat4 matView;
		uniform vec3 Camera_WorldPosition;
		uniform float emissionIntensity = 1.0;
		
		uniform sampler2D _MainTex; // diffuse
		uniform sampler2D _NormalTex; // Normal
		uniform sampler2D _RoughnessTex; // Roughness
		uniform sampler2D _MetallicTex; // Metallic
		uniform sampler2D _EmissionTex; // Emissive
		uniform sampler2D _OcclusionTex; // Ambient Occlusion
		uniform vec4 _MainColor; // color

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

			// Occlusion
			float ao = texture(_OcclusionTex, TexCoords).r;
			// Albedo
			//gAlbedoAO = vec4(alb.xyz * _MainColor.rgb, ao);
			gAlbedoAO = vec4(pow(alb.xyz * _MainColor.rgb, vec3(2.2)), ao);
	
			// Position & Roughness
			gPositionRoughness = vec4(FragPos, texture(_RoughnessTex, TexCoords).r);

			// Normal & Metallic
			//vec4 normal = vec4(TBN * (texture(_NormalTex, TexCoords).rgb * 2.0 - 1), 0);
			//gNormalMetallic = vec4((view * normal).rgb, texture(_MetallicTex, TexCoords).r);
			// ^ Produces NAN?
			gNormalMetallic = vec4((matView * vec4(getNormalFromMap(), 0)).rgb, texture(_MetallicTex, TexCoords).r);
			
			// Emission
			gEmission.rgb = texture(_EmissionTex, TexCoords).rgb * emissionIntensity;

			// Velocity
			vec2 a = (PosProj.xy / PosProj.w) * 0.5 + 0.5;
			vec2 b = (PosProjOld.xy / PosProjOld.w) * 0.5 + 0.5;
			gVelocity.xy = a - b;

		}
	}
}