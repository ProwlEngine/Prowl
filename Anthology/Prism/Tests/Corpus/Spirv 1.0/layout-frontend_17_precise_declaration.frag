#version 460 core

// precise declaration qualifier disables contraction on the computed value.

layout(location = 0) in vec3 v_in;
layout(location = 0) out vec4 fragColor;

void main()
{
    float a = v_in.x;
    float b = v_in.y;
    float c = v_in.z;
    precise float x = a * b + c;
    fragColor = vec4(x);
}
