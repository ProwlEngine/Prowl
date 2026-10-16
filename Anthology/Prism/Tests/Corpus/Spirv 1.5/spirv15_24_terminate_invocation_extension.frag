#version 450
#extension GL_EXT_terminate_invocation : require
layout(location = 0) out vec4 o;
void main() { if (gl_FragCoord.x > 1.0) terminateInvocation; o = vec4(1.0); }
