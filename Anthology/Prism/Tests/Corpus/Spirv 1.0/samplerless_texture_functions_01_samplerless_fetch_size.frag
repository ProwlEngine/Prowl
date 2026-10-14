#version 450
#extension GL_EXT_samplerless_texture_functions : require
layout(set=0,binding=0) uniform texture2D t;
layout(location=0) out vec4 o;
void main() { o = texelFetch(t, ivec2(0), 0) + vec4(textureSize(t, 0), 0, 0); }
