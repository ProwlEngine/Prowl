#version 450
#extension GL_EXT_fragment_invocation_density : require
layout(location=0) out vec4 o;
void main(){ o = vec4(gl_FragSizeEXT, gl_FragInvocationCountEXT, 0); }
