#version 450
#extension GL_EXT_demote_to_helper_invocation : require
layout(location = 0) out vec4 o;
void main() { if (gl_FragCoord.x > 1.0) demote; o = vec4(float(helperInvocationEXT())); }
