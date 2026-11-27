#version 450
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;
layout(set=0,binding=0) uniform U { int ui; uint uu; float uf; vec4 uv; } u;

void main(){ float n = 0.0/0.0; o=vec4(min(n,1.0), max(n,1.0), clamp(n,0.0,1.0), mix(n,1.0,0.0)); }
