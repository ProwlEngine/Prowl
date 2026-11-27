#version 460 core

// GTAO - Composite pass - fullscreen triangle passthrough

layout (location = 0) in vec3 vertexPosition;
layout (location = 1) in vec2 vertexTexCoord;

layout(location = 2) out vec2 TexCoords;

void main()
{
    gl_Position = vec4(vertexPosition, 1.0);
    TexCoords = vertexTexCoord;
}
