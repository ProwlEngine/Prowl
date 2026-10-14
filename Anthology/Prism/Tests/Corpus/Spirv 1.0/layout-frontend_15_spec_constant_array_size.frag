#version 460 core

// Specialization constant used as an array size.

layout(constant_id = 0) const int TAPS = 4;

layout(location = 0) in vec4 v_color;
layout(location = 0) out vec4 fragColor;

void main()
{
    float weights[TAPS];
    vec4 acc = vec4(0.0);
    for (int i = 0; i < TAPS; i++)
    {
        weights[i] = 1.0 / float(TAPS) * float(i + 1);
        acc += v_color * weights[i];
    }
    fragColor = acc;
}
