#version 450
#extension GL_ARB_post_depth_coverage : require
layout(post_depth_coverage) in;
layout(location=0) out vec4 o;
void main() { o = vec4(gl_SampleMaskIn[0]); }
