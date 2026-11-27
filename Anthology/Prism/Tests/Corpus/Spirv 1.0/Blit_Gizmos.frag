#version 460 core

layout (location = 0) out vec4 finalColor;

layout(location = 1) in vec2 TexCoords;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

void main()
{
	finalColor = texture(_MainTex, TexCoords).rgba;
}
