#shader vertex
#version 330

#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoords;

out vec2 uv;

void main()
{
    float x = -1.0 + float((gl_VertexID & 1) << 2);
    float y = -1.0 + float((gl_VertexID & 2) << 1);
    uv.x = (x+1.0)*0.5;
    uv.y = (y+1.0)*0.5;
    gl_Position = vec4(x, y, 0, 1);
}

#shader fragment
#version 330

out vec4 FragColor;
in vec2 uv;

uniform sampler2D texture0; // Lighting
uniform sampler2D texture1; // Diffuse

out vec4 finalColor;

void main()
{
    vec3 diffuseColor = texture(texture1, fragTexCoord).rgb;
    vec3 lightingColor = texture(texture0, fragTexCoord).rgb;

    // Apply gamma correction to the lighting color
    float gamma = 2.2;
    vec3 correctedLighting = pow(lightingColor.rgb, vec3(1.0 / gamma));
    
    FragColor = vec4(diffuseColor * correctedLighting, 1.0);
}
