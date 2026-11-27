#version 450
layout(location=0) flat in int fi;
layout(location=1) noperspective in float np;
layout(location=2) centroid in vec2 ce;
layout(location=3) sample in vec2 sa;
layout(location=0) out vec4 o;
void main(){ o = vec4(float(fi)+np+ce.x+sa.y); }
