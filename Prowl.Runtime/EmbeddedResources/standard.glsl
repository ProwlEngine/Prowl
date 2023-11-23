#shader vertex
#version 330

layout (location = 0) in vec3 position;
layout (location = 2) in vec2 uv;
layout (location = 3) in vec3 normal;

out vec3 fragNormal;
out vec3 fragPosition;
out vec2 fragTexCoord;

uniform mat4 u_Model;
uniform mat4 u_View;
uniform mat4 u_Projection;


void main()
{
    vec4 worldPosition = u_Projection * u_View * u_Model * vec4(position, 1.0);
    //vec4 positionRelativeToCam = u_View * worldPosition;

    fragPosition = position;
    fragNormal   = normal;
    fragTexCoord = uv;
       
    gl_Position = u_Projection * u_View * u_Model * vec4(position, 1.0);
}


#shader fragment
#version 330

layout (location = 0) out vec4 gBuffer_albedospec;
layout (location = 1) out vec3 gBuffer_normal;
layout (location = 2) out float gBuffer_depth;

in vec2 fragTexCoord;
in vec3 fragPosition;
in vec3 fragNormal;

uniform vec3 Camera_WorldPosition;
uniform vec3 Camera_NearFarFOV;

uniform sampler2D texture0; // diffuse
uniform sampler2D texture1; // specular
//uniform sampler2D texture2; // normals
//uniform sampler2D texture3; // emissive
uniform vec3 main_color; // color

void main()
{
    gBuffer_albedospec.rgb = texture(texture0, fragTexCoord).rgb * main_color;
    gBuffer_albedospec.a = texture(texture1, fragTexCoord).r;
	
    gBuffer_normal = normalize(fragNormal) * 0.5 + 0.5;
    
    gBuffer_depth = length(fragPosition - Camera_WorldPosition) / Camera_NearFarFOV.y;
}
