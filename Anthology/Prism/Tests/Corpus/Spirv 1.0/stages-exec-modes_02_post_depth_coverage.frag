#version 460
#extension GL_EXT_post_depth_coverage : require
layout(early_fragment_tests, post_depth_coverage) in;
layout(location = 0) out vec4 oColor;
void main()
{
    oColor = vec4(gl_SampleMaskIn[0], 0.0, 0.0, 1.0);
}
