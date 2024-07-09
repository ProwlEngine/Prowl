Shader "Default/TestShader"

Pass "TestShader"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull None

	Inputs
	{
		VertexInput 
        {
            Position // Input location 0
            UV0 // Input location 1
            Normals // Input location 2
            Tangents // Input location 3
            Colors // Input location 4
        }
        
        // Set 0
        Set
        {
            // Binding 0
            Buffer DefaultUniforms
            {
                Mat_V Matrix4x4
                Mat_P Matrix4x4
                Mat_ObjectToWorld Matrix4x4
                Mat_WorldToObject Matrix4x4
                Mat_MVP Matrix4x4
				Time Float
            }

			SampledTexture _AlbedoTex
			SampledTexture _NormalTex
			SampledTexture _EmissiveTex
			SampledTexture _SurfaceTex

            Buffer StandardUniforms
            {
				_MainColor Vector4 // color
				_AlphaClip Float
				_ObjectID Float
				_EmissiveColor Vector3 // Emissive color
				_EmissionIntensity Float
            }
        }
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		layout(location = 1) in vec2 vertexTexCoord;
		layout(location = 2) in vec3 vertexNormal;
		layout(location = 3) in vec3 vertexTangent;
		layout(location = 4) in vec4 vertexColors;
		
		layout(set = 0, binding = 0, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
		};

		layout(location = 0) out vec2 TexCoords;
		layout(location = 1) out vec4 VertColor;
		layout(location = 2) out vec3 FragPos;
		layout(location = 3) out mat3 TBN;
		
		void main() 
		{
		 	vec4 viewPos = Mat_V * Mat_ObjectToWorld * vec4(vertexPosition, 1.0);
		    FragPos = viewPos.xyz; 

			gl_Position = Mat_MVP * vec4(vertexPosition, 1.0);
			
			TexCoords = vertexTexCoord;
			VertColor = vertexColors;

			mat3 normalMatrix = transpose(inverse(mat3(Mat_ObjectToWorld)));
			
			vec3 T = normalize(vec3(Mat_ObjectToWorld * vec4(vertexTangent, 0.0)));
			vec3 B = normalize(vec3(Mat_ObjectToWorld * vec4(cross(vertexNormal, vertexTangent), 0.0)));
			vec3 N = normalize(vec3(Mat_ObjectToWorld * vec4(vertexNormal, 0.0)));
		    TBN = mat3(T, B, N);
		}
	ENDPROGRAM

	PROGRAM FRAGMENT	
		layout(location = 0) in vec2 TexCoords;
		layout(location = 1) in vec4 VertColor;
		layout(location = 2) in vec3 FragPos;
		layout(location = 3) in mat3 TBN;

		layout(location = 0) out vec4 Albedo;
		layout(location = 1) out vec3 Position;
		layout(location = 2) out vec3 Normal;
		layout(location = 3) out vec3 Emissive;
		layout(location = 4) out vec3 AoRoughnessMetallic;
		layout(location = 5) out uint ObjectID;
		
		layout(set = 0, binding = 0, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
		};

		layout(set = 0, binding = 1) uniform texture2D _AlbedoTex;
		layout(set = 0, binding = 2) uniform sampler _AlbedoTexSampler;

		layout(set = 0, binding = 3) uniform texture2D _NormalTex;
		layout(set = 0, binding = 4) uniform sampler _NormalTexSampler;

		layout(set = 0, binding = 5) uniform texture2D _EmissiveTex;
		layout(set = 0, binding = 6) uniform sampler _EmissiveTexSampler;

		layout(set = 0, binding = 7) uniform texture2D _SurfaceTex;
		layout(set = 0, binding = 8) uniform sampler _SurfaceTexSampler;
		
		
		layout(set = 0, binding = 9, std140) uniform StandardUniforms
		{
			vec4 _MainColor; // color
			float _AlphaClip;
			float _ObjectID;
			vec3 _EmissiveColor; // Emissive color
			float _EmissionIntensity;
		};
		
		#include "Prowl"

		void main()
		{
			// Albedo & Cutout
			vec4 baseColor = texture(sampler2D(_AlbedoTex, _AlbedoTexSampler), TexCoords);// * _MainColor.rgb;
			if(baseColor.w < _AlphaClip) discard;
			Albedo.rgb = pow(baseColor.xyz, vec3(2.2));
			Albedo.a = 1.0;

			// Position
			Position = FragPos;

			// Normal
			vec3 normal = texture(sampler2D(_NormalTex, _NormalTexSampler), TexCoords).rgb;
			normal = normal * 2.0 - 1.0;   
			normal = normalize(TBN * normal); 
			Normal = (Mat_V * vec4(normal, 0)).rgb;

			// Emissive
			vec3 emissiveColor = texture(sampler2D(_EmissiveTex, _EmissiveTexSampler), TexCoords).rgb * _EmissiveColor.rgb * _EmissionIntensity;
			Emissive = emissiveColor;

			// AO, Roughness, Metallic
			vec3 surface = texture(sampler2D(_SurfaceTex, _SurfaceTexSampler), TexCoords).rgb;
			AoRoughnessMetallic = vec3(surface.r, surface.g, surface.b);

			// Object ID
			ObjectID = uint(_ObjectID);
		}
	ENDPROGRAM
}