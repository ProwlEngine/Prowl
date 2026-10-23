#version 450
layout(set=0, binding=0) uniform texture2D tex;
layout(set=0, binding=1) uniform sampler smp;
layout(location=0) in vec2 uv;
layout(location=0) out vec4 o;
vec4 Read(sampler2D s, vec2 p) { vec2 d = dFdx(p); return textureGrad(s, p, d, d); }
vec4 Read_split1(texture2D s_texture, sampler s_sampler, vec2 p) { vec2 d = dFdx(p); return textureGrad(sampler2D(s_texture, s_sampler), p, d, d); }
vec4 Twice(sampler2D s, vec2 p) { return Read(s, p) + Read(s, p * 2.0); }
vec4 Twice_split0(texture2D s_texture, sampler s_sampler, vec2 p) { return Read_split1(s_texture, s_sampler, p) + Read_split1(s_texture, s_sampler, p * 2.0); }
void main() { o = Twice_split0(tex, smp, uv); }
