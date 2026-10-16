#version 460 core

layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in vec4 aColor;

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    mat4 projection;
};

layout(location = 3) out vec2 fragTexCoord;
layout(location = 4) out vec4 fragColor;
layout(location = 5) out vec2 fragPos;

void main()
{
    fragTexCoord = aTexCoord;
    fragColor = aColor;
    fragPos = aPosition;
    gl_Position = projection * vec4(aPosition, 0.0, 1.0);
}
