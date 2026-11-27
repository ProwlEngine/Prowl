#version 460
#extension GL_AMD_shader_early_and_late_fragment_tests : require
// glslang spells this layout id early_and_late_fragment_tests_amd
layout(early_and_late_fragment_tests_amd) in;
layout(location = 0) out vec4 oColor;
void main()
{
    oColor = vec4(1.0, 0.5, 0.0, 1.0);
}
