#version 450
#extension GL_EXT_texture_shadow_lod : require
layout(set=0,binding=0) uniform samplerCubeArrayShadow s;
layout(set=0,binding=1) uniform sampler2DArrayShadow a;
layout(location=0) in vec4 p;
layout(location=0) out vec4 o;
void main() { o = vec4(textureLod(s, p, 0.5, 1.0) + texture(s, p, 0.5, 0.25) + textureLod(a, p, 1.0)); }
