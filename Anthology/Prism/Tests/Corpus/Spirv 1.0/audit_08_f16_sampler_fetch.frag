#version 450
#extension GL_AMD_gpu_shader_half_float_fetch : require
#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require
layout(set=0,binding=0) uniform f16sampler2D s;
layout(set=0,binding=1, rgba16f) uniform f16image2D img;
layout(location=0) in vec2 uv;
layout(location=0) out vec4 o;
void main() { f16vec4 t = texture(s, uv); f16vec4 i = imageLoad(img, ivec2(0)); o = vec4(t + i); }
