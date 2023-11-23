#shader vertex
#version 330

// Input vertex attributes
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in vec4 vertexColor;

// Input uniform values
uniform mat4 mvp;
uniform mat4 matModel;
// uniform vec3 vertexNormal;

// Output vertex attributes (to fragment shader)
out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragNormal;
out vec3 fragPos;
out vec2 clipSpacePosition;

// NOTE: Add here your custom variables 

void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    
    mat3 normalMatrix = transpose(inverse(mat3(matModel)));
    fragNormal = normalize(normalMatrix*vertexNormal);
    
    fragPos = vec3(matModel*vec4(vertexPosition, 1.0));
    
    gl_Position = mvp*vec4(vertexPosition, 1.0);
	vec4 pos = gl_Position;
	pos.xyz /= pos.w;
	clipSpacePosition.x = pos.x * 0.5 + 0.5;
    clipSpacePosition.y = pos.y * 0.5 + 0.5;
}


#shader fragment
#version 330

layout (location = 0) out vec4 gBuffer_lighting;

in vec2 fragTexCoord;
in vec3 fragPos;
in vec3 fragNormal;
in vec2 clipSpacePosition;

uniform vec2 Resolution;
uniform vec3 Camera_WorldPosition;
uniform vec3 Camera_NearFarFOV;

uniform vec3 LightDirection;
uniform vec3 LightColor;
uniform float LightIntensity;
uniform vec3 LightAmbientColor;
uniform float LightAmbientIntensity;

uniform sampler2D texture0; // Normal
uniform sampler2D texture1; // Depth

void main()
{
	vec2 fragCoord = gl_FragCoord.xy / Resolution;

    float depth = texture(texture1, fragCoord).x * Camera_NearFarFOV.y;
    if(depth <= 0.0)
    {
        gBuffer_lighting = vec4(0,0,0,0);
        return;
    }
	
    vec3 normal = texture(texture0, fragCoord).rgb * 2.0 - 1.0;
	
    vec3 toLight = normalize(-LightDirection);
	
    float nDotL = max(dot(normal, toLight),0.0);
	
    vec3 diffuseLight = LightColor * nDotL;
	
    gBuffer_lighting = vec4(LightIntensity * diffuseLight, 1.0);
}
