#version 460 core

layout(location=0) out vec4 FragColor;

#if __VERSION__ == 460
const float version_match = 1.0;
#else
const float version_match = 0.0;
#endif

#ifdef GL_core_profile
const float core_profile_flag = 1.0;
#else
const float core_profile_flag = 0.0;
#endif

const int line_number = __LINE__;

void main()
{
    FragColor = vec4(version_match, core_profile_flag, fract(float(line_number) / 10.0), 1.0);
}
