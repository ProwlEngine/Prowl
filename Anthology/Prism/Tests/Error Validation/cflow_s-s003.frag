#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
layout(constant_id=1) const float F = 1.5; const float G = F*2.0; void main(){ o=vec4(G); }
