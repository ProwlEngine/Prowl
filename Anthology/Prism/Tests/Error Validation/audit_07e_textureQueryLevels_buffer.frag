#version 450
layout(set=0,binding=0) uniform samplerBuffer s;
layout(location=0) out vec4 o;
void main() { o = vec4(textureQueryLevels(s)); }
