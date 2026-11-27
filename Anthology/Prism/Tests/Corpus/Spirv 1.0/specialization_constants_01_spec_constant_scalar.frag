#version 460 core

// Specialization constants of different scalar types used in real arithmetic and branching.

layout(constant_id = 0) const float EXPOSURE = 1.0;
layout(constant_id = 1) const int TONE_MODE = 0;
layout(constant_id = 2) const bool USE_GAMMA_CORRECTION = true;
layout(constant_id = 3) const uint DEBUG_CHANNEL = 0u;

layout(location = 0) in vec3 inColor;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec3 color = inColor * EXPOSURE;

    if (TONE_MODE == 1)
    {
        color = color / (color + vec3(1.0));
    }
    else
    {
        color = clamp(color, vec3(0.0), vec3(1.0));
    }

    if (USE_GAMMA_CORRECTION)
    {
        color = pow(color, vec3(1.0 / 2.2));
    }

    if (DEBUG_CHANNEL == 1u)
    {
        color = vec3(color.r);
    }
    else if (DEBUG_CHANNEL == 2u)
    {
        color = vec3(color.g);
    }

    FragColor = vec4(color, 1.0);
}
