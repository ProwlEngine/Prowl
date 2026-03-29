#version 450
#extension GL_EXT_samplerless_texture_functions : enable

layout(set = 0, binding = 0) uniform utexture2D Tex;

layout(location = 0) out vec4 fsout_Color0;

void main()
{
    uvec4 data = texelFetch(Tex, ivec2(50, 50), 0);
	fsout_Color0 = vec4(data);
}
