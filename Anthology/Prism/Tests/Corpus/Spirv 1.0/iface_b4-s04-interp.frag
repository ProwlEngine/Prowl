#version 450
layout(location=0) flat in ivec2 a;
layout(location=1) noperspective in vec3 b;
layout(location=2) centroid in vec2 c;
layout(location=3) sample in float d;
layout(location=4) centroid noperspective in vec4 e[2];
layout(location=6, component=1) in vec2 f;
layout(location=6, component=3) in float g;
layout(location=7) in Blk { flat int x; layout(location=9) vec2 y; centroid float z; } blk;
layout(location=0) out vec4 o;
layout(location=1, index=0) out vec4 o2;
void main(){ o = vec4(a,0,0) + vec4(b, c.x) + vec4(d) + e[1] + vec4(f, g, 0) + vec4(blk.x, blk.y, blk.z); o2 = vec4(interpolateAtCentroid(c), interpolateAtSample(d, 1), interpolateAtOffset(b.x, vec2(0.1))); }
