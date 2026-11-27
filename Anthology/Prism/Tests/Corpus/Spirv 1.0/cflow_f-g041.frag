#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
layout(std140,binding=0) uniform U { vec4 uv; float uf; int uiv; } ub;
layout(std430,binding=1) buffer SB { vec4 sv; float sf; int si[4]; } sb;
float texture(float x){ return 5.0; } void main(){ o=vec4(texture(1.0)); }
