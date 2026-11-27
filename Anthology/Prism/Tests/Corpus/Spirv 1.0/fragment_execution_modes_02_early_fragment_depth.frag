#version 450
layout(early_fragment_tests) in;
layout(depth_greater) out float gl_FragDepth;
layout(location=0) out vec4 o;
void main() { gl_FragDepth = 0.5; o = vec4(1.0); }
