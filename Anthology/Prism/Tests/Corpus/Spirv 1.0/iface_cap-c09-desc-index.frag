#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(binding=0) uniform sampler2D tex[];
layout(binding=1) uniform texture2D t2[8];
layout(binding=2) uniform sampler s;
layout(binding=3, rgba8) uniform image2D imgs[];
layout(std140, binding=4) uniform U { vec4 c; } ubos[];
layout(std430, binding=5) buffer B { vec4 d; } ssbos[];
layout(binding=6) uniform samplerBuffer tb[];
layout(binding=7, r32f) uniform imageBuffer ib[];
layout(input_attachment_index=0, binding=8) uniform subpassInput sp[2];
layout(location=0) flat in int i;
layout(location=0) out vec4 o;
void main(){ o = texture(tex[nonuniformEXT(i)], vec2(0)) + texture(sampler2D(t2[nonuniformEXT(i)], s), vec2(0)) + imageLoad(imgs[nonuniformEXT(i)], ivec2(0)) + ubos[nonuniformEXT(i)].c + ssbos[nonuniformEXT(i)].d + texelFetch(tb[nonuniformEXT(i)], 0) + imageLoad(ib[nonuniformEXT(i)], 0) + subpassLoad(sp[i]); }
