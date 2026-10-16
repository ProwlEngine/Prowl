#version 450
layout(location=0) out vec4 c;
float g[];
float g[4];
void main(){ g[2] = 1.0; c = vec4(g.length()); }
